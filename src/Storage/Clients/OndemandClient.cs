using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

using Altinn.Platform.Storage.Configuration;
using AltinnCore.Authentication.Utils;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Altinn.Platform.Storage.Clients;

/// <summary>
/// Represents an implementation of <see cref="IOnDemandClient"/> using a HttpClient.
/// </summary>
public class OnDemandClient : IOnDemandClient
{
    private readonly HttpClient _client;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly GeneralSettings _generalSettings;

    /// <summary>
    /// Initializes a new instance of the <see cref="OnDemandClient"/> class with the given HttpClient and GeneralSettings.
    /// </summary>
    /// <param name="client">A HttpClient provided by a HttpClientFactory.</param>
    /// <param name="httpContextAccessor">Http context access</param>
    /// <param name="generalSettings">The general settings configured for Storage.</param>
    public OnDemandClient(HttpClient client, IHttpContextAccessor httpContextAccessor, IOptions<GeneralSettings> generalSettings)
    {
        _client = client;
        _client.BaseAddress = new Uri(generalSettings.Value.OndemandEndpoint);
        _client.Timeout = new TimeSpan(0, 0, 30);
        _httpContextAccessor = httpContextAccessor;
        _generalSettings = generalSettings.Value;
    }

    /// <inheritdoc />
    public async Task<Stream> GetStreamAsync(string path)
    {
        string token = JwtTokenUtil.GetTokenFromContext(_httpContextAccessor.HttpContext, _generalSettings.RuntimeCookieName);
        JwtTokenUtil.AddTokenToRequestHeader(_client, token);
        return await _client.GetStreamAsync(path);
    }
}