#!/bin/bash

# Configuration - Update these values
AWS_REGION="us-east-1"
STACK_NAME="pet-food-agent-infrastructure"
SERVICE_NAME="pet-food-agent-service"
TASK_DEFINITION_NAME="pet-food-agent"

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

echo -e "${GREEN}🚀 Creating ECS Service behind ALB${NC}"

# Get AWS Account ID
AWS_ACCOUNT_ID=$(aws sts get-caller-identity --query Account --output text)
if [ $? -ne 0 ]; then
    echo -e "${RED}❌ Failed to get AWS Account ID${NC}"
    exit 1
fi

echo -e "${BLUE}📋 Using AWS Account: $AWS_ACCOUNT_ID${NC}"
echo -e "${BLUE}📋 Using Region: $AWS_REGION${NC}"

# Check if infrastructure stack exists
echo -e "${YELLOW}🏗️  Checking infrastructure stack...${NC}"
aws cloudformation describe-stacks --stack-name $STACK_NAME --region $AWS_REGION > /dev/null 2>&1

if [ $? -ne 0 ]; then
    echo -e "${RED}❌ Infrastructure stack '$STACK_NAME' not found${NC}"
    echo -e "${YELLOW}Please deploy the infrastructure first using infrastructure.yaml${NC}"
    exit 1
fi

# Check if task definition exists
echo -e "${YELLOW}📝 Checking task definition...${NC}"
TASK_DEF_ARN=$(aws ecs describe-task-definition --task-definition $TASK_DEFINITION_NAME --query 'taskDefinition.taskDefinitionArn' --output text --region $AWS_REGION 2>/dev/null)

if [ $? -ne 0 ] || [ "$TASK_DEF_ARN" == "None" ]; then
    echo -e "${RED}❌ Task definition '$TASK_DEFINITION_NAME' not found${NC}"
    echo -e "${YELLOW}Please create the task definition first${NC}"
    exit 1
fi

echo -e "${GREEN}✅ Task definition found: $TASK_DEF_ARN${NC}"

# Get stack outputs
echo -e "${YELLOW}📋 Getting infrastructure details...${NC}"
CLUSTER_NAME=$(aws cloudformation describe-stacks --stack-name $STACK_NAME --query 'Stacks[0].Outputs[?OutputKey==`ClusterName`].OutputValue' --output text --region $AWS_REGION)
TARGET_GROUP_ARN=$(aws cloudformation describe-stacks --stack-name $STACK_NAME --query 'Stacks[0].Outputs[?OutputKey==`TargetGroupArn`].OutputValue' --output text --region $AWS_REGION)
ECS_SECURITY_GROUP=$(aws cloudformation describe-stacks --stack-name $STACK_NAME --query 'Stacks[0].Outputs[?OutputKey==`ECSSecurityGroupId`].OutputValue' --output text --region $AWS_REGION)
ALB_DNS=$(aws cloudformation describe-stacks --stack-name $STACK_NAME --query 'Stacks[0].Outputs[?OutputKey==`ALBDNSName`].OutputValue' --output text --region $AWS_REGION)

echo -e "${BLUE}📋 Cluster: $CLUSTER_NAME${NC}"
echo -e "${BLUE}📋 Target Group: $TARGET_GROUP_ARN${NC}"
echo -e "${BLUE}📋 Security Group: $ECS_SECURITY_GROUP${NC}"
echo -e "${BLUE}📋 ALB DNS: $ALB_DNS${NC}"

# Get VPC and subnets for service
echo -e "${YELLOW}🌐 Getting network configuration...${NC}"
VPC_ID=$(aws ec2 describe-security-groups --group-ids $ECS_SECURITY_GROUP --query 'SecurityGroups[0].VpcId' --output text --region $AWS_REGION)
SUBNETS=$(aws ec2 describe-subnets --filters "Name=vpc-id,Values=$VPC_ID" "Name=map-public-ip-on-launch,Values=true" --query 'Subnets[*].SubnetId' --output text --region $AWS_REGION)

if [ -z "$SUBNETS" ]; then
    echo -e "${RED}❌ No public subnets found in VPC $VPC_ID${NC}"
    exit 1
fi

# Convert space-separated subnets to comma-separated for AWS CLI
SUBNET_ARRAY=$(echo $SUBNETS | tr ' ' ',')
echo -e "${BLUE}📋 Subnets: $SUBNET_ARRAY${NC}"

# Check if service already exists
echo -e "${YELLOW}🔍 Checking if service exists...${NC}"
SERVICE_CHECK=$(aws ecs describe-services --cluster $CLUSTER_NAME --services $SERVICE_NAME --region $AWS_REGION 2>/dev/null)
SERVICE_EXISTS=$(echo "$SERVICE_CHECK" | jq -r '.services | length' 2>/dev/null)

if [ "$SERVICE_EXISTS" -gt 0 ]; then
    # Service exists, ask if user wants to update
    echo -e "${YELLOW}⚠️  Service '$SERVICE_NAME' already exists${NC}"
    read -p "Do you want to update it? (y/N): " -n 1 -r
    echo
    if [[ $REPLY =~ ^[Yy]$ ]]; then
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
        echo -e "${YELLOW}ℹ️  Skipping service update${NC}"
        exit 0
    fi
else
    # Service doesn't exist, create it
    echo -e "${YELLOW}🆕 Creating new ECS service...${NC}"
    
    # Create the service
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

# Wait for service to become stable
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

echo -e "${GREEN}🎉 ECS Service deployment completed successfully!${NC}"