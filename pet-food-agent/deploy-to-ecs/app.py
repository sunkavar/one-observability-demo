from typing import Dict, Optional
from fastapi import FastAPI, HTTPException
from fastapi.responses import StreamingResponse, PlainTextResponse
from pydantic import BaseModel
import uvicorn
from strands import Agent, tool
from strands_tools import http_request
from strands.models import BedrockModel
import os
import uuid

app = FastAPI(title="Pet Food Recommendation API")

PET_FOOD_SYSTEM_PROMPT = """You are a pet food recommendation assistant with HTTP capabilities. You can:

1. Fetch pet information from the pets API
2. Fetch available pet foods from the foods API  
3. Analyze pet characteristics and recommend suitable foods
4. Provide detailed explanations for your recommendations
5. Continue conversations and answer follow-up questions

When making recommendations:
1. First get pet details from http://servic-searc-hynpsggsckqh-2085079762.us-east-2.elb.amazonaws.com/api/search?
2. Then get available foods from http://d3lkws79zlvrc4.cloudfront.net/petfood/api/foods
3. Match pet characteristics (age, size, breed, health conditions) with appropriate food types
4. Consider nutritional needs, dietary restrictions, and preferences
5. Provide clear reasoning for each recommendation

Format your response with:
- Pet information summary
- Top 3 food recommendations with explanations
- Any special dietary considerations
"""

class RecommendationRequest(BaseModel):
    message: str

class ChatRequest(BaseModel):
    message: str
    session_id: Optional[str] = None

# Store active chat sessions and deleted session IDs
chat_sessions = {}
deleted_sessions = set()

@app.get('/health')
def health_check():
    """Health check endpoint for the load balancer."""
    return {"status": "healthy"}

@app.post('/recommend')
async def get_recommendation(request: RecommendationRequest):
    """Endpoint to get pet food recommendations."""
    message = request.message
    
    if not message:
        raise HTTPException(status_code=400, detail="No message provided")

    try:
        bedrock_model = BedrockModel(
            model_id="us.anthropic.claude-3-7-sonnet-20250219-v1:0",
            region_name="us-east-1"
        )
        agent = Agent(
            model=bedrock_model,
            system_prompt=PET_FOOD_SYSTEM_PROMPT,
            tools=[http_request],
        )
        response = agent(message)
        content = str(response)
        return PlainTextResponse(content=content)
    except Exception as e:
        raise HTTPException(status_code=500, detail=str(e))

async def run_recommendation_agent_and_stream_response(message: str):
    """Stream recommendation response chunks as they come in."""
    bedrock_model = BedrockModel(
        model_id="us.anthropic.claude-3-7-sonnet-20250219-v1:0",
        region_name="us-east-1"
    )
    agent = Agent(
        model=bedrock_model,
        system_prompt=PET_FOOD_SYSTEM_PROMPT,
        tools=[http_request],
        callback_handler=None
    )

    async for item in agent.stream_async(message):
        if "data" in item:
            yield item['data']

@app.post('/recommend-streaming')
async def get_recommendation_streaming(request: RecommendationRequest):
    """Endpoint to stream pet food recommendations as they come in."""
    try:
        message = request.message

        if not message:
            raise HTTPException(status_code=400, detail="No message provided")

        return StreamingResponse(
            run_recommendation_agent_and_stream_response(message),
            media_type="text/plain"
        )
    except Exception as e:
        raise HTTPException(status_code=500, detail=str(e))

async def run_chat_agent_and_stream_response(message: str, session_id: str):
    """Stream chat response chunks as they come in."""
    # Check if session was deleted
    if session_id in deleted_sessions:
        yield f"[Error: Session {session_id} has been ended. Please start a new session.]\n"
        return
    
    # First yield the session ID
    yield f"[Session ID: {session_id}]\n\n"
    
    # Get or create agent for this session
    if session_id not in chat_sessions:
        bedrock_model = BedrockModel(
            model_id="us.anthropic.claude-3-7-sonnet-20250219-v1:0",
            region_name="us-east-1"
        )
        chat_sessions[session_id] = Agent(
            model=bedrock_model,
            system_prompt=PET_FOOD_SYSTEM_PROMPT,
            tools=[http_request],
            callback_handler=None
        )
    
    agent = chat_sessions[session_id]
    
    async for item in agent.stream_async(message):
        if "data" in item:
            yield item['data']

@app.post('/chat-streaming')
async def chat_streaming(request: ChatRequest):
    """Endpoint for continuous chat with the agent."""
    try:
        message = request.message
        if not message:
            raise HTTPException(status_code=400, detail="No message provided")
        
        session_id = request.session_id or str(uuid.uuid4())
        
        return StreamingResponse(
            run_chat_agent_and_stream_response(message, session_id),
            media_type="text/plain",
            headers={"X-Session-ID": session_id}
        )
    except Exception as e:
        raise HTTPException(status_code=500, detail=str(e))

@app.delete('/chat/{session_id}')
def end_chat_session(session_id: str):
    """End a chat session and clean up resources."""
    if session_id in chat_sessions:
        del chat_sessions[session_id]
        deleted_sessions.add(session_id)
        return {"status": "session ended"}
    return {"status": "session not found"}

if __name__ == '__main__':
    port = int(os.environ.get('PORT', 8000))
    uvicorn.run(app, host='0.0.0.0', port=port)