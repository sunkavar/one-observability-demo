#nullable enable
using System;
using System.Diagnostics;

namespace PetSite.Helpers
{
    /// <summary>Records a caught exception on the request's server span, as the OpenTelemetry exception event.</summary>
    public static class SpanExceptions
    {
        private const string RecordedKey = "otel.exception.recorded";

        public static void Record(Exception exception)
        {
            // Controllers catch and return a result, so the instrumentation never sees these exceptions.
            if (exception.Data.Contains(RecordedKey))
            {
                return;
            }
            var span = ServerSpan(Activity.Current);
            if (span == null)
            {
                return;
            }
            exception.Data[RecordedKey] = true;
            span.SetStatus(ActivityStatusCode.Error, exception.Message);
            span.AddEvent(new ActivityEvent("exception", tags: new ActivityTagsCollection
            {
                { "exception.type", exception.GetType().FullName },
                { "exception.message", exception.Message },
                { "exception.stacktrace", exception.ToString() },
            }));
        }

        private static Activity? ServerSpan(Activity? activity)
        {
            for (var current = activity; current != null; current = current.Parent)
            {
                if (current.Kind == ActivityKind.Server)
                {
                    return current;
                }
            }
            return activity;
        }
    }
}
