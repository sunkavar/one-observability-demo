#!/usr/bin/env python3
"""
Deploy Pet Food Recommendation Agent to Bedrock Agent Core Runtime
"""

from bedrock_agentcore_starter_toolkit import Runtime
from boto3.session import Session
import time
import json


def deploy_pet_food_agent():
    """Deploy the pet food recommendation agent to AgentCore Runtime"""

    # Initialize boto session and get region
    boto_session = Session()
    region = boto_session.region_name
    print(f"Deploying to region: {region}")

    # Initialize the AgentCore Runtime
    agentcore_runtime = Runtime()
    agent_name = "pet_food_recommendation_agent"

    print("🚀 Configuring AgentCore Runtime...")

    # Configure the runtime
    response = agentcore_runtime.configure(
        entrypoint="bedrock_agent_core_app.py",
        auto_create_execution_role=True,
        auto_create_ecr=True,
        requirements_file="requirements.txt",
        region=region,
        agent_name=agent_name,
    )

    print("✅ Configuration complete!")
    print(f"Configuration response: {response}")

    print("\n🏗️  Launching agent to AgentCore Runtime...")

    # Launch to AgentCore Runtime
    launch_result = agentcore_runtime.launch()

    print("✅ Launch initiated!")
    print(f"Agent ARN: {launch_result.agent_arn}")
    print(f"ECR URI: {launch_result.ecr_uri}")

    print("\n⏳ Checking deployment status...")

    # Check status until deployment is complete
    status_response = agentcore_runtime.status()
    status = status_response.endpoint["status"]
    end_status = ["READY", "CREATE_FAILED", "DELETE_FAILED", "UPDATE_FAILED"]

    while status not in end_status:
        print(f"Status: {status}")
        time.sleep(10)
        status_response = agentcore_runtime.status()
        status = status_response.endpoint["status"]

    print(f"✅ Final Status: {status}")

    if status == "READY":
        print("\n🎉 Agent deployed successfully!")

        # Test the agent
        print("\n🧪 Testing the agent...")
        test_payload = {
            "prompt": "I have a 2-year-old Golden Retriever named Max. Can you recommend some good food options?"
        }

        try:
            invoke_response = agentcore_runtime.invoke(test_payload)
            print("✅ Test successful!")
            print("Response:")
            print(invoke_response["response"][0])

        except Exception as e:
            print(f"❌ Test failed: {str(e)}")

        # Print deployment info
        print(f"\n📋 Deployment Summary:")
        print(f"Agent Name: {agent_name}")
        print(f"Agent ARN: {launch_result.agent_arn}")
        print(f"ECR URI: {launch_result.ecr_uri}")
        print(f"Region: {region}")
        print(f"Status: {status}")

        return launch_result

    else:
        print(f"❌ Deployment failed with status: {status}")
        return None


if __name__ == "__main__":
    print("🤖 Pet Food Recommendation Agent Deployment")
    print("=" * 50)

    try:
        result = deploy_pet_food_agent()
        if result:
            print("\n✅ Deployment completed successfully!")
        else:
            print("\n❌ Deployment failed!")

    except Exception as e:
        print(f"\n❌ Deployment error: {str(e)}")
        import traceback

        traceback.print_exc()
