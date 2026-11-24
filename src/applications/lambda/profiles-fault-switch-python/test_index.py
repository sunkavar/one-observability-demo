import copy
import os
from datetime import datetime, timezone
from unittest import mock

os.environ.setdefault("AWS_DEFAULT_REGION", "us-east-1")

import index  # noqa: E402

ATTRS = "service.name=petsearch-api-java,service.namespace=petadoptions"
TASK_DEF = {
    "taskDefinitionArn": "arn:aws:ecs:us-east-1:111111111111:task-definition/petsearch:8",
    "revision": 8,
    "status": "ACTIVE",
    "family": "petsearch",
    "containerDefinitions": [
        {
            "name": "container",
            "environment": [{"name": "OTEL_RESOURCE_ATTRIBUTES", "value": ATTRS}],
        },
        {"name": "cloudwatch-agent", "environment": [{"name": "CW_CONFIG_CONTENT", "value": "{}"}]},
    ],
}


def env_of(container):
    return {e["name"]: e["value"] for e in container["environment"]}


def test_inject_sets_fault_and_version():
    out = index.switch_container(copy.deepcopy(TASK_DEF["containerDefinitions"][0]), "inject", "cpu-regex", "v1")
    env = env_of(out)
    assert env["PETSEARCH_FAULT"] == "cpu-regex"
    assert env["OTEL_RESOURCE_ATTRIBUTES"] == f"{ATTRS},service.version=v1"


def test_revert_removes_fault_and_replaces_version():
    container = index.switch_container(copy.deepcopy(TASK_DEF["containerDefinitions"][0]), "inject", "cpu-regex", "v1")
    env = env_of(index.switch_container(container, "revert", "cpu-regex", "v2"))
    assert "PETSEARCH_FAULT" not in env
    assert env["OTEL_RESOURCE_ATTRIBUTES"] == f"{ATTRS},service.version=v2"


def test_build_version_stamps_minute():
    now = datetime(2026, 11, 24, 16, 5, tzinfo=timezone.utc)
    assert index.build_version("inject", "cpu-regex", now) == "fault-cpu-regex-20261124T1605Z"
    assert index.build_version("revert", "cpu-regex", now) == "clean-20261124T1605Z"


def test_handler_registers_copy_and_updates_service():
    fake = mock.Mock()
    fake.describe_services.return_value = {"services": [{"taskDefinition": TASK_DEF["taskDefinitionArn"]}]}
    fake.describe_task_definition.return_value = {
        "taskDefinition": copy.deepcopy(TASK_DEF),
        "tags": [{"key": "aws:cloudformation:stack-name", "value": "x"}, {"key": "app", "value": "petsearch"}],
    }
    fake.register_task_definition.return_value = {"taskDefinition": {"taskDefinitionArn": "arn:new:9"}}
    env = {"CLUSTER_NAME": "c", "SERVICE_NAME": "s", "CONTAINER_NAME": "container"}
    with mock.patch.object(index, "ecs", fake), mock.patch.dict(os.environ, env):
        result = index.handler({"action": "inject"}, None)

    registered = fake.register_task_definition.call_args.kwargs
    assert "taskDefinitionArn" not in registered and "revision" not in registered
    assert registered["tags"] == [{"key": "app", "value": "petsearch"}]
    assert env_of(registered["containerDefinitions"][0])["PETSEARCH_FAULT"] == "cpu-regex"
    assert env_of(registered["containerDefinitions"][1]) == {"CW_CONFIG_CONTENT": "{}"}
    fake.update_service.assert_called_once_with(cluster="c", service="s", taskDefinition="arn:new:9")
    assert result["taskDefinition"] == "arn:new:9"


def test_handler_rejects_unknown_action():
    try:
        index.handler({"action": "explode"}, None)
    except ValueError:
        return
    raise AssertionError("expected ValueError")
