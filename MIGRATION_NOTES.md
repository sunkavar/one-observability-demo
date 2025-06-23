# Migration from OTEL Collector to CloudWatch Agent

## Changes Made

This branch migrates the PetSearch Java service from using OTEL Collector sidecar to CloudWatch Agent with Application Signals integration.

### Key Changes:

1. **ECS Service Configuration (`ecs-service.ts`)**:
   - Added `@aws-cdk/aws-applicationsignals-alpha` import
   - Added `serviceName` to `EcsServiceProps` interface
   - Added new `cloudwatch` instrumentation case using `ApplicationSignalsIntegration`
   - Removed manual CloudWatch agent container method

2. **Service Configuration (`services.ts`)**:
   - Changed PetSearch service from `instrumentation: 'otel'` to `instrumentation: 'cloudwatch'`
   - Added `serviceName: 'PetSearch'` parameter

3. **Package Dependencies (`package.json`)**:
   - Added `@aws-cdk/aws-applicationsignals-alpha` dependency

4. **Application Configuration**:
   - No changes needed to the Java application Dockerfile
   - Application continues to send OTLP traces to `localhost:4317`
   - CloudWatch Agent sidecar will receive traces on port 4317

### Benefits:

- **Native AWS Integration**: Uses AWS Application Signals for better integration with CloudWatch
- **Automatic Instrumentation**: Application Signals provides automatic Java instrumentation
- **Simplified Configuration**: No need for manual OTEL collector configuration files
- **Enhanced Observability**: Better integration with CloudWatch dashboards and alarms

### Deployment:

The CloudWatch Agent sidecar will be automatically deployed with:
- Container name: `ecs-cwagent`
- CPU: 256
- Memory: 512 MiB
- Logging enabled

### Telemetry Flow:

```
Java App (OTLP) → CloudWatch Agent Sidecar → AWS Application Signals → CloudWatch
```

The application continues to use the AWS OTEL Java agent but now sends telemetry to the CloudWatch Agent instead of the OTEL Collector.
