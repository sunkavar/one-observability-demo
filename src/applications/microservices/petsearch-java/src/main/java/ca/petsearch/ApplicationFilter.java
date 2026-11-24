/*
Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
SPDX-License-Identifier: Apache-2.0
*/
package ca.petsearch;

import org.springframework.web.servlet.HandlerMapping;
import org.springframework.web.util.ContentCachingResponseWrapper;

import javax.servlet.*;
import javax.servlet.http.HttpServletRequest;
import javax.servlet.http.HttpServletResponse;
import java.io.IOException;

public class ApplicationFilter implements Filter {

    private final MetricEmitter metricEmitter;

    public ApplicationFilter(MetricEmitter metricEmitter) {
        this.metricEmitter = metricEmitter;
    }

    @Override
    public void doFilter(ServletRequest request, ServletResponse response, FilterChain chain) throws IOException, ServletException {

        long requestStartTime = System.currentTimeMillis();

        ContentCachingResponseWrapper responseWrapper = new ContentCachingResponseWrapper((HttpServletResponse) response);

        chain.doFilter(request, responseWrapper);

        int loadSize = responseWrapper.getContentSize();

        responseWrapper.copyBodyToResponse();

        String statusCode = String.valueOf(((HttpServletResponse)response).getStatus());

        // Route template, not the raw path, so /pets/{pet_id} does not create one metric series per pet.
        Object route = request.getAttribute(HandlerMapping.BEST_MATCHING_PATTERN_ATTRIBUTE);
        String apiName = route != null ? route.toString() : ((HttpServletRequest)request).getServletPath();

        metricEmitter.emitReturnTimeMetric(System.currentTimeMillis() - requestStartTime, apiName, statusCode);

        metricEmitter.emitBytesSentMetric(loadSize, apiName, statusCode);
    }
}
