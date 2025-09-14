from strands import Agent, tool
from strands_tools import http_request  # Import the http_request tool
import argparse
import json
from bedrock_agentcore.runtime import BedrockAgentCoreApp
from strands.models import BedrockModel

app = BedrockAgentCoreApp()


# Create custom tools for pet food recommendations
@tool
def search_pets(query: str):
    """Search for pet information using the pets API"""
    import requests

    try:
        url = "http://servic-searc-hynpsggsckqh-2085079762.us-east-2.elb.amazonaws.com/api/search"
        params = {"q": query}
        response = requests.get(url, params=params, timeout=10)
        response.raise_for_status()
        return response.json()
    except Exception as e:
        return {"error": f"Failed to search pets: {str(e)}"}


@tool
def get_pet_foods():
    """Get available pet foods from the foods API"""  # Dummy implementation for demo
    import requests

    try:
        url = "http://d3lkws79zlvrc4.cloudfront.net/petfood/api/foods"
        response = requests.get(url, timeout=10)
        response.raise_for_status()
        return response.json()
    except Exception as e:
        return {"error": f"Failed to fetch pet foods: {str(e)}"}


@app.entrypoint
def pet_food_agent_bedrock(payload):
    """
    Invoke the pet food recommendation agent with a payload
    """
    user_input = payload.get("prompt")
    print("User input:", user_input)
    
    # Create a fresh agent for each request to avoid conversation state issues
    model_id = "us.anthropic.claude-3-7-sonnet-20250219-v1:0"
    model = BedrockModel(
        model_id=model_id,
    )
    
    agent = Agent(
        model=model,
        tools=[http_request, search_pets, get_pet_foods],
        system_prompt="You're a helpful pet food recommendation assistant. You can search for pet information, get available pet foods, and make HTTP requests to external APIs to provide personalized food recommendations.",
    )
    
    try:
        response = agent(user_input)
        return response.message["content"][0]["text"]
    except Exception as e:
        print(f"Error in agent execution: {str(e)}")
        return f"I apologize, but I encountered an error while processing your request: {str(e)}"


if __name__ == "__main__":
    app.run()
