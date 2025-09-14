#!/bin/bash

# Configuration - Update these values
AWS_REGION="us-east-1"
STACK_NAME="pet-food-agent-infrastructure"
ECR_REPOSITORY="pet-food-agent"
SERVICE_NAME="pet-food-agent-service"
TASK_DEFINITION_NAME="pet-food-agent"

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

echo -e "${GREEN}🚀 Complete ECS deployment for Pet Food Agent with ALB${NC}"

# Get AWS Account ID
AWS_ACCOUNT_ID=$(aws sts get-caller-identity --query Account --output text)
if [ $? -ne 0 ]; then
    echo -e "${RED}❌ Failed to get AWS Account ID${NC}"
    exit 1
fi

echo -e "${BLUE}📋 Using AWS Account: $AWS_ACCOUNT_ID${NC}"
echo -e "${BLUE}📋 Using Region: $AWS_REGION${NC}"

# Step 1: Check infrastructure stack exists
echo -e "${YELLOW}🏗️  Checking infrastructure stack...${NC}"
aws cloudformation describe-stacks --stack-name $STACK_NAME --region $AWS_REGION > /dev/null 2>&1

if [ $? -ne 0 ]; then
    echo -e "${RED}❌ Infrastructure stack '$STACK_NAME' not found${NC}"
    echo -e "${YELLOW}Please deploy the infrastructure first using infrastructure.yaml${NC}"
    exit 1
fi

# Get stack outputs
echo -e "${YELLOW}📋 Getting infrastructure details...${NC}"
CLUSTER_NAME=$(aws cloudformation describe-stacks --stack-name $STACK_NAME --query 'Stacks[0].Outputs[?OutputKey==`ClusterName`].OutputValue' --output text --region $AWS_REGION)
TARGET_GROUP_ARN=$(aws cloudformation describe-stacks --stack-name $STACK_NAME --query 'Stacks[0].Outputs[?OutputKey==`TargetGroupArn`].OutputValue' --output text --region $AWS_REGION)
ECS_SECURITY_GROUP=$(aws cloudformation describe-stacks --stack-name $STACK_NAME --query 'Stacks[0].Outputs[?OutputKey==`ECSSecurityGroupId`].OutputValue' --output text --region $AWS_REGION)
EXECUTION_ROLE_ARN=$(aws cloudformation describe-stacks --stack-name $STACK_NAME --query 'Stacks[0].Outputs[?OutputKey==`TaskExecutionRoleArn`].OutputValue' --output text --region $AWS_REGION)
TASK_ROLE_ARN=$(aws cloudformation describe-stacks --stack-name $STACK_NAME --query 'Stacks[0].Outputs[?OutputKey==`TaskRoleArn`].OutputValue' --output text --region $AWS_REGION)
ECR_URI=$(aws cloudformation describe-stacks --stack-name $STACK_NAME --query 'Stacks[0].Outputs[?OutputKey==`ECRRepositoryURI`].OutputValue' --output text --region $AWS_REGION)
ALB_DNS=$(aws cloudformation describe-stacks --stack-name $STACK_NAME --query 'Stacks[0].Outputs[?OutputKey==`ALBDNSName`].OutputValue' --output text --region $AWS_REGION)

echo -e "${BLUE}📋 Cluster: $CLUSTER_NAME${NC}"
echo -e "${BLUE}📋 ECR URI: $ECR_URI${NC}"
echo -e "${BLUE}📋 ALB DNS: $ALB_DNS${NC}"

# Step 2: Build Docker image for AMD64 platform (ECS Fargate)
echo -e "${YELLOW}📦 Building Docker image for AMD64 platform...${NC}"
docker build --platform linux/amd64 -t $ECR_REPOSITORY .

if [ $? -ne 0 ]; then
    echo -e "${RED}❌ Docker build failed${NC}"
    exit 1
fi

# Step 3: Tag image for ECR
echo -e "${YELLOW}🏷️  Tagging image for ECR...${NC}"
docker tag $ECR_REPOSITORY:latest $ECR_URI:latest

# Step 4: Login to ECR
echo -e "${YELLOW}🔐 Logging into ECR...${NC}"
aws ecr get-login-password --region $AWS_REGION | docker login --username AWS --password-stdin $AWS_ACCOUNT_ID.dkr.ecr.$AWS_REGION.amazonaws.com

if [ $? -ne 0 ]; then
    echo -e "${RED}❌ ECR login failed${NC}"
    exit 1
fi

# Step 5: Push image to ECR with retry logic
echo -e "${YELLOW}⬆️  Pushing image to ECR...${NC}"
PUSH_RETRIES=3
PUSH_ATTEMPT=1

while [ $PUSH_ATTEMPT -le $PUSH_RETRIES ]; do
    echo -e "${YELLOW}📤 Push attempt $PUSH_ATTEMPT of $PUSH_RETRIES...${NC}"
    
    if docker push $ECR_URI:latest; then
        echo -e "${GREEN}✅ Docker push successful!${NC}"
        break
    else
        echo -e "${RED}❌ Push attempt $PUSH_ATTEMPT failed${NC}"
        
        if [ $PUSH_ATTEMPT -eq $PUSH_RETRIES ]; then
            echo -e "${RED}❌ All push attempts failed. Troubleshooting steps:${NC}"
            echo -e "${YELLOW}1. Check your internet connection${NC}"
            echo -e "${YELLOW}2. Verify Docker is running: docker info${NC}"
            echo -e "${YELLOW}3. Check if you're behind a corporate proxy${NC}"
            echo -e "${YELLOW}4. Try: docker system prune -f${NC}"
            echo -e "${YELLOW}5. Restart Docker Desktop and try again${NC}"
            exit 1
        fi
        
        echo -e "${YELLOW}⏳ Waiting 10 seconds before retry...${NC}"
        sleep 10
        
        # Re-login to ECR before retry
        echo -e "${YELLOW}🔐 Re-authenticating with ECR...${NC}"
        aws ecr get-login-password --region $AWS_REGION | docker login --username AWS --password-stdin $AWS_ACCOUNT_ID.dkr.ecr.$AWS_REGION.amazonaws.com
    fi
    
    PUSH_ATTEMPT=$((PUSH_ATTEMPT + 1))
done

# Step 6: Create task definition from template
echo -e "${YELLOW}📝 Creating task definition...${NC}"
sed "s|EXECUTION_ROLE_ARN|$EXECUTION_ROLE_ARN|g; s|TASK_ROLE_ARN|$TASK_ROLE_ARN|g; s|ECR_IMAGE_URI|$ECR_URI:latest|g; s|AWS_REGION|$AWS_REGION|g" ecs-task-definition-template.json > ecs-task-definition-final.json

# Step 7: Register task definition
echo -e "${YELLOW}📝 Registering task definition...${NC}"
aws ecs register-task-definition --cli-input-json file://ecs-task-definition-final.json --region $AWS_REGION

if [ $? -ne 0 ]; then
    echo -e "${RED}❌ Task definition registration failed${NC}"
    exit 1
fi

# Step 8: Get network configuration
echo -e "${YELLOW}🌐 Getting network configuration...${NC}"
VPC_ID=$(aws ec2 describe-security-groups --group-ids $ECS_SECURITY_GROUP --query 'SecurityGroups[0].VpcId' --output text --region $AWS_REGION)
SUBNETS=$(aws ec2 describe-subnets --filters "Name=vpc-id,Values=$VPC_ID" "Name=map-public-ip-on-launch,Values=true" --query 'Subnets[*].SubnetId' --output text --region $AWS_REGION)

if [ -z "$SUBNETS" ]; then
    echo -e "${RED}❌ No public subnets found in VPC $VPC_ID${NC}"
    exit 1
fi

SUBNET_ARRAY=$(echo $SUBNETS | tr ' ' ',')
echo -e "${BLUE}📋 Subnets: $SUBNET_ARRAY${NC}"

# Step 9: Check if service exists and create/update accordingly
echo -e "${YELLOW}🔍 Checking if service exists...${NC}"
SERVICE_CHECK=$(aws ecs describe-services --cluster $CLUSTER_NAME --services $SERVICE_NAME --region $AWS_REGION 2>/dev/null)
SERVICE_EXISTS=$(echo "$SERVICE_CHECK" | jq -r '.services | length' 2>/dev/null)

if [ "$SERVICE_EXISTS" -gt 0 ]; then
    # Service exists, update it
    echo -e "${YELLOW}🔄 Updating existing service...${NC}"
    aws ecs update-service \
        --cluster $CLUSTER_NAME \
        --service $SERVICE_NAME \
        --task-definition $TASK_DEFINITION_NAME \
        --force-new-deployment \
        --region $AWS_REGION
    
    if [ $? -ne 0 ]; then
        echo -e "${RED}❌ Service update failed${NC}"
        exit 1
    fi
    
    echo -e "${GREEN}✅ Service update initiated${NC}"
else
    # Service doesn't exist, create it
    echo -e "${YELLOW}🆕 Creating new ECS service...${NC}"
    
    aws ecs create-service \
        --cluster $CLUSTER_NAME \
        --service-name $SERVICE_NAME \
        --task-definition $TASK_DEFINITION_NAME \
        --desired-count 2 \
        --launch-type FARGATE \
        --platform-version LATEST \
        --network-configuration "awsvpcConfiguration={subnets=[$SUBNET_ARRAY],securityGroups=[$ECS_SECURITY_GROUP],assignPublicIp=ENABLED}" \
        --load-balancers "targetGroupArn=$TARGET_GROUP_ARN,containerName=pet-food-agent,containerPort=8000" \
        --health-check-grace-period-seconds 300 \
        --enable-execute-command \
        --region $AWS_REGION

    if [ $? -ne 0 ]; then
        echo -e "${RED}❌ Service creation failed${NC}"
        echo -e "${YELLOW}💡 Common issues:${NC}"
        echo -e "${YELLOW}   - Check if task definition has the correct container name 'pet-food-agent'${NC}"
        echo -e "${YELLOW}   - Verify target group exists and is healthy${NC}"
        echo -e "${YELLOW}   - Ensure subnets have internet gateway access${NC}"
        echo -e "${YELLOW}   - Check security group allows traffic on port 8000${NC}"
        exit 1
    fi
    
    echo -e "${GREEN}✅ Service creation initiated${NC}"
fi

# Step 10: Wait for deployment to complete
echo -e "${YELLOW}⏳ Waiting for service to become stable...${NC}"
echo -e "${BLUE}This may take 5-10 minutes for initial deployment${NC}"

aws ecs wait services-stable --cluster $CLUSTER_NAME --services $SERVICE_NAME --region $AWS_REGION

if [ $? -eq 0 ]; then
    echo -e "${GREEN}🎯 Service is now stable and ready!${NC}"
    
    # Get service status
    RUNNING_COUNT=$(aws ecs describe-services --cluster $CLUSTER_NAME --services $SERVICE_NAME --query 'services[0].runningCount' --output text --region $AWS_REGION)
    DESIRED_COUNT=$(aws ecs describe-services --cluster $CLUSTER_NAME --services $SERVICE_NAME --query 'services[0].desiredCount' --output text --region $AWS_REGION)
    
    echo -e "${GREEN}📊 Service Status: $RUNNING_COUNT/$DESIRED_COUNT tasks running${NC}"
    echo -e "${GREEN}🌐 ALB URL: http://$ALB_DNS${NC}"
    echo -e "${GREEN}🔗 Test your streaming API:${NC}"
    echo -e "${BLUE}curl -X POST \"http://$ALB_DNS/health\"${NC}"
    echo -e "${BLUE}curl -X POST \"http://$ALB_DNS/recommend-streaming\" -H \"Content-Type: application/json\" -d '{\"message\": \"recommend food for golden retriever\"}'${NC}"
    echo -e "${BLUE}curl -X POST \"http://$ALB_DNS/chat-streaming\" -H \"Content-Type: application/json\" -d '{\"message\": \"recommend food for golden retriever\"}'${NC}"
    
    # Check target group health
    echo -e "${YELLOW}🏥 Checking target group health...${NC}"
    HEALTHY_TARGETS=$(aws elbv2 describe-target-health --target-group-arn $TARGET_GROUP_ARN --query 'TargetHealthDescriptions[?TargetHealth.State==`healthy`]' --output text --region $AWS_REGION | wc -l)
    TOTAL_TARGETS=$(aws elbv2 describe-target-health --target-group-arn $TARGET_GROUP_ARN --query 'TargetHealthDescriptions' --output text --region $AWS_REGION | wc -l)
    
    echo -e "${GREEN}🎯 Target Health: $HEALTHY_TARGETS/$TOTAL_TARGETS targets healthy${NC}"
    
    if [ "$HEALTHY_TARGETS" -eq 0 ]; then
        echo -e "${YELLOW}⚠️  No healthy targets yet. This is normal for new deployments.${NC}"
        echo -e "${YELLOW}   Wait a few more minutes and check target group health in AWS Console${NC}"
    fi
    
else
    echo -e "${RED}❌ Service failed to become stable${NC}"
    echo -e "${YELLOW}🔍 Check the following:${NC}"
    echo -e "${YELLOW}   - ECS Console: https://console.aws.amazon.com/ecs/home?region=$AWS_REGION#/clusters/$CLUSTER_NAME/services${NC}"
    echo -e "${YELLOW}   - CloudWatch Logs: https://console.aws.amazon.com/cloudwatch/home?region=$AWS_REGION#logsV2:log-groups/log-group/%2Fecs%2Fpet-food-agent${NC}"
    echo -e "${YELLOW}   - Target Group Health: https://console.aws.amazon.com/ec2/v2/home?region=$AWS_REGION#TargetGroups:${NC}"
    exit 1
fi

# Cleanup
rm -f ecs-task-definition-final.json

echo -e "${GREEN}🎉 Complete ECS deployment finished successfully!${NC}"