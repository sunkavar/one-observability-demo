"""Pet Food Recommendation Agent using Amazon Bedrock AgentCore and Strands SDK."""

import os
import boto3
import logging
import requests
from opentelemetry import trace
from opentelemetry.trace import Status, StatusCode
from strands import Agent, tool
from strands.models import BedrockModel
from strands.agent.conversation_manager import SummarizingConversationManager
from bedrock_agentcore.runtime import BedrockAgentCoreApp

logger = logging.getLogger(__name__)

# Configuration
PARAMETER_STORE_PREFIX = os.environ.get("PARAMETER_STORE_PREFIX")
if not PARAMETER_STORE_PREFIX:
    raise RuntimeError("Required environment variable PARAMETER_STORE_PREFIX not set")
MODEL_ID = "us.anthropic.claude-sonnet-4-20250514-v1:0"

# Initialize SSM client
REGION = os.environ.get("AWS_REGION", "us-east-1")
ssm_client = boto3.client("ssm", region_name=REGION)


def get_ssm_parameter(parameter_name: str) -> str:
    """Get parameter value from SSM Parameter Store."""
    try:
        full_parameter_name = f"{PARAMETER_STORE_PREFIX}/{parameter_name}"
        response = ssm_client.get_parameter(Name=full_parameter_name)
        logger.info(f"Retrieving SSM parameter: {full_parameter_name}")
        return response["Parameter"]["Value"]
    except ssm_client.exceptions.ParameterNotFound:
        raise RuntimeError(f"Required SSM parameter not found: {parameter_name}")
    except Exception as e:
        raise RuntimeError(f"Error retrieving SSM parameter {parameter_name}: {e}")


# Fetch API URLs from Parameter Store
search_api_url_parameter_name = os.environ.get("SEARCH_API_URL_PARAMETER_NAME")
if not search_api_url_parameter_name:
    raise RuntimeError(
        "Required environment variable SEARCH_API_URL_PARAMETER_NAME not set",
    )
petfood_api_url_parameter_name = os.environ.get("PETFOOD_API_URL_PARAMETER_NAME")
if not petfood_api_url_parameter_name:
    raise RuntimeError(
        "Required environment variable PETFOOD_API_URL_PARAMETER_NAME not set",
    )
petfood_cart_api_url_parameter_name = os.environ.get("PETFOOD_CART_API_URL_PARAMETER_NAME")
if not petfood_cart_api_url_parameter_name:
    raise RuntimeError(
        "Required environment variable PETFOOD_CART_API_URL_PARAMETER_NAME not set",
    )

search_api_url = get_ssm_parameter(search_api_url_parameter_name)
petfood_api_url = get_ssm_parameter(petfood_api_url_parameter_name)
petfood_cart_api_url = get_ssm_parameter(petfood_cart_api_url_parameter_name)


def get_pet_data(search_query: str):
    """Helper function to get pet data from the search API with proper error handling"""
    if not search_api_url:
        return {"error": "Error: Pet search service not configured"}
    
    try:
        response = requests.get(f"{search_api_url}?q={search_query}", timeout=5)
        
        if response.status_code == 200:
            data = response.json()
            # Check if results are empty - means pet type not found
            if isinstance(data, list) and len(data) == 0:
                return {"error": f"No pets found matching '{search_query}'. We only have puppies, kittens, and bunnies available."}
            return data
        
        # For non-200 status codes, raise an exception to record in span
        response.raise_for_status()
    except requests.HTTPError as e:
        # This will catch 4xx and 5xx errors
        logger.error(f"Pet search service returned {e.response.status_code}: {e}")
        return {"error": f"Error: Pet search service returned {e.response.status_code}"}
    except requests.RequestException as e:
        logger.error(f"Pet search service error: {e}")
        return {"error": "Error: Pet search service unavailable"}


def get_food_data(pet_type: str):
    """Helper function to get food data from the API with proper error handling"""
    if not petfood_api_url:
        return {"error": "Error: Pet food service not configured"}
    
    try:
        # Use query parameter instead of path parameter
        response = requests.get(f"{petfood_api_url}?pet_type={pet_type.lower()}", timeout=5)
        
        if response.status_code == 200:
            data = response.json()
            # Check if foods list is empty
            if isinstance(data, dict) and data.get('foods') is not None:
                if len(data.get('foods', [])) == 0:
                    return {"error": f"No food recommendations available for {pet_type}"}
                return data
            return data
        
        # For non-200 status codes (including 404), raise an exception to record in span
        response.raise_for_status()
    except requests.HTTPError as e:
        # This will catch 4xx and 5xx errors including 404
        logger.error(f"Pet food service returned {e.response.status_code} for pet type '{pet_type}': {e}")
        return {"error": f"Error: No food recommendations available for {pet_type} (service returned {e.response.status_code})"}
    except requests.RequestException as e:
        logger.error(f"Pet food service error: {e}")
        return {"error": "Error: Pet food service unavailable"}


@tool
def search_pets(search_query: str):
    """Search for pets by name, color, type, or characteristics"""
    data = get_pet_data(search_query)
    if "error" in data:
        return data["error"]
    return f"Pet search results: {data}"


@tool
def get_food_recommendations(pet_type: str):
    """Get food recommendations for a specific pet type (puppy, kitten, bunny)"""
    data = get_food_data(pet_type)
    if "error" in data:
        return data["error"]
    
    # API returns {"foods": [...], "total_count": N}
    foods = data.get('foods', [])
    if not foods:
        return f"No food recommendations available for {pet_type}"
    
    # Format the food data for the agent with actual field names from API
    food_list = []
    for food in foods:
        food_info = {
            "id": food.get("id"),
            "name": food.get("name"),
            "type": food.get("food_type"),
            "price": food.get("price"),
            "description": food.get("description"),
            "ingredients": food.get("ingredients", []),
            "feeding_guidelines": food.get("feeding_guidelines"),
            "nutritional_info": food.get("nutritional_info"),
            "availability": food.get("availability_status"),
            "stock": food.get("stock_quantity")
        }
        food_list.append(food_info)
    
    return f"Available {pet_type} foods ({len(foods)} options): {food_list}"


# Global variable to store current user_id for the session
_current_user_id = None


def set_current_user_id(user_id: str):
    """Set the current user ID for the session"""
    global _current_user_id
    _current_user_id = user_id


def get_current_user_id() -> str:
    """Get the current user ID from session or span context"""
    global _current_user_id
    
    # Try to get from global variable first
    if _current_user_id:
        return _current_user_id
    
    # Fallback to span context
    current_span = trace.get_current_span()
    if current_span:
        user_id = current_span.get_attribute("user.id")
        if user_id:
            return user_id
    
    # No user ID available
    return None


@tool
def add_to_cart(food_id: str, quantity: int = 1):
    """Add a food item to the user's cart. Use the food ID from the recommendations."""
    if not petfood_cart_api_url:
        return "Error: Cart service not configured"
    
    user_id = get_current_user_id()
    if not user_id:
        return "Error: User ID not available. Cannot add items to cart without a user ID."
    
    try:
        url = f"{petfood_cart_api_url}/{user_id}/items"
        payload = {"food_id": food_id, "quantity": quantity}
        
        response = requests.post(url, json=payload, timeout=5)
        
        if response.status_code == 201:
            data = response.json()
            return f"Successfully added {data.get('quantity')} x {data.get('name')} to cart. Unit price: ${data.get('unit_price')}, Total: ${data.get('total_price')}"
        
        # For non-201 status codes, raise an exception to record in span
        response.raise_for_status()
    except requests.HTTPError as e:
        logger.error(f"Cart service returned {e.response.status_code}: {e}")
        return f"Error: Could not add item to cart (service returned {e.response.status_code})"
    except requests.RequestException as e:
        logger.error(f"Cart service error: {e}")
        return "Error: Cart service unavailable"


# System prompt
SYSTEM_PROMPT = """You are Waggle, a friendly and knowledgeable pet food recommendation assistant. You're here to help pet parents find the perfect food for their furry companions!

Your process:
1. First get pet details using search_pets tool
2. Then get available foods using get_food_recommendations tool
3. Match pet characteristics (age, size, color, personality traits) with appropriate food types
4. Consider nutritional needs and preferences
5. Provide exactly 2 specific food recommendations with clear reasoning
6. If user wants to add food to cart, use add_to_cart tool with the food ID

When helping users:
- Be conversational and friendly, not formal or robotic
- Always use your tools first - never skip the search step
- If pets are found: Match their characteristics to foods and explain WHY you're recommending specific foods
- If pets aren't found: Politely inform user we specialize in puppies, kittens, and bunnies, and offer to help with those
- When recommending foods, mention the food name so users can easily reference it
- If user wants to purchase or add to cart, use the add_to_cart tool with the food ID
- Ask clarifying questions if you need more information about their pet
- Consider factors like: personality, activity level, color, and preferences
- Be ready to answer follow-up questions or adjust recommendations

Remember: You're having a conversation, not writing a report. Keep responses natural, helpful, and engaging while being informative about pet nutrition! Always sound like a helpful friend, not a robot."""

# Initialize components
app = BedrockAgentCoreApp()

conversation_manager = SummarizingConversationManager(
    summary_ratio=0.5,
    preserve_recent_messages=3,
)

bedrock_model = BedrockModel(
    model_id=MODEL_ID,
)

agent = Agent(
    model=bedrock_model,
    tools=[search_pets, get_food_recommendations, add_to_cart],
    system_prompt=SYSTEM_PROMPT,
    conversation_manager=conversation_manager,
    callback_handler=None,
    trace_attributes={
        "user.email": "demo@example.com",
    },
)


@app.entrypoint
async def pet_food_agent_bedrock(payload):
    """Streaming endpoint for pet food recommendation agent."""
    user_input = payload.get("prompt")
    user_id = payload.get("userId")

    if not user_input:
        yield "Error: No prompt provided in the request."
        return

    # Set the user_id for this session so tools can access it (if provided)
    if user_id:
        set_current_user_id(user_id)
        print(f"User ID: {user_id}, User input: {user_input}")
    else:
        print(f"User input: {user_input}")

    # Add trace attributes for observability
    current_span = trace.get_current_span()
    if current_span:
        current_span.set_attribute("agent.name", "petfood-agent")
        if user_id:
            current_span.set_attribute("user.id", user_id)

    try:
        # Stream response from agent
        async for event in agent.stream_async(user_input):
            if "data" in event:
                yield event["data"]

    except Exception as e:
        error_msg = (
            f"I apologize, but I encountered an error while processing "
            f"your request: {e}"
        )
        logger.error(f"Error in agent execution: {e}")
        
        # Record error in span for telemetry
        if current_span:
            current_span.set_status(Status(StatusCode.ERROR, str(e)))
            current_span.record_exception(e)
        
        yield error_msg


if __name__ == "__main__":
    app.run()
