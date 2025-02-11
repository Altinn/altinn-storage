using System;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Helpers;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Repository;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest.HelperTests;

public class ApplicationHelperTests
{
    private readonly Mock<IApplicationRepository> _mockRepository;
    private readonly ApplicationHelper _applicationHelper;

    public ApplicationHelperTests()
    {
        _mockRepository = new Mock<IApplicationRepository>();
        _applicationHelper = new ApplicationHelper(_mockRepository.Object);
    }

    [Fact]
    public async Task GetApplicationOrErrorAsync_WhenFound_ReturnsApplication()
    {
        // Arrange
        string appId = "org123/app456";
        string expectedOrg = "org123";
        Application expectedApp = new();

        _mockRepository
            .Setup(repo => repo.FindOne(appId, expectedOrg))
            .ReturnsAsync(new Application());

        // Act
        var (app, error) = await _applicationHelper.GetApplicationOrErrorAsync(appId);

        // Assert
        Assert.NotNull(app);
        Assert.Null(error);
        _mockRepository.Verify(repo => repo.FindOne(appId, expectedOrg), Times.Once);
    }

    [Fact]
    public async Task GetApplicationOrErrorAsync_WhenApplicationNotFound_ReturnsNotFound()
    {
        // Arrange
        string appId = "org123/app456";
        string expectedOrg = "org123";

        _mockRepository
            .Setup(repo => repo.FindOne(appId, expectedOrg))
            .ReturnsAsync((Application)null);

        // Act
        var (app, error) = await _applicationHelper.GetApplicationOrErrorAsync(appId);

        // Assert
        Assert.Null(app);
        Assert.NotNull(error);
        var notFoundResult = Assert.IsType<NotFoundObjectResult>(error);

        Assert.Equal(404, notFoundResult.StatusCode);
        Assert.Equal($"Did not find application with appId={appId}", notFoundResult.Value);
    }

    [Fact]
    public async Task GetApplicationOrErrorAsync_WhenExceptionThrown_Returns500()
    {
        // Arrange
        string appId = "org123/app456";
        string expectedOrg = "org123";
        var exception = new Exception("Test exception");

        _mockRepository
            .Setup(repo => repo.FindOne(appId, expectedOrg))
            .ThrowsAsync(exception);

        // Act
        var (app, error) = await _applicationHelper.GetApplicationOrErrorAsync(appId);

        // Assert
        Assert.Null(app);
        Assert.NotNull(error);
        var objectResult = Assert.IsType<ObjectResult>(error);
        Assert.Equal(500, objectResult.StatusCode);

        Assert.Contains("Unable to perform request:", objectResult.Value.ToString());
        Assert.Contains("Test exception", objectResult.Value.ToString());
    }
}
