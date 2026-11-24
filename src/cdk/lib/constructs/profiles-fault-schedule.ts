/*
Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
SPDX-License-Identifier: Apache-2.0
*/

import { Duration, RemovalPolicy, Stack } from 'aws-cdk-lib';
import { ICluster, TaskDefinition } from 'aws-cdk-lib/aws-ecs';
import { Effect, PolicyStatement, Role, ServicePrincipal } from 'aws-cdk-lib/aws-iam';
import { Code, Function as LambdaFunction, Runtime, Tracing } from 'aws-cdk-lib/aws-lambda';
import { LogGroup, RetentionDays } from 'aws-cdk-lib/aws-logs';
import { CfnSchedule } from 'aws-cdk-lib/aws-scheduler';
import { DefinitionBody, LogLevel, StateMachine, TaskInput, Wait, WaitTime } from 'aws-cdk-lib/aws-stepfunctions';
import { LambdaInvoke } from 'aws-cdk-lib/aws-stepfunctions-tasks';
import { NagSuppressions } from 'cdk-nag';
import { Construct } from 'constructs';

export interface ProfilesFaultScheduleProperties {
    cluster: ICluster;
    serviceName: string;
    taskDefinition: TaskDefinition;
    containerName: string;
    /** EventBridge Scheduler expression, evaluated in UTC */
    scheduleExpression: string;
    faultDurationMinutes: number;
}

/** Injects the petsearch CPU regression on a schedule, then reverts it after a fixed window. */
export class ProfilesFaultSchedule extends Construct {
    constructor(scope: Construct, id: string, properties: ProfilesFaultScheduleProperties) {
        super(scope, id);
        const stack = Stack.of(this);

        const switchFunction = new LambdaFunction(this, 'SwitchFunction', {
            runtime: Runtime.PYTHON_3_14,
            handler: 'index.handler',
            code: Code.fromAsset('../applications/lambda/profiles-fault-switch-python', {
                exclude: ['test_*.py', '__pycache__'],
            }),
            timeout: Duration.minutes(1),
            memorySize: 256,
            tracing: Tracing.ACTIVE,
            description: 'Switches petsearch between its clean build and the profiles CPU regression',
            environment: {
                CLUSTER_NAME: properties.cluster.clusterName,
                SERVICE_NAME: properties.serviceName,
                CONTAINER_NAME: properties.containerName,
                FAULT_NAME: 'cpu-regex',
            },
            logGroup: new LogGroup(this, 'SwitchFunctionLogGroup', {
                retention: RetentionDays.ONE_WEEK,
                removalPolicy: RemovalPolicy.DESTROY,
            }),
        });

        const serviceArn = stack.formatArn({
            service: 'ecs',
            resource: 'service',
            resourceName: `${properties.cluster.clusterName}/${properties.serviceName}`,
        });
        switchFunction.addToRolePolicy(
            new PolicyStatement({
                actions: ['ecs:DescribeServices', 'ecs:UpdateService'],
                resources: [serviceArn],
            }),
        );
        switchFunction.addToRolePolicy(
            new PolicyStatement({
                // These actions do not support resource-level permissions.
                actions: ['ecs:DescribeTaskDefinition', 'ecs:RegisterTaskDefinition'],
                resources: ['*'],
            }),
        );
        switchFunction.addToRolePolicy(
            new PolicyStatement({
                actions: ['ecs:TagResource'],
                resources: [stack.formatArn({ service: 'ecs', resource: 'task-definition', resourceName: '*' })],
                conditions: { StringEquals: { 'ecs:CreateAction': 'RegisterTaskDefinition' } },
            }),
        );
        const passRoles = [properties.taskDefinition.taskRole.roleArn];
        if (properties.taskDefinition.executionRole) {
            passRoles.push(properties.taskDefinition.executionRole.roleArn);
        }
        switchFunction.addToRolePolicy(
            new PolicyStatement({
                actions: ['iam:PassRole'],
                resources: passRoles,
                conditions: { StringEquals: { 'iam:PassedToService': 'ecs-tasks.amazonaws.com' } },
            }),
        );

        const invoke = (action: 'inject' | 'revert') =>
            new LambdaInvoke(this, action === 'inject' ? 'InjectFault' : 'RevertFault', {
                lambdaFunction: switchFunction,
                payload: TaskInput.fromObject({ action }),
                payloadResponseOnly: true,
                retryOnServiceExceptions: true,
            });

        const revert = invoke('revert');
        revert.addRetry({ maxAttempts: 3, interval: Duration.seconds(30) });
        const inject = invoke('inject');
        // Revert even when inject fails partway, so the service never stays on a faulty revision.
        inject.addCatch(revert, { resultPath: '$.error' });

        const stateMachine = new StateMachine(this, 'StateMachine', {
            definitionBody: DefinitionBody.fromChainable(
                inject
                    .next(
                        new Wait(this, 'FaultWindow', {
                            time: WaitTime.duration(Duration.minutes(properties.faultDurationMinutes)),
                        }),
                    )
                    .next(revert),
            ),
            timeout: Duration.minutes(properties.faultDurationMinutes + 30),
            tracingEnabled: true,
            logs: {
                destination: new LogGroup(this, 'StateMachineLogGroup', {
                    retention: RetentionDays.ONE_WEEK,
                    removalPolicy: RemovalPolicy.DESTROY,
                }),
                level: LogLevel.ALL,
            },
        });

        const schedulerRole = new Role(this, 'SchedulerRole', {
            assumedBy: new ServicePrincipal('scheduler.amazonaws.com', {
                conditions: { StringEquals: { 'aws:SourceAccount': stack.account } },
            }),
        });
        schedulerRole.addToPolicy(
            new PolicyStatement({
                effect: Effect.ALLOW,
                actions: ['states:StartExecution'],
                resources: [stateMachine.stateMachineArn],
            }),
        );

        new CfnSchedule(this, 'Schedule', {
            description: 'Daily petsearch CPU regression for the profiles use case',
            scheduleExpression: properties.scheduleExpression,
            scheduleExpressionTimezone: 'UTC',
            flexibleTimeWindow: { mode: 'OFF' },
            target: {
                arn: stateMachine.stateMachineArn,
                roleArn: schedulerRole.roleArn,
                retryPolicy: { maximumRetryAttempts: 0 },
            },
        });

        NagSuppressions.addResourceSuppressions(
            this,
            [
                { id: 'AwsSolutions-IAM4', reason: 'Lambda basic execution managed policy for the switch function' },
                {
                    id: 'AwsSolutions-IAM5',
                    reason: 'RegisterTaskDefinition and DescribeTaskDefinition have no resource-level permissions; Lambda and X-Ray defaults',
                },
            ],
            true,
        );
    }
}
