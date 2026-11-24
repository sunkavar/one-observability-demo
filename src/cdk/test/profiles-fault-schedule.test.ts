/*
Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
SPDX-License-Identifier: Apache-2.0
*/

import { App, Aspects, Stack } from 'aws-cdk-lib';
import { Annotations, Match, Template } from 'aws-cdk-lib/assertions';
import { Vpc } from 'aws-cdk-lib/aws-ec2';
import { Cluster, ContainerImage, FargateTaskDefinition, LogDrivers } from 'aws-cdk-lib/aws-ecs';
import { AwsSolutionsChecks } from 'cdk-nag';
import { ProfilesFaultSchedule } from '../lib/constructs/profiles-fault-schedule';
import { WorkshopNagPack } from '../lib/utils/utilities';

function synthesize() {
    const app = new App();
    const stack = new Stack(app, 'TestStack', { env: { account: '111122223333', region: 'us-east-1' } });
    const cluster = Cluster.fromClusterAttributes(stack, 'Cluster', {
        clusterName: 'PetsiteECS-cluster',
        vpc: Vpc.fromVpcAttributes(stack, 'Vpc', { vpcId: 'vpc-1', availabilityZones: ['us-east-1a'] }),
    });
    const taskDefinition = new FargateTaskDefinition(stack, 'TaskDefinition');
    taskDefinition.addContainer('container', {
        image: ContainerImage.fromRegistry('petsearch'),
        logging: LogDrivers.awsLogs({ streamPrefix: 'petsearch' }),
    });
    new ProfilesFaultSchedule(stack, 'Fault', {
        cluster,
        serviceName: 'petsearch-java',
        taskDefinition,
        containerName: 'container',
        scheduleExpression: 'cron(0 16 * * ? *)',
        faultDurationMinutes: 60,
    });
    Aspects.of(stack).add(new AwsSolutionsChecks());
    Aspects.of(stack).add(new WorkshopNagPack());
    return { stack, template: Template.fromStack(stack) };
}

const { stack, template } = synthesize();

test('schedules the state machine daily in UTC', () => {
    template.hasResourceProperties('AWS::Scheduler::Schedule', {
        ScheduleExpression: 'cron(0 16 * * ? *)',
        ScheduleExpressionTimezone: 'UTC',
    });
});

test('injects, waits for the fault window, then reverts', () => {
    const machine = Object.values(template.findResources('AWS::StepFunctions::StateMachine'))[0];
    const definition = JSON.stringify(machine.Properties.DefinitionString);
    expect(definition).toContain('\\"InjectFault\\"');
    expect(definition).toContain('\\"Seconds\\":3600');
    expect(definition).toContain('\\"RevertFault\\"');
});

test('scopes UpdateService to the petsearch service and PassRole to its task roles', () => {
    template.hasResourceProperties('AWS::IAM::Policy', {
        PolicyDocument: {
            Statement: Match.arrayWith([
                Match.objectLike({
                    Action: ['ecs:DescribeServices', 'ecs:UpdateService'],
                    Resource: {
                        'Fn::Join': [
                            '',
                            Match.arrayWith([Match.stringLikeRegexp('service/PetsiteECS-cluster/petsearch-java')]),
                        ],
                    },
                }),
                Match.objectLike({
                    Action: 'iam:PassRole',
                    Condition: { StringEquals: { 'iam:PassedToService': 'ecs-tasks.amazonaws.com' } },
                }),
            ]),
        },
    });
});

test('passes cdk-nag', () => {
    const errors = Annotations.fromStack(stack).findError('*', Match.stringLikeRegexp('.*'));
    expect(errors.filter((e) => e.id.startsWith('/TestStack/Fault/'))).toEqual([]);
});
