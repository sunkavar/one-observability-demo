import json
import os
import random
import uuid
import urllib.parse as urlparse
from urllib.request import Request, urlopen
import boto3
from botocore.auth import SigV4Auth
from botocore.awsrequest import AWSRequest

def load_prompts():
    with open('prompts.json', 'r') as f:
        return json.load(f)

def lambda_handler(event, context):
    """
    Traffic generator that invokes the Pet Food Agent endpoint with random queries.
    Falls back to direct Agent invocation if PETFOOD_AGENT_URL is not set.
    """
    
    petfood_agent_url = os.environ.get('PETFOOD_AGENT_URL')
    petfood_agent_arn = os.environ.get('PETFOOD_AGENT_ARN')
    region = os.environ.get('AWS_REGION', 'us-east-1')
    session_id = os.environ.get('SESSION_ID', f"petfood-session-{str(uuid.uuid4())}")
    
    if not petfood_agent_url and not petfood_agent_arn:
        return {
            'statusCode': 200,
            'body': json.dumps({'message': 'Neither PETFOOD_AGENT_URL nor PETFOOD_AGENT_ARN set, skipping traffic generation'})
        }
    
    prompts = load_prompts()
    results = []
    
    # Generate 3 queries per session with at least 1 unsupported pet query
    queries = []
    supported_query_types = ['puppy_food_queries', 'kitten_food_queries', 'bunny_food_queries', 
                             'pet_lookup_queries', 'general_queries']
    
    # First query: always unsupported pet (guaranteed)
    queries.append(random.choice(prompts['unsupported_pet_queries']))
    
    # Remaining 2 queries: mix of all types
    all_query_types = supported_query_types + ['unsupported_pet_queries']
    for _ in range(2):
        query_type = random.choice(all_query_types)
        queries.append(random.choice(prompts[query_type]))
    
    # Fallback to direct agent invocation if PETFOOD_AGENT_URL is not set
    if not petfood_agent_url:
        session = boto3.Session()
        credentials = session.get_credentials()
        
        for query in queries:
            try:
                encoded_arn = urlparse.quote(petfood_agent_arn, safe='')
                url = f'https://bedrock-agentcore.{region}.amazonaws.com/runtimes/{encoded_arn}/invocations?qualifier=DEFAULT'
                
                payload = json.dumps({'prompt': query}).encode('utf-8')
                request = AWSRequest(method='POST', url=url, data=payload, headers={
                    'Content-Type': 'application/json',
                    'X-Amzn-Bedrock-AgentCore-Runtime-Session-Id': session_id
                })
                SigV4Auth(credentials, 'bedrock-agentcore', region).add_auth(request)
                
                with urlopen(Request(url, data=payload, headers=dict(request.headers))) as response:
                    body = response.read().decode('utf-8')
                
                results.append({
                    'query': query,
                    'response': body,
                    'agent_used': 'petfood'
                })
                
            except Exception as error:
                results.append({
                    'query': query,
                    'error': str(error)
                })
    else:
        # Use Pet Food Agent URL if available
        for query in queries:
            try:
                url = f"{petfood_agent_url.rstrip('/')}/api/agent/ask"
                payload = json.dumps({'query': query}).encode('utf-8')
                request = Request(url, data=payload, headers={'Content-Type': 'application/json'})
                
                with urlopen(request) as response:
                    body = response.read().decode('utf-8')
                
                results.append({
                    'query': query,
                    'response': body
                })
                
            except Exception as error:
                results.append({
                    'query': query,
                    'error': str(error)
                })
    
    return {
        'statusCode': 200,
        'body': json.dumps({
            'total_requests': len(results),
            'results': results
        })
    }
