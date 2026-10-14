using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Common.AccessTokenClient.Services;
using Altinn.Platform.Storage.Configuration;
using Altinn.Platform.Storage.Exceptions;
using Altinn.Register.Contracts;
using Microsoft.Extensions.Options;

namespace Altinn.Platform.Storage.Services;

/// <summary>
/// Looks up parties through the internal party query API (v2) in Platform Register.
/// </summary>
public class RegisterService : IRegisterService
{
    /// <summary>
    /// The Register fields Storage needs to build an instance owner.
    /// </summary>
    internal const string InstanceOwnerFields = "identifiers,type,display-name,user,si";

    private const string PartyIdFields = "id";

    private static readonly JsonSerializerOptions _serializerOptions = JsonSerializerOptions.Web;

    private readonly HttpClient _client;
    private readonly IAccessTokenGenerator _accessTokenGenerator;

    /// <summary>
    /// Initializes a new instance of the <see cref="RegisterService"/> class.
    /// </summary>
    public RegisterService(
        HttpClient httpClient,
        IAccessTokenGenerator accessTokenGenerator,
        IOptions<RegisterServiceSettings> registerServiceSettings
    )
    {
        httpClient.BaseAddress = registerServiceSettings.Value.GetInternalApiEndpoint();
        _client = httpClient;
        _accessTokenGenerator = accessTokenGenerator;
    }

    /// <inheritdoc/>
    public Task<Party?> GetParty(int partyId, CancellationToken cancellationToken) =>
        QuerySingleParty(
            PartyUrn.PartyId.Create((uint)partyId),
            InstanceOwnerFields,
            cancellationToken
        );

    /// <inheritdoc/>
    public async Task<int?> LookupPartyId(PartyUrn identifier, CancellationToken cancellationToken)
    {
        Party? party = await QuerySingleParty(identifier, PartyIdFields, cancellationToken);
        return party?.PartyId.HasValue == true ? (int)party.PartyId.Value : null;
    }

    private async Task<Party?> QuerySingleParty(
        PartyUrn identifier,
        string fields,
        CancellationToken cancellationToken
    )
    {
        using HttpRequestMessage request = new(HttpMethod.Post, $"parties/query?fields={fields}")
        {
            Content = JsonContent.Create(new PartyQuery([identifier]), options: _serializerOptions),
        };
        request.Headers.Add(
            "PlatformAccessToken",
            _accessTokenGenerator.GenerateAccessToken("platform", "storage")
        );

        using HttpResponseMessage response = await _client.SendAsync(request, cancellationToken);

        // Register answers 206 when some of the requested parties are missing; with a single
        // identifier that means an empty list.
        if (
            response.StatusCode is not HttpStatusCode.OK
            && response.StatusCode is not HttpStatusCode.PartialContent
        )
        {
            throw await PlatformHttpException.CreateAsync(response);
        }

        PartyList? result = await response.Content.ReadFromJsonAsync<PartyList>(
            _serializerOptions,
            cancellationToken
        );

        return result?.Data.FirstOrDefault();
    }

    private sealed record PartyQuery(
        [property: JsonPropertyName("data")] IEnumerable<PartyUrn> Data
    );

    private sealed record PartyList([property: JsonPropertyName("data")] IReadOnlyList<Party> Data);
}
