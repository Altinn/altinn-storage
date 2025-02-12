using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Models;
using Altinn.Platform.Storage.Repository;
using Altinn.Platform.Storage.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest.TestingServices
{
    public class ApplicationServiceTests
    {
        private readonly Mock<IApplicationRepository> _applicationRepositoryMock;
        private readonly Mock<ILogger<ApplicationService>> _loggerMock;

        public ApplicationServiceTests()
        {
            _applicationRepositoryMock = new Mock<IApplicationRepository>();
            _loggerMock = new Mock<ILogger<ApplicationService>>();
        }

        [Fact]
        public async Task ValidateDataTypeForApp_Success()
        {
            // Arrange
            Application application = CreateApplication("ttd", "test-app");

            _applicationRepositoryMock.Setup(arm => arm.FindOne(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(application);

            ApplicationService applicationService = new(_applicationRepositoryMock.Object, _loggerMock.Object);

            // Act
            (bool isValid, ServiceError serviceError) = await applicationService.ValidateDataTypeForApp("ttd", "test-app", "sign-datatype", "currentTask");

            // Assert
            Assert.True(isValid);
            Assert.Null(serviceError);
            _applicationRepositoryMock.VerifyAll();
        }

        [Fact]
        public async Task ValidateDataTypeForApp_Failed_AppNotExists()
        {
            // Arrange
            ApplicationService applicationService = new(_applicationRepositoryMock.Object, _loggerMock.Object);

            // Act
            (bool isValid, ServiceError serviceError) = await applicationService.ValidateDataTypeForApp("ttd", "test-app", "sign-datatype", "currentTask");

            // Assert
            Assert.False(isValid);
            Assert.Equal(404, serviceError.ErrorCode);
        }

        [Fact]
        public async Task ValidateDataTypeForApp_Failed_InvalidDataType()
        {
            // Arrange
            Application application = CreateApplication("ttd", "test-app");

            _applicationRepositoryMock.Setup(arm => arm.FindOne(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(application);

            ApplicationService applicationService = new(_applicationRepositoryMock.Object, _loggerMock.Object);

            // Act
            (bool isValid, ServiceError serviceError) = await applicationService.ValidateDataTypeForApp("ttd", "test-app", "invalid-datatype", "currentTask");

            // Assert
            Assert.False(isValid);
            Assert.Equal(405, serviceError.ErrorCode);
            _applicationRepositoryMock.VerifyAll();
        }

        [Fact]
        public async Task ValidateDataTypeForApp_Failed_InvalidTask()
        {
            // Arrange
            Application application = CreateApplication("ttd", "test-app");

            _applicationRepositoryMock.Setup(arm => arm.FindOne(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(application);

            ApplicationService applicationService = new(_applicationRepositoryMock.Object, _loggerMock.Object);

            // Act
            (bool isValid, ServiceError serviceError) = await applicationService.ValidateDataTypeForApp("ttd", "test-app", "sign-datatype", "invalidTask");

            // Assert
            Assert.False(isValid);
            Assert.Equal(405, serviceError.ErrorCode);
            _applicationRepositoryMock.VerifyAll();
        }

        private static Application CreateApplication(string org, string appName)
        {
            Application appInfo = new Application
            {
                Id = $"{org}/{appName}",
                VersionId = "rocket",
                Title = new Dictionary<string, string>(),
                Org = org,
                DataTypes = new List<DataType> { new DataType { Id = "sign-datatype", TaskId = "currentTask" } }
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
                .Setup(repo => repo.FindOne(appId, expectedOrg))
                .ReturnsAsync(new Application());

            ApplicationService applicationService = new(_applicationRepositoryMock.Object, _loggerMock.Object);

            // Act
            var (app, error) = await applicationService.GetApplicationOrErrorAsync(appId);

            // Assert
            Assert.NotNull(app);
            Assert.Null(error);
            _applicationRepositoryMock.Verify(repo => repo.FindOne(appId, expectedOrg), Times.Once);
        }

        [Fact]
        public async Task GetApplicationOrErrorAsync_WhenApplicationNotFound_ReturnsNotFound()
        {
            // Arrange
            string appId = "org123/app456";
            string expectedOrg = "org123";

            _applicationRepositoryMock
                .Setup(repo => repo.FindOne(appId, expectedOrg))
                .ReturnsAsync((Application)null);

            ApplicationService applicationService = new(_applicationRepositoryMock.Object, _loggerMock.Object);

            // Act
            var (app, error) = await applicationService.GetApplicationOrErrorAsync(appId);

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

            _applicationRepositoryMock
                .Setup(repo => repo.FindOne(appId, expectedOrg))
                .ThrowsAsync(exception);

            ApplicationService applicationService = new(_applicationRepositoryMock.Object, _loggerMock.Object);

            // Act
            var (app, error) = await applicationService.GetApplicationOrErrorAsync(appId);

            // Assert
            Assert.Null(app);
            Assert.NotNull(error);
            var objectResult = Assert.IsType<ObjectResult>(error);
            Assert.Equal(500, objectResult.StatusCode);

            Assert.Contains("Unable to perform request:", objectResult.Value.ToString());
            Assert.Contains("Test exception", objectResult.Value.ToString());
        }
    }
}
