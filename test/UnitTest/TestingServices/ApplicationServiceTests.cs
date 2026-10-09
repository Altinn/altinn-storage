#nullable disable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Models;
using Altinn.Platform.Storage.Repository;
using Altinn.Platform.Storage.Services;
using Moq;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest.TestingServices;

public class ApplicationServiceTests
{
    private readonly Mock<IApplicationRepository> _applicationRepositoryMock;

    public ApplicationServiceTests()
    {
        _applicationRepositoryMock = new Mock<IApplicationRepository>();
    }

    [Fact]
    public void ValidateDataTypeForApp_Success()
    {
        // Arrange
        Application application = CreateApplication("ttd", "test-app");
        ApplicationService applicationService = new(_applicationRepositoryMock.Object);

        // Act
        (bool isValid, ServiceError serviceError) = applicationService.ValidateDataTypeForApp(
            application,
            "sign-datatype",
            "currentTask"
        );

        // Assert
        Assert.True(isValid);
        Assert.Null(serviceError);
    }

    [Fact]
    public void ValidateDataTypeForApp_Failed_InvalidDataType()
    {
        // Arrange
        Application application = CreateApplication("ttd", "test-app");
        ApplicationService applicationService = new(_applicationRepositoryMock.Object);

        // Act
        (bool isValid, ServiceError serviceError) = applicationService.ValidateDataTypeForApp(
            application,
            "invalid-datatype",
            "currentTask"
        );

        // Assert
        Assert.False(isValid);
        Assert.Equal(405, serviceError.ErrorCode);
    }

    [Fact]
    public void ValidateDataTypeForApp_Failed_InvalidTask()
    {
        // Arrange
        Application application = CreateApplication("ttd", "test-app");
        ApplicationService applicationService = new(_applicationRepositoryMock.Object);

        // Act
        (bool isValid, ServiceError serviceError) = applicationService.ValidateDataTypeForApp(
            application,
            "sign-datatype",
            "invalidTask"
        );

        // Assert
        Assert.False(isValid);
        Assert.Equal(405, serviceError.ErrorCode);
    }

    private static Application CreateApplication(string org, string appName)
    {
        Application appInfo = new Application
        {
            Id = $"{org}/{appName}",
            VersionId = "rocket",
            Title = new Dictionary<string, string>(),
            Org = org,
            DataTypes = new List<DataType>
            {
                new DataType { Id = "sign-datatype", TaskId = "currentTask" },
            },
        };

        appInfo.Title.Add("nb", "Tittel");

        return appInfo;
    }

    [Fact]
    public async Task GetApplicationOrErrorAsync_WhenFound_ReturnsApplication()
    {
        // Arrange
        string appId = "org123/app456";
        string expectedOrg = "org123";
        Application expectedApp = new();

        _applicationRepositoryMock
            .Setup(repo => repo.FindOne(appId, expectedOrg, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Application());

        ApplicationService applicationService = new(_applicationRepositoryMock.Object);

        // Act
        var (app, error) = await applicationService.GetApplicationOrErrorAsync(appId);

        // Assert
        Assert.NotNull(app);
        Assert.Null(error);
        _applicationRepositoryMock.Verify(
            repo => repo.FindOne(appId, expectedOrg, It.IsAny<CancellationToken>()),
            Times.Once
        );
    }

    [Fact]
    public async Task GetApplicationOrErrorAsync_WhenApplicationNotFound_ReturnsNotFound()
    {
        // Arrange
        string appId = "org123/app456";
        string expectedOrg = "org123";

        _applicationRepositoryMock
            .Setup(repo => repo.FindOne(appId, expectedOrg, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Application)null);

        ApplicationService applicationService = new(_applicationRepositoryMock.Object);

        // Act
        var (app, error) = await applicationService.GetApplicationOrErrorAsync(appId);

        // Assert
        Assert.Null(app);
        Assert.NotNull(error);
        ServiceError notFoundResult = Assert.IsType<ServiceError>(error);

        Assert.Equal(404, notFoundResult.ErrorCode);
        Assert.Equal($"Did not find application with appId={appId}", notFoundResult.ErrorMessage);
    }

    [Fact]
    public async Task GetApplicationOrErrorAsync_WhenExceptionThrown_ReturnsStatusInternalServerError()
    {
        // Arrange
        string appId = "org123/app456";
        string expectedOrg = "org123";
        var exception = new Exception("Test exception");

        _applicationRepositoryMock
            .Setup(repo => repo.FindOne(appId, expectedOrg, It.IsAny<CancellationToken>()))
            .ThrowsAsync(exception);

        ApplicationService applicationService = new(_applicationRepositoryMock.Object);

        // Act
        Application app = null;
        ServiceError serviceError = null;
        Exception resultingException = null;

        try
        {
            (app, serviceError) = await applicationService.GetApplicationOrErrorAsync(appId);
        }
        catch (Exception ex)
        {
            resultingException = ex;
        }

        // Assert
        Assert.Null(app);
        Assert.Null(serviceError);
        Assert.NotNull(resultingException);
        Assert.IsType<Exception>(resultingException);
        Assert.Equal("Test exception", resultingException.Message);
    }
}
