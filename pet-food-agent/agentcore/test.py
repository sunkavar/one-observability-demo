import boto3
import json

agent_arn = "arn:aws:bedrock-agentcore:us-east-1:203918868918:runtime/pet_food_recommendation_agent-pp6aKJ2xWF"
agentcore_client = boto3.client(
    'bedrock-agentcore',
    region_name="us-east-1"
)

response = agentcore_client.invoke_agent_runtime(
    agentRuntimeArn=agent_arn,
    qualifier="DEFAULT",
    payload=json.dumps({"prompt": "I have a Golden Retriever. What food do you recommend?"})
)

response_body = response['response'].read()
response_data = json.loads(response_body)
print("Agent Response:", response_data)