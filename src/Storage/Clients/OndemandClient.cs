using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

using Altinn.Platform.Storage.Configuration;

using Microsoft.Extensions.Options;

namespace Altinn.Platform.Storage.Clients
{
    /// <summary>
    /// Represents an implementation of <see cref="IOndemandClient"/> using a HttpClient.
    /// </summary>
    public class OndemandClient : IOndemandClient
    {
        private readonly HttpClient _client;

        /// <summary>
        /// Initializes a new instance of the <see cref="OndemandClient"/> class with the given HttpClient and GeneralSettings.
        /// </summary>
        /// <param name="client">A HttpClient provided by a HttpClientFactory.</param>
        /// <param name="generalSettings">The general settings configured for Storage.</param>
        public OndemandClient(HttpClient client, IOptions<GeneralSettings> generalSettings)
        {
            _client = client;
            _client.BaseAddress = new Uri(generalSettings.Value.OndemandEndpoint);
            _client.Timeout = new TimeSpan(0, 0, 30);
        }

        /// <inheritdoc />
        public async Task<Stream> GetStreamAsync(string path)
        {
            return await _client.GetStreamAsync(path);
        }
    }
}