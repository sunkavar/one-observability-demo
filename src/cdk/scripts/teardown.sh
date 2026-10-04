#!/bin/bash

# Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
# SPDX-License-Identifier: Apache-2.0

# Teardown for the slim personal-account deployment. Deletes the CDK application
# stacks (found by the application tag), sweeps tagged leftovers via npm run
# cleanup, then deletes the bootstrap stack. No CDK knowledge needed.
#
# Usage: ./teardown.sh [--bootstrap-stack NAME] [--region REGION]
#          [--application-tag NAME] [--profile NAME] [--dry-run] [--yes]

set -euo pipefail

RED='\033[0;31m'; GREEN='\033[0;32m'; YELLOW='\033[1;33m'; BLUE='\033[0;34m'; NC='\033[0m'

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
CDK_DIR="$(dirname "$SCRIPT_DIR")"

BOOTSTRAP_STACK="OneObservability-CDK"
REGION="${AWS_REGION:-us-east-1}"
DRY_RUN="false"
ASSUME_YES="false"
PROFILE_ARG=""
APPLICATION_TAG="One Observability Workshop"

usage() {
    sed -n '6,14p' "$0" | sed 's/^# \{0,1\}//'
    exit 0
}

while [[ $# -gt 0 ]]; do
    case "$1" in
        --bootstrap-stack) BOOTSTRAP_STACK="$2"; shift 2 ;;
        --region)          REGION="$2"; shift 2 ;;
        --application-tag) APPLICATION_TAG="$2"; shift 2 ;;
        --profile)         PROFILE_ARG="--profile $2"; shift 2 ;;
        --dry-run)         DRY_RUN="true"; shift ;;
        --yes|-y)          ASSUME_YES="true"; shift ;;
        -h|--help)         usage ;;
        *) echo -e "${RED}Unknown option: $1${NC}"; echo "Run with --help for usage."; exit 1 ;;
    esac
done

AWS="aws $PROFILE_ARG --region $REGION"

echo -e "${BLUE}One Observability Demo - Teardown${NC}"
echo "========================================"
echo "  Region:            $REGION"
echo "  Bootstrap stack:   $BOOTSTRAP_STACK"
echo "  Application tag:   $APPLICATION_TAG"
echo "  Dry run:           $DRY_RUN"
echo ""

if ! command -v aws &>/dev/null; then
    echo -e "${RED}Error: AWS CLI is not installed.${NC}"; exit 1
fi
if ! $AWS sts get-caller-identity &>/dev/null; then
    echo -e "${RED}Error: no valid AWS credentials for region $REGION.${NC}"
    echo "Configure credentials first (e.g. 'aws sso login' or 'aws configure'), then re-run."
    exit 1
fi
ACCOUNT_ID="$($AWS sts get-caller-identity --query Account --output text)"
echo -e "${GREEN}Authenticated to account ${ACCOUNT_ID} in ${REGION}.${NC}"
echo ""

echo -e "${BLUE}Step 1/3: Finding CDK application stacks to delete...${NC}"
APP_STACKS="$($AWS cloudformation describe-stacks \
    --query "Stacks[?StackName!='${BOOTSTRAP_STACK}'] | [?Tags[?Key=='application' && Value=='${APPLICATION_TAG}']].StackName" \
    --output text 2>/dev/null || true)"

if [[ -z "$APP_STACKS" ]]; then
    echo -e "${YELLOW}  No CDK application stacks found with tag application='${APPLICATION_TAG}'.${NC}"
    echo "  (If you deployed with a different pApplicationName, re-run with --application-tag '<NAME>'.)"
else
    echo "  Found these application stacks:"
    for s in $APP_STACKS; do echo "    - $s"; done
fi
echo ""

if [[ "$DRY_RUN" == "false" && "$ASSUME_YES" == "false" ]]; then
    echo -e "${YELLOW}This will DELETE the stacks above, sweep tagged resources, and delete the"
    echo -e "bootstrap stack '${BOOTSTRAP_STACK}'. This is not reversible.${NC}"
    read -r -p "Type 'yes' to proceed: " CONFIRM
    [[ "$CONFIRM" == "yes" ]] || { echo "Aborted."; exit 0; }
    echo ""
fi

delete_stack() {
    local name="$1"
    if [[ "$DRY_RUN" == "true" ]]; then
        echo -e "  ${YELLOW}[dry-run]${NC} would delete stack: $name"
        return
    fi
    echo "  Deleting stack: $name"
    $AWS cloudformation delete-stack --stack-name "$name"
    echo "  Waiting for $name to delete..."
    $AWS cloudformation wait stack-delete-complete --stack-name "$name" \
        && echo -e "  ${GREEN}Deleted: $name${NC}" \
        || echo -e "  ${RED}Delete did not complete for $name. Check the CloudFormation console for the failed resource, then re-run.${NC}"
}

if [[ -n "$APP_STACKS" ]]; then
    for s in $APP_STACKS; do delete_stack "$s"; done
fi
echo ""

echo -e "${BLUE}Step 2/3: Sweeping tagged leftover resources...${NC}"
CLEANUP_FLAGS="--discover"
[[ "$DRY_RUN" == "true" ]] && CLEANUP_FLAGS="$CLEANUP_FLAGS --dry-run"
(
    cd "$CDK_DIR"
    if [[ -n "$PROFILE_ARG" ]]; then export AWS_PROFILE="${PROFILE_ARG#--profile }"; fi
    export AWS_REGION="$REGION"
    echo "  Running: npm run cleanup -- $CLEANUP_FLAGS"
    npm run --silent cleanup -- $CLEANUP_FLAGS || echo -e "  ${YELLOW}Cleanup sweep reported issues; review its output above.${NC}"
)
echo ""

echo -e "${BLUE}Step 3/3: Deleting the bootstrap stack...${NC}"
if $AWS cloudformation describe-stacks --stack-name "$BOOTSTRAP_STACK" &>/dev/null; then
    delete_stack "$BOOTSTRAP_STACK"
else
    echo -e "${YELLOW}  Bootstrap stack '${BOOTSTRAP_STACK}' not found; nothing to delete.${NC}"
fi
echo ""

if [[ "$DRY_RUN" == "true" ]]; then
    echo -e "${GREEN}Dry run complete. Nothing was deleted. Re-run without --dry-run to tear down.${NC}"
else
    echo -e "${GREEN}Teardown complete.${NC}"
    echo "If any stack failed to delete, open the CloudFormation console, find the"
    echo "resource that blocked it (often a non-empty S3 bucket or an ENI), remove it,"
    echo "and re-run this script."
fi
