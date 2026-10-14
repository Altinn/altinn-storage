using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Common.AccessTokenClient.Services;
using Altinn.Platform.Storage.Configuration;
using Altinn.Platform.Storage.Exceptions;
using Altinn.Platform.Storage.Services;
using Altinn.Platform.Storage.UnitTest.Mocks.Clients;
using Altinn.Register.Contracts;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest.TestingServices;

public class RegisterServiceTest
{
    private const string _accessToken = "platform-access-token";

    private readonly List<(HttpRequestMessage Request, string Body)> _requests = [];

    [Fact]
    public async Task GetParty_QueriesInternalApiByPartyIdWithPlatformAccessToken()
    {
        // Arrange
        RegisterService target = CreateTarget(
            HttpStatusCode.OK,
            PartyList(PersonJson(1337, "25871999336"))
        );

        // Act
        await target.GetParty(1337, CancellationToken.None);

        // Assert
        (HttpRequestMessage request, string body) = Assert.Single(_requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(
            "http://localhost:5101/register/api/v2/internal/parties/query?fields=identifiers,type,display-name,user,si",
            request.RequestUri!.ToString()
        );
        Assert.Equal(_accessToken, Assert.Single(request.Headers.GetValues("PlatformAccessToken")));
        Assert.False(request.Headers.Contains("Authorization"));
        Assert.Equal("{\"data\":[\"urn:altinn:party:id:1337\"]}", body);
    }

    [Fact]
    public async Task GetParty_PartyFound_ReturnsParty()
    {
        // Arrange
        RegisterService target = CreateTarget(
            HttpStatusCode.OK,
            PartyList(PersonJson(1337, "25871999336"))
        );

        // Act
        Party? party = await target.GetParty(1337, CancellationToken.None);

        // Assert
        Person person = Assert.IsType<Person>(party);
        Assert.Equal(1337u, person.PartyId.Value);
        Assert.Equal("25871999336", person.PersonIdentifier.ToString());
    }

    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.PartialContent)]
    public async Task GetParty_PartyMissing_ReturnsNull(HttpStatusCode statusCode)
    {
        // Arrange
        RegisterService target = CreateTarget(statusCode, "{\"data\":[]}");

        // Act
        Party? party = await target.GetParty(1337, CancellationToken.None);

        // Assert
        Assert.Null(party);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task GetParty_UnsuccessfulResponse_ThrowsPlatformHttpException(
        HttpStatusCode statusCode
    )
    {
        // Arrange
        RegisterService target = CreateTarget(statusCode, "error");

        // Act
        PlatformHttpException exception = await Assert.ThrowsAsync<PlatformHttpException>(() =>
            target.GetParty(1337, CancellationToken.None)
        );

        // Assert
        Assert.Equal(statusCode, exception.Response.StatusCode);
    }

    [Fact]
    public async Task LookupPartyId_SendsIdentifierAndReturnsPartyId()
    {
        // Arrange
        RegisterService target = CreateTarget(
            HttpStatusCode.OK,
            PartyList(PersonJson(500000, "25871999336"))
        );
        PartyUrn identifier = PartyUrn.PersonId.Create(PersonIdentifier.Parse("25871999336"));

        // Act
        int? partyId = await target.LookupPartyId(identifier, CancellationToken.None);

        // Assert
        Assert.Equal(500000, partyId);
        (HttpRequestMessage request, string body) = Assert.Single(_requests);
        Assert.Equal(
            "http://localhost:5101/register/api/v2/internal/parties/query?fields=id",
            request.RequestUri!.ToString()
        );
        Assert.Equal("{\"data\":[\"urn:altinn:person:identifier-no:25871999336\"]}", body);
    }

    [Fact]
    public async Task LookupPartyId_PartyMissing_ReturnsNull()
    {
        // Arrange
        RegisterService target = CreateTarget(HttpStatusCode.PartialContent, "{\"data\":[]}");
        PartyUrn identifier = PartyUrn.PartyUuid.Create(Guid.NewGuid());

        // Act
        int? partyId = await target.LookupPartyId(identifier, CancellationToken.None);

        // Assert
        Assert.Null(partyId);
    }

    [Theory]
    [InlineData("https://platform.at22.altinn.cloud/register/api/v1/")]
    [InlineData("https://platform.at22.altinn.cloud/register/api/v1")]
    public void GetInternalApiEndpoint_ResolvesV2InternalApiNextToV1Api(string v1Endpoint)
    {
        // Arrange
        RegisterServiceSettings settings = new() { ApiRegisterEndpoint = v1Endpoint };

        // Act
        Uri endpoint = settings.GetInternalApiEndpoint();

        // Assert
        Assert.Equal(
            "https://platform.at22.altinn.cloud/register/api/v2/internal/",
            endpoint.ToString()
        );
    }

    private static string PersonJson(int partyId, string personNumber) =>
        $$"""
            {
              "partyType": "person",
              "partyUuid": "{{RegisterServiceMock.GetPartyUuid(partyId)}}",
              "versionId": 1,
              "partyId": {{partyId}},
              "personIdentifier": "{{personNumber}}"
            }
            """;

    private static string PartyList(string partyJson) => $"{{\"data\":[{partyJson}]}}";

    private RegisterService CreateTarget(HttpStatusCode statusCode, string responseBody)
    {
        Mock<IAccessTokenGenerator> accessTokenGenerator = new();
        accessTokenGenerator
            .Setup(generator => generator.GenerateAccessToken("platform", "storage"))
            .Returns(_accessToken);

        HttpClient httpClient = new(new RecordingHandler(_requests, statusCode, responseBody));

        return new RegisterService(
            httpClient,
            accessTokenGenerator.Object,
            Options.Create(
                new RegisterServiceSettings
                {
                    ApiRegisterEndpoint = "http://localhost:5101/register/api/v1/",
                }
            )
        );
    }

    private sealed class RecordingHandler(
        List<(HttpRequestMessage Request, string Body)> requests,
        HttpStatusCode statusCode,
        string responseBody
    ) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            string body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            requests.Add((request, body));

            return new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json"),
            };
        }
    }
}
