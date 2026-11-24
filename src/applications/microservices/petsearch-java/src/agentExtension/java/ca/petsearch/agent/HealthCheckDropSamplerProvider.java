/*
Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
SPDX-License-Identifier: Apache-2.0
*/
package ca.petsearch.agent;

import io.opentelemetry.api.common.AttributeKey;
import io.opentelemetry.api.common.Attributes;
import io.opentelemetry.api.trace.SpanKind;
import io.opentelemetry.context.Context;
import io.opentelemetry.sdk.autoconfigure.spi.ConfigProperties;
import io.opentelemetry.sdk.autoconfigure.spi.traces.ConfigurableSamplerProvider;
import io.opentelemetry.sdk.trace.data.LinkData;
import io.opentelemetry.sdk.trace.samplers.Sampler;
import io.opentelemetry.sdk.trace.samplers.SamplingResult;

import java.util.List;

public class HealthCheckDropSamplerProvider implements ConfigurableSamplerProvider {

    @Override
    public Sampler createSampler(ConfigProperties config) {
        return new HealthCheckDropSampler(Sampler.parentBased(Sampler.alwaysOn()));
    }

    @Override
    public String getName() {
        return "petsearch_parentbased_always_on";
    }

    static final class HealthCheckDropSampler implements Sampler {
        private static final AttributeKey<String> URL_PATH = AttributeKey.stringKey("url.path");
        private final Sampler delegate;

        HealthCheckDropSampler(Sampler delegate) {
            this.delegate = delegate;
        }

        @Override
        public SamplingResult shouldSample(Context parentContext, String traceId, String name, SpanKind spanKind,
                                           Attributes attributes, List<LinkData> parentLinks) {
            if (spanKind == SpanKind.SERVER && "/health/status".equals(attributes.get(URL_PATH))) {
                return SamplingResult.drop();
            }
            return delegate.shouldSample(parentContext, traceId, name, spanKind, attributes, parentLinks);
        }

        @Override
        public String getDescription() {
            return "HealthCheckDropSampler{" + delegate.getDescription() + "}";
        }
    }
}
