# Quick Start

:material-rocket-launch: **Pick the path that matches you and follow only that section.**

<div class="grid cards" markdown>

- :material-school: **AWS Workshop**

    ---

    Deployed for you automatically. Nothing to set up.

    [:octicons-arrow-right-24: Jump to section](#aws-workshops)

- :material-cloud-upload: **Your own AWS account**

    ---

    Deploy via CloudFormation using S3 or CodeConnection.

    [:octicons-arrow-right-24: Jump to section](#deploy-to-your-own-account-via-cloudformation)

- :material-laptop: **Rapid local development**

    ---

    Run the CDK app from your checkout and iterate fast.

    [:octicons-arrow-right-24: Jump to section](#rapid-local-development)

</div>

---

## :material-school: AWS Workshops

!!! success "Nothing to do"
    If you are following this through an AWS-run workshop, the environment is **deployed for you automatically**. No setup, no CloudFormation, no instructions needed. Start from the workshop guide.

---

## :material-cloud-upload: Deploy to your own account via CloudFormation

Use this to stand the demo up in a **personal or team AWS account**. The CloudFormation template provisions a CodeBuild project that runs the CDK pipeline. The pipeline needs a **source** for the code, and there are two methods to provide it.

### Which method should I use?

| | :material-bucket: **S3** (Method 1) | :material-source-branch: **CodeConnection** (Method 2) :material-star: |
|---|---|---|
| | | **Recommended** |
| **Best for** | Deploying the demo as-is | Changing the code and iterating |
| **GitHub** | Not required | Fork required |
| **Lifecycle** | One-off / throwaway | Push-to-deploy, ongoing |

!!! tip "CodeConnection is recommended"
    Use CodeConnection (Method 2) for anything beyond a one-off deploy: every push to your fork is picked up by CodePipeline and redeployed, with no S3 step. Use S3 (Method 1) only to deploy the demo as-is without a fork.

### Prerequisites

- AWS CLI v2 configured for the target account (`aws sts get-caller-identity` returns the right account).
- Permission to run CloudFormation, CodeBuild, and CDK bootstrap in that account.
- The template uploaded to S3.

!!! warning "Use `--template-url`, not `--template-body`"
    The template exceeds CloudFormation's 51,200-byte inline limit, so it must be uploaded to S3 and referenced with `--template-url`.

### :material-bucket: Method 1: S3 source

CodeBuild clones the public repo and uses a template-created S3 bucket as the pipeline source. No GitHub account or connection required.

```bash
aws cloudformation create-stack \
  --stack-name OneObservability-CDK \
  --template-url https://<your-bucket>.s3.amazonaws.com/codebuild-deployment-template.yaml \
  --capabilities CAPABILITY_NAMED_IAM \
  --parameters \
    ParameterKey=pOrganizationName,ParameterValue=aws-samples \
    ParameterKey=pRepositoryName,ParameterValue=one-observability-demo \
    ParameterKey=pBranchName,ParameterValue=main \
    ParameterKey=pWorkingFolder,ParameterValue=src/cdk
```

### :material-source-branch: Method 2: CodeConnection source (Recommended)

!!! note "Recommended for ongoing code changes"
    Tracks your fork so your commits drive deployments. After a one-time GitHub connection, push to your fork and CodePipeline redeploys automatically.

**Step 1: Fork the repo.**
In GitHub, fork `aws-samples/one-observability-demo` into your own account.

**Step 2: Create a CodeConnection and authorize it.**

```bash
aws codeconnections create-connection \
  --provider-type GitHub \
  --connection-name one-observability-demo
```

The connection is created in `PENDING` state.

!!! info "One manual step"
    Open the AWS Console, go to **Developer Tools -> Connections**, click **Update pending connection**, and complete the GitHub authorization against your fork. Copy the resulting connection ARN. This console step is the one part that cannot be scripted.

**Step 3: Deploy with the connection ARN**, pointing the organization/repo at your fork:

```bash
aws cloudformation create-stack \
  --stack-name OneObservability-CDK \
  --template-url https://<your-bucket>.s3.amazonaws.com/codebuild-deployment-template.yaml \
  --capabilities CAPABILITY_NAMED_IAM \
  --parameters \
    ParameterKey=pCodeConnectionArn,ParameterValue=arn:aws:codeconnections:<region>:<account>:connection/<id> \
    ParameterKey=pOrganizationName,ParameterValue=<your-github-username> \
    ParameterKey=pRepositoryName,ParameterValue=one-observability-demo \
    ParameterKey=pBranchName,ParameterValue=<your-branch> \
    ParameterKey=pWorkingFolder,ParameterValue=src/cdk
```

When `pCodeConnectionArn` is set, it is used as the source instead of S3.

---

## :material-laptop: Rapid local development

For fast iteration without redeploying the whole stack, run the CDK app directly from your checkout.

#### Step 1: Clone and configure

```bash
cd src/cdk

# Recommended: CodeConnection source (your fork drives deployments)
cp .env.sample.codeconnection .env

# ...or S3 source (deploy from a bucket, no GitHub fork)
# cp .env.sample.s3 .env
```

Edit `.env` and fill in your account ID, region, and (for CodeConnection) the connection ARN and your fork's org/branch. Both samples ship with Waggle AI agents on and OpenSearch off.

#### Step 2: Bootstrap (one time per account/region)

```bash
npx cdk bootstrap aws://<account>/us-east-1
```

#### Step 3: Deploy

```bash
cdk -a "npx ts-node bin/local.ts" list        # see the stacks
cdk -a "npx ts-node bin/local.ts" deploy --all
```

#### Step 4: Iterate on a single microservice

Changed one service and want to test it without re-running the whole pipeline? Rebuild just that container to ECR and roll the one service with `redeploy-app.sh`:

```bash
./src/cdk/scripts/redeploy-app.sh
```

It prompts for the service and platform, builds and pushes the image, then for an ECS service forces a new deployment automatically, and for the EKS service (`petsite-net`) prints the `kubectl rollout restart` to run. See [Application Redeployment](redeployment.md) for the per-host test and verification steps.

!!! tip "Prefer the pipeline for a durable change"
    `redeploy-app.sh` is for a fast local test loop. To make the change stick in the deployed environment, push it so the pipeline redeploys from source (this is why Method 2, CodeConnection, is recommended):

    ```bash
    git add -A && git commit -m "..." && git push origin <your-branch>
    ```

---

## :material-layers: What gets deployed

The deployment creates a CDK Pipeline that provisions resources in 5 stages:

| # | Stage | Resources |
|---|---|---|
| 1 | **Core** | VPC, security groups, VPC endpoints, CloudTrail, EventBridge, OpenSearch |
| 2 | **Containers** | Container image builds for all 6 microservices |
| 3 | **Storage** | DynamoDB, Aurora PostgreSQL, S3, SQS, data seeding |
| 4 | **Compute** | ECS cluster, EKS cluster, load balancers |
| 5 | **Microservices** | Service deployments, Lambda functions, canaries, WAF |

For full architecture details, see the [Architecture Overview](../architecture/overview.md).

---

## :material-broom: Cleanup

```bash
# Primary cleanup
cdk destroy --all

# Find remaining resources
npm run cleanup -- --discover

# Clean specific leftovers
npm run cleanup -- --stack-name MyStack --dry-run
npm run cleanup -- --stack-name MyStack
```

See [Cleanup Script](../operations/cleanup.md) and [CDK Cleanup](../operations/cdk-cleanup.md) for detailed instructions.
