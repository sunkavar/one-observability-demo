#!/usr/bin/env python3
"""
Check if agent is deployed and test it, or deploy if needed
"""

from bedrock_agentcore_starter_toolkit import Runtime
from boto3.session import Session
import json
import time

def check_and_test_agent():
    """Check if agent is deployed, deploy if needed, then test"""
    
    # Initialize the runtime
    agentcore_runtime = Runtime()
    agent_name = "pet_food_recommendation_agent"
    
    print("🔍 Checking agent deployment status...")
    
    try:
        # Try to get status first
        status_response = agentcore_runtime.status()
        print(f"✅ Agent found! Status: {status_response.endpoint['status']}")
        
        if status_response.endpoint['status'] == 'READY':
            print("🎉 Agent is ready! Testing...")
            test_agent(agentcore_runtime)
        else:
            print(f"⏳ Agent status: {status_response.endpoint['status']}")
            print("Waiting for agent to be ready...")
            
    except Exception as e:
        print(f"❌ Agent not found or error: {str(e)}")
        print("🚀 Deploying agent...")
        
        try:
            deploy_agent(agentcore_runtime, agent_name)
        except Exception as deploy_error:
            print(f"❌ Deployment failed: {str(deploy_error)}")
            return

def deploy_agent(runtime, agent_name):
    """Deploy the agent"""
    boto_session = Session()
    region = boto_session.region_name
    
    print(f"Deploying to region: {region}")
    
    # Configure
    response = runtime.configure(
        entrypoint="bedrock_agent_core_app.py",
        auto_create_execution_role=True,
        auto_create_ecr=True,
        requirements_file="requirements.txt",
        region=region,
        agent_name=agent_name
    )
    print("✅ Configuration complete!")
    
    # Launch
    launch_result = runtime.launch()
    print("✅ Launch initiated!")
    
    # Wait for ready
    print("⏳ Waiting for deployment...")
    status_response = runtime.status()
    status = status_response.endpoint['status']
    end_status = ['READY', 'CREATE_FAILED', 'DELETE_FAILED', 'UPDATE_FAILED']
    
    while status not in end_status:
        print(f"Status: {status}")
        time.sleep(10)
        status_response = runtime.status()
        status = status_response.endpoint['status']
    
    print(f"✅ Final Status: {status}")
    
    if status == 'READY':
        print("🎉 Agent deployed successfully!")
        test_agent(runtime)
    else:
        print(f"❌ Deployment failed with status: {status}")

def test_agent(runtime):
    """Test the deployed agent"""
    
    test_cases = [
        "I have a 2-year-old Golden Retriever named Max. Can you recommend some good food options?",
        "What makes a good dog food?",
        "My cat has sensitive stomach. What should I feed her?"
    ]
    
    print("\n🧪 Testing agent...")
    print("=" * 50)
    
    for i, prompt in enumerate(test_cases, 1):
        print(f"\n{i}. Testing: {prompt}")
        print("-" * 30)
        
        try:
            response = runtime.invoke({"prompt": prompt})
            
            print("✅ Response received!")
            
            # Handle different response formats
            if isinstance(response, dict):
                if 'response' in response:
                    response_content = response['response']
                    if isinstance(response_content, list) and response_content:
                        print(f"Response: {response_content[0]}")
                    else:
                        print(f"Response: {response_content}")
                else:
                    print(f"Full response: {json.dumps(response, indent=2)}")
            else:
                print(f"Response: {response}")
                
        except Exception as e:
            print(f"❌ Test failed: {str(e)}")

if __name__ == "__main__":
    print("🤖 Pet Food Agent - Check and Test")
    print("=" * 40)
    check_and_test_agent()