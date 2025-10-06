from strands import Agent
from strands_tools import http_request
import json
import boto3
import os
from bedrock_agentcore.runtime import BedrockAgentCoreApp
from strands.models import BedrockModel

app = BedrockAgentCoreApp()

# Health check endpoint - required by AgentCore
@app.health_check
def health_check():
    """Health check endpoint required by Bedrock AgentCore"""
    try:
        # Test SSM connectivity
        ssm_client = boto3.client("ssm", region_name=os.environ.get("AWS_REGION", "us-east-1"))
        # Simple test to verify SSM access
        ssm_client.describe_parameters(MaxResults=1)
        
        return {
            "status": "healthy",
            "timestamp": str(boto3.Session().region_name),
            "services": {
                "ssm": "connected",
                "bedrock": "ready"
            }
        }
    except Exception as e:
        print(f"Health check failed: {str(e)}")
        return {
            "status": "unhealthy",
            "error": str(e)
        }

# Get current AWS region
AWS_REGION = os.environ.get("AWS_REGION", boto3.Session().region_name or "us-east-1")

# Initialize SSM client with region
ssm_client = boto3.client("ssm", region_name=AWS_REGION)

# Parameter store prefix - matches CDK deployment
PARAMETER_STORE_PREFIX = "/petstore"


def get_ssm_parameter(parameter_name: str) -> str:
    """Get parameter value from SSM Parameter Store"""
    try:
        full_parameter_name = f"{PARAMETER_STORE_PREFIX}/{parameter_name}"
        response = ssm_client.get_parameter(Name=full_parameter_name)
        return response["Parameter"]["Value"]
    except Exception as e:
        print(f"Error retrieving SSM parameter {parameter_name}: {str(e)}")
        return None


def build_system_prompt() -> str:
    """Build system prompt with API URLs from SSM parameters"""
    search_api_url = get_ssm_parameter("searchapiurl")
    petfood_api_url = get_ssm_parameter("petfoodapiurl")

    prompt = f"""You are Waggle, a friendly and knowledgeable pet food recommendation assistant. You're here to help pet parents find the perfect food for their furry, feathered, or scaled companions!

Your process:
1. First get pet details from {search_api_url}
2. Then get available foods from {petfood_api_url}
3. Match pet characteristics (age, size, breed, health conditions) with appropriate food types
4. Consider nutritional needs, dietary restrictions, and preferences
5. Provide clear reasoning for each recommendation

When helping users:
- Be conversational and friendly, not formal or robotic
- Ask clarifying questions if you need more information about their pet
- First gather pet details (breed, age, size, health conditions, preferences)
- Then fetch available foods and match them to the pet's needs
- Explain WHY you're recommending specific foods (nutritional benefits, breed-specific needs, etc.)
- Consider factors like: life stage, activity level, health conditions, dietary restrictions
- Provide 2-3 specific recommendations with clear reasoning
- Be ready to answer follow-up questions or adjust recommendations

Remember: You're having a conversation, not writing a report. Keep responses natural, helpful, and engaging while being informative about pet nutrition!"""

    return prompt


@app.entrypoint
def pet_food_agent_bedrock(payload):
    """
    Non-streaming endpoint for pet food recommendation agent
    """
    return _process_request(payload, stream=False)


@app.entrypoint
def pet_food_agent_bedrock_stream(payload):
    """
    Streaming endpoint for pet food recommendation agent
    """
    return _process_request(payload, stream=True)


def _process_request(payload, stream=False):
    """
    Process the pet food recommendation request with optional streaming
    """
    user_input = payload.get("prompt")
    print(f"User input: {user_input}")
    print(f"Stream mode: {stream}")

    # Create a fresh agent for each request to avoid conversation state issues
    model_id = "us.anthropic.claude-sonnet-4-20250514-v1:0"
    model = BedrockModel(
        model_id=model_id,
        region_name=AWS_REGION,
    )

    # Build system prompt with API URLs from SSM
    system_prompt = build_system_prompt()

    agent = Agent(
        model=model,
        tools=[http_request],
        system_prompt=system_prompt,
    )

    try:
        if stream:
            # For streaming response
            response_stream = agent.stream(user_input)
            
            # Collect and yield streaming chunks
            for chunk in response_stream:
                if hasattr(chunk, 'message') and chunk.message:
                    content = chunk.message.get('content', [])
                    if content and len(content) > 0 and 'text' in content[0]:
                        yield {"chunk": content[0]['text']}
        else:
            # Non-streaming response (original behavior)
            response = agent(user_input)
            return response.message["content"][0]["text"]
            
    except Exception as e:
        print(f"Error in agent execution: {str(e)}")
        error_msg = f"I apologize, but I encountered an error while processing your request: {str(e)}"
        if stream:
            yield {"chunk": error_msg}
        else:
            return error_msg


if __name__ == "__main__":
    app.run()
