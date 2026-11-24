using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;

namespace PetSite.Middleware
{
    public class TraceSuppressionMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly IFileProvider _webRoot;
        private static readonly string[] StaticPrefixes = { "/css", "/js", "/lib", "/images", "/favicon.ico" };

        public TraceSuppressionMiddleware(RequestDelegate next, IWebHostEnvironment env)
        {
            _next = next;
            _webRoot = env.WebRootFileProvider;
        }

        public Task InvokeAsync(HttpContext context)
        {
            var activity = Activity.Current;
            if (activity != null && ShouldSuppress(context.Request.Path))
            {
                // Injected auto-instrumentation has no in-app sampler, so unmark the server span; children inherit not-sampled.
                activity.ActivityTraceFlags &= ~ActivityTraceFlags.Recorded;
                activity.IsAllDataRequested = false;
            }
            return _next(context);
        }

        public bool ShouldSuppress(PathString path)
        {
            var value = path.Value;
            if (string.IsNullOrEmpty(value) || value == "/")
                return false;

            if (value.Equals("/health/status", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("/metrics", StringComparison.OrdinalIgnoreCase))
                return true;

            foreach (var prefix in StaticPrefixes)
            {
                if (path.StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return _webRoot.GetFileInfo(value).Exists;
        }
    }
}
