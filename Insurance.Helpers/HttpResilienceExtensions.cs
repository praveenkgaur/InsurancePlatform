using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Extensions.Http.Resilience;

namespace Insurance.Helpers
{
    public static class HttpResilienceExtensions
    {
        public static IHttpClientBuilder AddInsuranceHttpResilience(
            this IHttpClientBuilder builder,
            IConfiguration config,
            TimeSpan totalTimeout)
        {
            if (!config.GetValue("Resilience:Retry:Enabled", true)
                && !config.GetValue("Resilience:CircuitBreaker:Enabled", true))
                return builder;

            var retries = config.GetValue("Resilience:Retry:MaxRetries", 3);
            var minThroughput = config.GetValue("Resilience:CircuitBreaker:MinimumThroughput", 5);
            var breakSeconds = config.GetValue("Resilience:CircuitBreaker:BreakSeconds", 30);

            builder.AddStandardResilienceHandler(options =>
            {
                options.Retry.MaxRetryAttempts = Math.Max(1, retries);
                options.Retry.Delay = TimeSpan.FromMilliseconds(400);
                options.Retry.BackoffType = Polly.DelayBackoffType.Exponential;
                options.CircuitBreaker.ShouldHandle = args =>
                    ValueTask.FromResult(args.Outcome.Exception is not null
                        || (args.Outcome.Result is { } r && (int)r.StatusCode >= 500));
                options.CircuitBreaker.MinimumThroughput = Math.Max(2, minThroughput);
                options.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(breakSeconds);
                options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(Math.Max(30, breakSeconds));
                options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(Math.Max(2, totalTimeout.TotalSeconds / 2));
                options.TotalRequestTimeout.Timeout = totalTimeout;
            });
            return builder;
        }
    }
}
