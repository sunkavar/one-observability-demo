<!--
Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
SPDX-License-Identifier: Apache-2.0
-->

# Local Deployment Guide

This guide provides step-by-step instructions for setting up and deploying the One Observability Demo project locally for development purposes.

## Table of Contents

- [Prerequisites](#prerequisites)
- [Environment Setup](#environment-setup)
- [Pre-commit Hooks Setup](#pre-commit-hooks-setup)
- [Environment Configuration](#environment-configuration)
- [Deployment Process](#deployment-process)
- [Development Workflow](#development-workflow)
- [Cleanup](#cleanup)

## Prerequisites

### Required Software

1. **Node.js and npm**: For CDK and TypeScript dependencies
2. **AWS CLI**: Configured with appropriate credentials
3. **Python 3.x**: For pre-commit hooks and scripts
4. **Git**: Version control
5. **Container Runtime** (for application development):
    - Docker (recommended)
    - Finch
    - Podman

### AWS Requirements

- AWS account with appropriate permissions
- AWS CLI configured with credentials
- Permissions for:
    - CDK deployment (CloudFormation, IAM, etc.)
    - S3 bucket creation and management
    - ECR (for container applications)
    - ECS/EKS (for service deployment)

### Additional Tools

```bash
# Install jq for JSON processing (required for DynamoDB seeding)
# macOS
brew install jq

# Ubuntu/Debian
sudo apt-get install jq

# Amazon Linux
sudo yum install jq
```

## Environment Setup

### 1. Clone and Navigate to Repository

```bash
git clone <repository-url>
cd one-observability-demo
```

### 2. Install Dependencies

```bash
# Install Node.js dependencies
npm install

# Install Python dependencies for pre-commit
pip install pre-commit
pip install git-remote-s3
```

## Pre-commit Hooks Setup

```bash
# Install pre-commit hooks
pre-commit install
pre-commit install --hook-type commit-msg

# Test pre-commit hooks
pre-commit run --all-files
```

## Environment Configuration

### 1. Create Environment File

```bash
# Copy the sample environment file
cp src/cdk/.env.sample src/cdk/.env
```

### 2. Configure Environment Variables

Edit `src/cdk/.env` with your AWS details:

```bash
# Required configuration
CONFIG_BUCKET=your-unique-s3-bucket-name
BRANCH_NAME=main
AWS_ACCOUNT_ID=123456789012
AWS_REGION=us-east-1

# Optional: Add other environment-specific variables
```

**Important Notes:**

- `CONFIG_BUCKET`: Must be globally unique S3 bucket name
- `AWS_ACCOUNT_ID`: Your 12-digit AWS account ID
- `AWS_REGION`: Target AWS region for deployment

## Deployment Process

### Step 1: Environment Validation (CRITICAL)

**This step is mandatory and must be run first:**

```bash
./src/cdk/scripts/deploy-check.sh
```

This script will:

- ✅ Validate AWS credentials and display current role/account
- ✅ Check if the S3 bucket exists (create if needed)
- ✅ Verify the repository archive exists in S3 (upload if needed)
- ✅ Prepare the environment for CDK deployment

### Step 2: Set Up CDK Alias and Update (Recommended)

```bash
# Update AWS CDK to latest version
npm install -g aws-cdk@latest

# Add to your shell profile (~/.bashrc, ~/.zshrc, etc.)
alias cdk="npx cdk"

# Reload your shell configuration
source ~/.bashrc  # or ~/.zshrc

# Verify CDK version
cdk --version
```

### Step 3: Bootstrap CDK (First Time Only)

**This step is required for first-time CDK deployment in your AWS account/region:**

```bash
# Bootstrap CDK for your account and region
cdk -a "npx ts-node bin/local.ts" bootstrap

# Alternative: Bootstrap with specific account and region
cdk -a "npx ts-node bin/local.ts" bootstrap aws://123456789012/us-east-1
```

This will:

- ✅ Create the CDK toolkit stack in your AWS account
- ✅ Set up S3 bucket for CDK assets
- ✅ Create IAM roles needed for CDK deployments
- ✅ Prepare your account for CDK stack deployments

**Note:** You only need to run bootstrap once per AWS account/region combination.

### Step 4: Understanding Stack Deployment Flow

The project consists of 5 CDK stacks that deploy in a specific order due to dependencies:

#### Stack Deployment Order:

1. **DevCoreStack** (Foundation)
    - VPC, subnets, security groups
    - NAT gateways, internet gateways
    - Core networking infrastructure
    - **Dependencies**: None (deploys first)

2. **DevApplicationsStack** (Containers - Parallel with Storage/Compute)
    - ECS clusters and services
    - EKS clusters (if enabled)
    - Container registries (ECR)
    - **Dependencies**: None (can deploy in parallel)

3. **DevStorageStack** (Data Layer)
    - S3 buckets for assets
    - Aurora PostgreSQL database
    - DynamoDB tables
    - **Dependencies**: DevCoreStack (needs VPC)

4. **DevComputeStack** (Processing Layer)
    - Lambda functions
    - OpenSearch collection and ingestion pipeline
    - EC2 instances (if any)
    - **Dependencies**: DevCoreStack (needs VPC)

5. **DevMicroservicesStack** (Application Layer)
    - Pet store microservices
    - API Gateway endpoints
    - CloudWatch canaries for monitoring
    - **Dependencies**: DevComputeStack (needs compute resources)

#### Deployment Flow Diagram:

```
DevCoreStack (VPC, Networking)
    ├── DevApplicationsStack (ECS/EKS) [Parallel]
    ├── DevStorageStack (S3, Aurora, DynamoDB) [Parallel]
    └── DevComputeStack (Lambda, OpenSearch)
            └── DevMicroservicesStack (Pet Store Apps)
```

### Step 5: Deploy Using Local CDK

The project uses a local CDK application in `src/cdk/bin/local.ts` for development:

```bash
# List available stacks
cdk -a "npx ts-node bin/local.ts" list

# Show what will be deployed (recommended first)
cdk -a "npx ts-node bin/local.ts" diff

# Deploy all stacks (with approval prompts)
cdk -a "npx ts-node bin/local.ts" deploy --all

# Deploy all stacks without approval prompts (faster for development)
cdk -a "npx ts-node bin/local.ts" deploy --all --require-approval never

# Deploy specific stack (if needed)
cdk -a "npx ts-node bin/local.ts" deploy <stack-name> --require-approval never
```

#### Individual Stack Deployment (Optional):

If you need to deploy stacks individually, follow this order:

```bash
# 1. Core infrastructure first
cdk -a "npx ts-node bin/local.ts" deploy DevCoreStack --require-approval never

# 2. Then deploy these in parallel (or sequentially)
cdk -a "npx ts-node bin/local.ts" deploy DevApplicationsStack --require-approval never
cdk -a "npx ts-node bin/local.ts" deploy DevStorageStack --require-approval never
cdk -a "npx ts-node bin/local.ts" deploy DevComputeStack --require-approval never

# 3. Finally deploy microservices
cdk -a "npx ts-node bin/local.ts" deploy DevMicroservicesStack --require-approval never
```

#### Deployment Time Estimates:

- **DevCoreStack**: ~5-8 minutes (VPC, NAT gateways)
- **DevApplicationsStack**: ~10-15 minutes (ECS clusters, ECR)
- **DevStorageStack**: ~15-20 minutes (Aurora database creation)
- **DevComputeStack**: ~8-12 minutes (OpenSearch, Lambda functions)
- **DevMicroservicesStack**: ~5-10 minutes (Applications, APIs)

**Total deployment time**: ~45-65 minutes for complete infrastructure

### Step 6: Verify Deployment

```bash
# Check CloudFormation stacks in AWS Console
# Verify resources are created as expected
# Test application endpoints (if applicable)
```

## Development Workflow

### Application Redeployment

For quick iteration on individual microservices:

```bash
# Interactive application redeployment
./src/cdk/scripts/redeploy-app.sh
```

This script will:

- Build container images with cross-platform support
- Push images to Amazon ECR
- Trigger service redeployments on ECS
- Provide EKS instructions if applicable

### Database Seeding

Populate DynamoDB tables with initial data:

```bash
# Interactive mode (recommended)
./src/cdk/scripts/seed-dynamodb.sh

# Non-interactive mode with specific table
./src/cdk/scripts/seed-dynamodb.sh TABLE_NAME
```

### Parameter Store Access

Retrieve configuration values:

```bash
# Get parameter value
./src/cdk/scripts/get-parameter.sh database-endpoint

# Example usage
DB_ENDPOINT=$(./src/cdk/scripts/get-parameter.sh database-endpoint)
echo "Database endpoint: $DB_ENDPOINT"
```

### Go Application Development

For Go services (like payforadoption):

```bash
# Navigate to Go service directory
cd src/payforadoption  # or appropriate service directory

# Install dependencies
go mod tidy

# Build application
go build -o payforadoption .

# Run locally
./payforadoption

# Run tests
go test ./... -v
go test ./... -cover

# Build Docker image for local testing
docker build -t payforadoption:dev .
```

### Container Development

```bash
# Build for production (Linux/AMD64)
docker buildx build --platform linux/amd64 -t service-name:latest .

# Build for local development
docker build -t service-name:dev .

# Run locally
docker run -p 8080:8080 service-name:dev
```

## Cleanup

### Destroy Resources

```bash
# Destroy all CDK stacks
cdk -a "npx ts-node bin/local.ts" destroy --all

# Confirm destruction in AWS Console
# Clean up any remaining resources manually if needed
```

### Clean Local Environment

```bash
# Remove node modules
rm -rf node_modules

# Clean CDK cache
rm -rf cdk.out

# Reset pre-commit
pre-commit clean
```

## Best Practices

### Security

1. **Never commit AWS credentials** - Use IAM roles or AWS CLI profiles
2. **Run security scans** - Pre-commit hooks include security checks
3. **Review changes** - Always run `cdk diff` before deployment
4. **Use least privilege** - Grant minimal required AWS permissions

### Development

1. **Run deploy-check first** - Always validate environment before deployment
2. **Use feature branches** - Don't develop directly on main branch
3. **Test locally** - Use local CDK deployment for faster iteration
4. **Clean up resources** - Destroy stacks when not needed to save costs

### Collaboration

1. **Follow commit conventions** - Use conventional commit messages
2. **Run pre-commit hooks** - Ensure code quality before pushing
3. **Document changes** - Update documentation for significant changes
4. **Review PRs thoroughly** - Check both code and infrastructure changes

## Additional Resources

- [AWS CDK Documentation](https://docs.aws.amazon.com/cdk/)
- [Pre-commit Documentation](https://pre-commit.com/)
- [Application Redeployment Guide](docs/application-redeployment.md)
- [CodeBuild CDK Deployment Template](docs/codebuild-cdk-deployment-template.md)

## Support

For issues specific to this project:

1. Check existing GitHub issues
2. Create a new issue with detailed information
3. Include logs and error messages when reporting problems
