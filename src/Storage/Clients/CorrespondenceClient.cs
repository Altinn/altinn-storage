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
        /// <summary>
        /// Initializes a new instance of <see cref="CorrespondenceClient"/> and configures the provided <see cref="HttpClient"/> with the bridge API endpoint, a 30-second timeout, and an Accept header for "application/json".
        /// </summary>
        /// <remarks>
        /// The constructor assigns the provided HttpClient to the client field and sets its BaseAddress from <c>GeneralSettings.BridgeApiCorrespondenceEndpoint</c>.
        /// </remarks>
        /// <exception cref="InvalidOperationException">Thrown when <c>GeneralSettings.BridgeApiCorrespondenceEndpoint</c> is not configured (null).</exception>
        public CorrespondenceClient(HttpClient client, IOptions<GeneralSettings> generalSettings)
        {
            _client = client;
            _client.BaseAddress = generalSettings.Value.BridgeApiCorrespondenceEndpoint;
            if (_client.BaseAddress is null)
            {
                throw new InvalidOperationException("GeneralSettings.BridgeApiCorrespondenceEndpoint must be configured.");
            }

            _client.Timeout = new TimeSpan(0, 0, 30);
            _client.DefaultRequestHeaders.Clear();
            _client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }

        /// <summary>
        /// Asynchronously notifies the bridge API about a correspondence event (read, confirm, or delete).
        /// </summary>
        /// <param name="eventType">The type of event to sync. Accepted case-insensitive values: "read", "confirm", "delete".</param>
        /// <param name="eventTimeStamp">The timestamp of the event; it is converted to UTC and sent in ISO 8601 format.</param>
        /// <returns>A <see cref="Task"/> representing the asynchronous HTTP POST operation.</returns>
        /// <exception cref="ArgumentException">Thrown when <paramref name="eventType"/> is not one of the accepted values.</exception>
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
