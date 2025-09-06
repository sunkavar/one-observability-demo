# Pet Food Recommendation Agent

A streaming endpoint that provides personalized pet food recommendations using the Strands Agent SDK.

## Features

- **Streaming Responses**: Real-time streaming of recommendations as they're generated
- **Conversational Chat**: Multi-turn conversations with session management
- **Pet Data Integration**: Fetches pet information from the pets API
- **Food Database**: Accesses comprehensive pet food database
- **Intelligent Matching**: Analyzes pet characteristics to recommend suitable foods

## API Endpoints

### Health Check
```
GET /health
```

```
curl http://localhost:8002/health
```

### Get Recommendations (Non-streaming)
```
POST /recommend
Content-Type: application/json

{
  "message": "recommend food for golden retriever, 3 years old, active"
}
```

### Get Recommendations (Streaming)
```
POST /recommend-streaming
Content-Type: application/json

{
  "message": "recommend food for senior cat with kidney issues"
}
```

### Chat with Agent (Streaming with Memory)
```
POST /chat-streaming
Content-Type: application/json

{
  "message": "recommend food for golden retriever"
}

# Continue conversation with session ID
{
  "message": "what about for puppies?",
  "session_id": "abc123-def456-ghi789"
}
```

### End Chat Session
```
DELETE /chat/{session_id}
```

## Usage

1. Install dependencies:
```bash
pip install -r requirements.txt
```

2. Run the comprehensive application:
```bash
python app.py  # Runs on port 8000
```

3. Test endpoints:
```bash
# Non-streaming
curl -X POST "http://localhost:8000/recommend" \
  -H "Content-Type: application/json" \
  -d '{"message": "recommend food for small dog, 2 years old, sensitive stomach"}'

# Streaming
curl -X POST "http://localhost:8000/recommend-streaming" \
  -H "Content-Type: application/json" \
  -d '{"message": "recommend food for golden retriever"}'

# Chat (get session ID from response)
curl -X POST "http://localhost:8000/chat-streaming" \
  -H "Content-Type: application/json" \
  -d '{"message": "recommend food for golden retriever"}'

# Continue chat
curl -X POST "http://localhost:8000/chat-streaming" \
  -H "Content-Type: application/json" \
  -d '{"message": "what about for puppies?", "session_id": "YOUR_SESSION_ID"}'

# Delete Session
curl -X DELETE http://localhost:8000/chat/YOUR_SESSION_ID

# Get Health
curl http://localhost:8000/health
```

## Data Sources

- **Pets API**: `http://servic-searc-hynpsggsckqh-2085079762.us-east-2.elb.amazonaws.com/api/search?`
- **Foods API**: `http://d3lkws79zlvrc4.cloudfront.net/petfood/api/foods`

## Model Configuration

- **Model**: Claude 3.7 Sonnet (`us.anthropic.claude-3-7-sonnet-20250219-v1:0`)
- **Region**: us-east-1