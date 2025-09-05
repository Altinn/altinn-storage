using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;

using Altinn.Platform.Storage.Configuration;

using Microsoft.Extensions.Options;

namespace Altinn.Platform.Storage.Clients
{
    /// <summary>
    /// Represents an implementation of <see cref="ICorrespondenceClient"/> using a HttpClient.
    /// </summary>
    public class CorrespondenceClient : ICorrespondenceClient
    {
        private readonly HttpClient _client;
        private readonly Dictionary<string, string> _routes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["read"] = "syncmarkasread",
            ["confirm"] = "syncconfirm",
            ["delete"] = "syncdelete"
        };

        /// <summary>
        /// Initializes a new instance of the <see cref="CorrespondenceClient"/> class with the given HttpClient and GeneralSettings.
        /// </summary>
        /// <param name="client">A HttpClient provided by a HttpClientFactory.</param>
        /// <param name="generalSettings">The general settings configured for Storage.</param>
        public CorrespondenceClient(HttpClient client, IOptions<GeneralSettings> generalSettings)
        {
            _client = client;
            var endpoint = generalSettings.Value.BridgeApiCorrespondenceEndpoint;
            if (endpoint is null)
            {
                throw new InvalidOperationException("GeneralSettings.BridgeApiCorrespondenceEndpoint must be configured.");
            }
            
            // Ensure trailing slash so relative routes append rather than replace the last segment
            var endpointStr = endpoint.ToString().Trim();
            if (!endpointStr.EndsWith('/'))
            {
                endpoint = new Uri(endpointStr + "/", UriKind.Absolute);
            }
            
            _client.BaseAddress = endpoint;

            _client.Timeout = new TimeSpan(0, 0, 30);
            _client.DefaultRequestHeaders.Clear();
            _client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }

        /// <inheritdoc />
        public async Task SyncCorrespondenceEvent(int correspondenceId, int partyId, DateTimeOffset eventTimeStamp, string eventType)
        {
            if (!_routes.TryGetValue(eventType, out string route))
            {
                throw new ArgumentException($"Invalid event type: {eventType}", nameof(eventType));
            }

            string url = Microsoft.AspNetCore.WebUtilities.QueryHelpers.AddQueryString(
            route,
            new System.Collections.Generic.Dictionary<string, string>
            {
                ["seReporteeElementId"] = correspondenceId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["partyId"] = partyId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["eventOccurredUtc"] = eventTimeStamp.UtcDateTime.ToString("O", System.Globalization.CultureInfo.InvariantCulture)
            });

            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
        }
    }
}
