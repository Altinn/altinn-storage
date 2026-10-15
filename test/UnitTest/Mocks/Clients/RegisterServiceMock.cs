using System;
using System.Globalization;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Services;
using Altinn.Register.Contracts;

namespace Altinn.Platform.Storage.UnitTest.Mocks.Clients;

/// <summary>
/// Register where every party id belongs to a person, and no identifier can be looked up.
/// </summary>
public class RegisterServiceMock : IRegisterService
{
    public const string PersonNumber = "25871999336";

    public Task<Party?> GetParty(PartyUrn identifier, CancellationToken cancellationToken) =>
        Task.FromResult<Party?>(
            identifier is PartyUrn.PartyId partyId ? CreatePerson((int)partyId.Value) : null
        );

    public Task<int?> LookupPartyId(PartyUrn identifier, CancellationToken cancellationToken) =>
        Task.FromResult<int?>(null);

    /// <summary>
    /// Gets the party uuid the mock uses for a party id.
    /// </summary>
    public static Guid GetPartyUuid(int partyId) =>
        Guid.Parse(
            $"00000000-0000-0000-0000-{partyId.ToString("D12", CultureInfo.InvariantCulture)}"
        );

    /// <summary>
    /// Creates a person party as returned by the Register party query API.
    /// </summary>
    public static Party CreatePerson(int partyId, string personNumber = PersonNumber) =>
        Deserialize(
            $$"""
            {
              "partyType": "person",
              "partyUuid": "{{GetPartyUuid(partyId)}}",
              "versionId": 1,
              "partyId": {{partyId}},
              "displayName": "Test Testesen",
              "personIdentifier": "{{personNumber}}",
              "externalUrn": "urn:altinn:person:identifier-no:{{personNumber}}"
            }
            """
        );

    /// <summary>
    /// Deserializes a party using the options used by the Register client.
    /// </summary>
    public static Party Deserialize(string json) =>
        JsonSerializer.Deserialize<Party>(json, JsonSerializerOptions.Web)
        ?? throw new InvalidOperationException("Party json was null.");
}
