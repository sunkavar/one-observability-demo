# ECS Deployment Files

This folder contains all the files needed to deploy the Pet Food Agent to AWS ECS.

## Quick Start

1. **See the main README** in the parent directory for complete deployment instructions
2. **Run the deployment:**
    ```bash
    ./deploy-to-ecs.sh
    ```

## Files

- `app.py` - FastAPI application
- `requirements.txt` - Python dependencies
- `infrastructure.yaml` - CloudFormation template for AWS resources
- `deploy-to-ecs.sh` - Complete deployment script (Docker build + ECS deploy)
- `create-ecs-service.sh` - ECS service creation only
- `Dockerfile` - Container definition
- `ecs-task-definition-template.json` - ECS task definition template
- `.dockerignore` - Docker build exclusions

## Prerequisites

- AWS CLI configured
- Docker installed and running
- Infrastructure deployed via CloudFormation

For detailed instructions, see the **Deployment to AWS ECS** section in the main README.md file.
