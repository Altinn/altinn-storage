using System;
using Altinn.Platform.Storage.Interface.Models;
using Newtonsoft.Json;
using Xunit;

namespace Altinn.Platform.Storage.Interface.Tests.InstanceOwnerTests;

public class InstanceOwnerSerializationTests
{
    [Fact]
    public void Serialize_WithPartyUuid_WritesCamelCasePartyUuid()
    {
        InstanceOwner owner = new()
        {
            PartyId = "1337",
            PartyUuid = Guid.Parse("4c3b4909-eb17-45d5-bde1-256e065e196a"),
        };

        string json = JsonConvert.SerializeObject(owner);

        Assert.Equal(
            "{\"partyId\":\"1337\",\"partyUuid\":\"4c3b4909-eb17-45d5-bde1-256e065e196a\"}",
            json
        );
    }

    [Fact]
    public void Serialize_WithoutPartyUuid_OmitsPartyUuid()
    {
        InstanceOwner owner = new() { PartyId = "1337" };

        string json = JsonConvert.SerializeObject(owner);

        Assert.Equal("{\"partyId\":\"1337\"}", json);
    }

    [Fact]
    public void Deserialize_WithoutPartyUuid_LeavesPartyUuidNull()
    {
        InstanceOwner? owner = JsonConvert.DeserializeObject<InstanceOwner>(
            "{\"partyId\":\"1337\",\"personNumber\":\"01017512345\"}"
        );

        Assert.NotNull(owner);
        Assert.Equal("1337", owner.PartyId);
        Assert.Null(owner.PartyUuid);
    }

    [Fact]
    public void SystemTextJson_WebDefaults_RoundTripsPartyUuid()
    {
        Guid partyUuid = Guid.Parse("4c3b4909-eb17-45d5-bde1-256e065e196a");
        InstanceOwner owner = new() { PartyId = "1337", PartyUuid = partyUuid };

        string json = System.Text.Json.JsonSerializer.Serialize(
            owner,
            System.Text.Json.JsonSerializerOptions.Web
        );
        InstanceOwner? roundTripped = System.Text.Json.JsonSerializer.Deserialize<InstanceOwner>(
            json,
            System.Text.Json.JsonSerializerOptions.Web
        );

        Assert.Contains("\"partyUuid\":\"4c3b4909-eb17-45d5-bde1-256e065e196a\"", json);
        Assert.NotNull(roundTripped);
        Assert.Equal(partyUuid, roundTripped.PartyUuid);
    }
}
