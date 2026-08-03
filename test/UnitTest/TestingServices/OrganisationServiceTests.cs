using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Models;
using Altinn.Platform.Storage.Repository;
using Altinn.Platform.Storage.Services;
using Moq;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest.TestingServices;

public class OrganisationServiceTests
{
    [Fact]
    public async Task GetOrgNumber_KnownCode_ReturnsOrgNumber()
    {
        // Arrange
        OrganisationService service = SetupService(
            new Dictionary<string, Org>
            {
                {
                    "ttd",
                    new Org { Orgnr = "991825827" }
                },
            }
        );

        // Act
        string? orgNumber = await service.GetOrgNumber("ttd", CancellationToken.None);

        // Assert
        Assert.Equal("991825827", orgNumber);
    }

    [Fact]
    public async Task GetOrgNumber_UnknownCode_ReturnsNull()
    {
        // Arrange
        OrganisationService service = SetupService(
            new Dictionary<string, Org>
            {
                {
                    "ttd",
                    new Org { Orgnr = "991825827" }
                },
            }
        );

        // Act
        string? orgNumber = await service.GetOrgNumber("unknown", CancellationToken.None);

        // Assert
        Assert.Null(orgNumber);
    }

    [Fact]
    public async Task GetOrgNumber_NullCode_ThrowsArgumentNullException()
    {
        // Arrange — a null code is a contract violation, not a "no match"
        Mock<IOrganisationRepository> repositoryMock = new(MockBehavior.Strict);
        OrganisationService service = new(repositoryMock.Object);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            service.GetOrgNumber(null!, CancellationToken.None)
        );
        repositoryMock.Verify(r => r.GetOrganisations(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetOrgNumber_EmptyCode_ReturnsNull()
    {
        // Arrange — an empty code is a legitimate "no owner", treated as a miss
        OrganisationService service = SetupService(new Dictionary<string, Org>());

        // Act
        string? orgNumber = await service.GetOrgNumber(string.Empty, CancellationToken.None);

        // Assert
        Assert.Null(orgNumber);
    }

    [Fact]
    public async Task GetOrgNumber_RepositoryThrows_PropagatesException()
    {
        // Arrange
        Mock<IOrganisationRepository> repositoryMock = new();
        repositoryMock
            .Setup(r => r.GetOrganisations(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("CDN unavailable"));
        OrganisationService service = new(repositoryMock.Object);

        // Act & Assert
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            service.GetOrgNumber("ttd", CancellationToken.None)
        );
    }

    private static OrganisationService SetupService(Dictionary<string, Org> organisations)
    {
        Mock<IOrganisationRepository> repositoryMock = new();
        repositoryMock
            .Setup(r => r.GetOrganisations(It.IsAny<CancellationToken>()))
            .ReturnsAsync(organisations);
        return new OrganisationService(repositoryMock.Object);
    }
}
