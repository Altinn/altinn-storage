using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Net.Http;
using System.Security.Claims;

using AltinnCore.Authentication.Constants;

using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.AspNetCore.Http;

namespace Altinn.Platform.Storage.Filters
{
    /// <summary>
    /// Filter to enrich request telemetry with request body for slow requests
    /// </summary>
    [ExcludeFromCodeCoverage]
    public class RequestBodyTelemetryFilter : ITelemetryProcessor
    {
        private ITelemetryProcessor Next { get; set; }

        private readonly IHttpContextAccessor _httpContextAccessor;

        /// <summary>
        /// Initializes a new instance of the <see cref="RequestBodyTelemetryFilter"/> class.
        /// </summary>
        public RequestBodyTelemetryFilter(ITelemetryProcessor next, IHttpContextAccessor httpContextAccessor)
        {
            Next = next;
            _httpContextAccessor = httpContextAccessor;
        }

        /// <inheritdoc/>
        public void Process(ITelemetry item)
        {
            RequestTelemetry request = item as RequestTelemetry;

            if (request != null && request.Url.ToString().EndsWith("sbl/instances/search") && request.Duration.TotalMilliseconds > 2000)
            {
                var content = GetContentString(_httpContextAccessor.HttpContext.Request.Body);
                request.Properties.Add("queryModel", content);
            }

            Next.Process(item);
        }

        private string GetContentString(Stream content)
        {
            var reader = new StreamReader(content);
            reader.BaseStream.Seek(0, SeekOrigin.Begin);
            return reader.ReadToEnd();
        }
    }
}
