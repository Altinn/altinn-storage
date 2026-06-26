using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Mime;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Configuration;
using Altinn.Platform.Storage.Models;
using Altinn.Platform.Storage.Repository;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest.TestingRepositories;

public class AltinnCdnOrganisationRepositoryTests
{
    private const string OrgsUrl = "https://altinncdn.no/orgs/altinn-orgs.json";

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    [Fact]
    public async Task GetOrganisations_ReturnsOrgsKeyedByServiceOwnerCode()
    {
        // Arrange
        Mock<HttpMessageHandler> handlerMock = CreateHandler(OkResponse(DigdirOrgList()));
        AltinnCdnOrganisationRepository repository = CreateRepository(handlerMock);

        // Act
        IReadOnlyDictionary<string, Org> organisations = await repository.GetOrganisations(
            CancellationToken.None
        );

        // Assert
        Assert.True(organisations.ContainsKey("digdir"));
        Assert.Equal("991825827", organisations["digdir"].Orgnr);
    }

    [Fact]
    public async Task GetOrganisations_NonSuccessStatusCode_ThrowsHttpRequestException()
    {
        // Arrange
        Mock<HttpMessageHandler> handlerMock = CreateHandler(
            new HttpResponseMessage { StatusCode = HttpStatusCode.InternalServerError }
        );
        AltinnCdnOrganisationRepository repository = CreateRepository(handlerMock);

        // Act & Assert
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            repository.GetOrganisations(CancellationToken.None)
        );
    }

    [Fact]
    public async Task GetOrganisations_InvalidJson_ThrowsJsonException()
    {
        // Arrange
        Mock<HttpMessageHandler> handlerMock = CreateHandler(OkResponse("not valid json"));
        AltinnCdnOrganisationRepository repository = CreateRepository(handlerMock);

        // Act & Assert
        await Assert.ThrowsAsync<JsonException>(() =>
            repository.GetOrganisations(CancellationToken.None)
        );
    }

    [Fact]
    public async Task GetOrganisations_DeserializesToNull_ThrowsInvalidOperationException()
    {
        // Arrange
        Mock<HttpMessageHandler> handlerMock = CreateHandler(OkResponse("null"));
        AltinnCdnOrganisationRepository repository = CreateRepository(handlerMock);

        // Act & Assert
        await Assert.ThrowsAsync<System.InvalidOperationException>(() =>
            repository.GetOrganisations(CancellationToken.None)
        );
    }

    [Fact]
    public async Task GetOrganisations_CachesResult_FetchesOnlyOnce()
    {
        // Arrange
        Mock<HttpMessageHandler> handlerMock = CreateHandler(OkResponse(DigdirOrgList()));
        AltinnCdnOrganisationRepository repository = CreateRepository(handlerMock);

        // Act
        await repository.GetOrganisations(CancellationToken.None);
        await repository.GetOrganisations(CancellationToken.None);

        // Assert
        handlerMock
            .Protected()
            .Verify(
                "SendAsync",
                Times.Once(),
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>()
            );
    }

    private static AltinnCdnOrganisationRepository CreateRepository(
        Mock<HttpMessageHandler> handlerMock
    )
    {
        return new AltinnCdnOrganisationRepository(
            new HttpClient(handlerMock.Object),
            new MemoryCache(new MemoryCacheOptions()),
            Options.Create(
                new GeneralSettings
                {
                    OrganisationsUrl = OrgsUrl,
                    OrganisationsCacheLifeTimeInSeconds = 60,
                }
            )
        );
    }

    private static Mock<HttpMessageHandler> CreateHandler(HttpResponseMessage response)
    {
        Mock<HttpMessageHandler> handlerMock = new(MockBehavior.Strict);
        handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>()
            )
            .ReturnsAsync(response);
        return handlerMock;
    }

    private static HttpResponseMessage OkResponse(string content)
    {
        return new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(content, Encoding.UTF8, MediaTypeNames.Application.Json),
        };
    }

    private static string DigdirOrgList()
    {
        OrgList orgList = new()
        {
            Orgs = new Dictionary<string, Org>
            {
                {
                    "digdir",
                    new Org
                    {
                        Orgnr = "991825827",
                        Name = new Dictionary<string, string>
                        {
                            { "nb", "Digitaliseringsdirektoratet" },
                        },
                    }
                },
            },
        };
        return JsonSerializer.Serialize(orgList, _jsonOptions);
    }
}
