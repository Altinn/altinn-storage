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
        private readonly IHttpWrapper _httpWrapper;
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
        public CorrespondenceClient(HttpClient client, IOptions<GeneralSettings> generalSettings) : this(new HttpWrapper(client), generalSettings.Value.BridgeApiCorrespondenceEndpoint)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="CorrespondenceClient"/> class with the given HttpClient. Used for unit testing.
        /// </summary>
        /// <param name="mockClient">A Mocked HTTP client</param>
        /// <param name="mockUri">Mocked URI</param>
        public CorrespondenceClient(IHttpWrapper mockClient, string mockUri) : this(mockClient, new Uri(mockUri))
        {
        }

        private CorrespondenceClient(IHttpWrapper client, Uri endpoint)
        {
            _httpWrapper = client;

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

            _httpWrapper.AssignHttpClientSettings(endpoint, new TimeSpan(0, 0, 30));
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
            var response = await _httpWrapper.SendAsync(request);
            response.EnsureSuccessStatusCode();
        }
    }

    /// <inheritdoc/>
    public class HttpWrapper : IHttpWrapper
    {
        private readonly HttpClient _client;

        /// <summary>
        /// Initializes a new instance of the <see cref="HttpWrapper"/> class.
        /// </summary>
        /// <param name="client">Http client to forward requests to.</param>
        public HttpWrapper(HttpClient client)
        {
            _client = client;
        }

        /// <inheritdoc/>>
        public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request)
        {
            return _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        }

        /// <inheritdoc/>>
        public void AssignHttpClientSettings(Uri uri, TimeSpan timeout)
        {
            _client.BaseAddress = uri;
            _client.Timeout = timeout;
            _client.DefaultRequestHeaders.Clear();
            _client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }
    }

    /// <summary>
    /// Used to mock Http requests to Altinn 2.
    /// </summary>
    public interface IHttpWrapper
    {
        /// <summary>
        /// Wraps Http call to use in unit testing.
        /// </summary>
        /// <param name="request">Request to use in HttpClient.</param>
        /// <returns></returns>
        Task<HttpResponseMessage> SendAsync(HttpRequestMessage request);

        /// <summary>
        /// Assigns uri to internal HttpClient.
        /// </summary>
        /// <param name="uri">Uri to assign.</param>
        /// <param name="timeout">Default timeout</param>
        void AssignHttpClientSettings(Uri uri, TimeSpan timeout);
    }
}
