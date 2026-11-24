"""Switch an ECS service between its clean build and the profiles CPU regression."""

import os
from datetime import datetime, timezone

import boto3

ecs = boto3.client("ecs")

FAULT_ENV = "PETSEARCH_FAULT"
VERSION_KEY = "service.version"

# describe_task_definition returns these, register_task_definition rejects them.
READ_ONLY_FIELDS = (
    "taskDefinitionArn",
    "revision",
    "status",
    "requiresAttributes",
    "compatibilities",
    "registeredAt",
    "registeredBy",
    "deregisteredAt",
)


def set_resource_attribute(value, key, new_value):
    parts = [p for p in (value or "").split(",") if p and not p.startswith(f"{key}=")]
    parts.append(f"{key}={new_value}")
    return ",".join(parts)


def switch_container(container, action, fault_name, version):
    env = [e for e in container.get("environment", []) if e["name"] != FAULT_ENV]
    if action == "inject":
        env.append({"name": FAULT_ENV, "value": fault_name})
    for entry in env:
        if entry["name"] == "OTEL_RESOURCE_ATTRIBUTES":
            entry["value"] = set_resource_attribute(entry["value"], VERSION_KEY, version)
            break
    else:
        env.append({"name": "OTEL_RESOURCE_ATTRIBUTES", "value": f"{VERSION_KEY}={version}"})
    container["environment"] = env
    return container


def build_version(action, fault_name, now):
    prefix = f"fault-{fault_name}" if action == "inject" else "clean"
    return f"{prefix}-{now.strftime('%Y%m%dT%H%MZ')}"


def handler(event, _context):
    action = event.get("action")
    if action not in ("inject", "revert"):
        raise ValueError(f"action must be inject or revert, got {action!r}")

    cluster = os.environ["CLUSTER_NAME"]
    service = os.environ["SERVICE_NAME"]
    container_name = os.environ["CONTAINER_NAME"]
    fault_name = os.environ.get("FAULT_NAME", "cpu-regex")
    version = build_version(action, fault_name, datetime.now(timezone.utc))

    current_arn = ecs.describe_services(cluster=cluster, services=[service])["services"][0]["taskDefinition"]
    described = ecs.describe_task_definition(taskDefinition=current_arn, include=["TAGS"])
    task_def = described["taskDefinition"]

    containers = task_def["containerDefinitions"]
    if not any(c["name"] == container_name for c in containers):
        raise ValueError(f"container {container_name!r} not found in {current_arn}")
    task_def["containerDefinitions"] = [
        switch_container(c, action, fault_name, version) if c["name"] == container_name else c for c in containers
    ]

    for field in READ_ONLY_FIELDS:
        task_def.pop(field, None)
    tags = [t for t in described.get("tags", []) if not t["key"].startswith("aws:")]
    if tags:
        task_def["tags"] = tags

    new_arn = ecs.register_task_definition(**task_def)["taskDefinition"]["taskDefinitionArn"]
    ecs.update_service(cluster=cluster, service=service, taskDefinition=new_arn)
    print(f"{action}: {service} {current_arn} -> {new_arn} ({VERSION_KEY}={version})")
    return {"action": action, "version": version, "taskDefinition": new_arn}
