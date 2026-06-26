using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Configuration;
using Altinn.Platform.Storage.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Altinn.Platform.Storage.Repository;

/// <summary>
/// Repository for the Altinn CDN organisation list
/// </summary>
public class AltinnCdnOrganisationRepository : IOrganisationRepository
{
    private const string _cacheKey = "altinnCdnOrgs";

    private readonly HttpClient _httpClient;
    private readonly IMemoryCache _memoryCache;
    private readonly GeneralSettings _generalSettings;
    private readonly MemoryCacheEntryOptions _cacheEntryOptions;

    /// <summary>
    /// Initializes a new instance of the <see cref="AltinnCdnOrganisationRepository"/> class.
    /// </summary>
    /// <param name="httpClient">HttpClient</param>
    /// <param name="memoryCache">The memory cache</param>
    /// <param name="generalSettings">GeneralSettings</param>
    public AltinnCdnOrganisationRepository(
        HttpClient httpClient,
        IMemoryCache memoryCache,
        IOptions<GeneralSettings> generalSettings
    )
    {
        _httpClient = httpClient;
        _memoryCache = memoryCache;
        _generalSettings = generalSettings.Value;
        _cacheEntryOptions = new MemoryCacheEntryOptions()
            .SetPriority(CacheItemPriority.High)
            .SetAbsoluteExpiration(
                new TimeSpan(0, 0, _generalSettings.OrganisationsCacheLifeTimeInSeconds)
            );
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyDictionary<string, Org>> GetOrganisations(
        CancellationToken cancellationToken
    )
    {
        if (
            _memoryCache.TryGetValue(_cacheKey, out IReadOnlyDictionary<string, Org>? organisations)
            && organisations is not null
        )
        {
            return organisations;
        }

        using HttpRequestMessage requestMessage = new(
            HttpMethod.Get,
            _generalSettings.OrganisationsUrl
        );
        using HttpResponseMessage response = await _httpClient.SendAsync(
            requestMessage,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken
        );
        response.EnsureSuccessStatusCode();

        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        OrgList? orgList = await JsonSerializer.DeserializeAsync<OrgList?>(
            stream,
            cancellationToken: cancellationToken
        );

        if (orgList?.Orgs is null)
        {
            throw new InvalidOperationException(
                $"Failed to deserialize {nameof(OrgList)} from {_generalSettings.OrganisationsUrl}"
            );
        }

        _memoryCache.Set(_cacheKey, orgList.Orgs, _cacheEntryOptions);
        return orgList.Orgs;
    }
}
