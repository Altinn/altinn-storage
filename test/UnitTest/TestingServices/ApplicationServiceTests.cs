using System.Collections.Generic;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Models;
using Altinn.Platform.Storage.Repository;
using Altinn.Platform.Storage.Services;
using Moq;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest.TestingServices
{
    public class ApplicationServiceTests
    {
        [Fact]
        public async Task ValidateDataTypeForApp_Success()
        {
            // Arrange
            Application application = CreateApplication("ttd", "test-app");
            
            Mock<IApplicationRepository> applicationRepositoryMock = new Mock<IApplicationRepository>();
            applicationRepositoryMock.Setup(arm => arm.FindOne(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(application);

            ApplicationService applicationService = new ApplicationService(applicationRepositoryMock.Object); 

            // Act
            (bool isValid, ServiceError serviceError) = await applicationService.ValidateDataTypeForApp("ttd", "test-app", "sign-datatype");

            // Assert
            Assert.True(isValid);
            Assert.Null(serviceError);
            applicationRepositoryMock.VerifyAll();
        }

        [Fact]
        public async Task ValidateDataTypeForApp_Failed_AppNotExists()
        {
            // Arrange
            Mock<IApplicationRepository> applicationRepositoryMock = new Mock<IApplicationRepository>();

            ApplicationService applicationService = new ApplicationService(applicationRepositoryMock.Object); 

            // Act
            (bool isValid, ServiceError serviceError) = await applicationService.ValidateDataTypeForApp("ttd", "test-app", "sign-datatype");

            // Assert
            Assert.False(isValid);
            Assert.Equal(404, serviceError.ErrorCode);
        }

        [Fact]
        public async Task ValidateDataTypeForApp_Failed_InvalidDataType()
        {
            // Arrange
            Application application = CreateApplication("ttd", "test-app");
            
            Mock<IApplicationRepository> applicationRepositoryMock = new Mock<IApplicationRepository>();
            applicationRepositoryMock.Setup(arm => arm.FindOne(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(application);

            ApplicationService applicationService = new ApplicationService(applicationRepositoryMock.Object); 

            // Act
            (bool isValid, ServiceError serviceError) = await applicationService.ValidateDataTypeForApp("ttd", "test-app", "invalid-datatype");

            // Assert
            Assert.False(isValid);
            Assert.Equal(405, serviceError.ErrorCode);
            applicationRepositoryMock.VerifyAll();
        }

        private static Application CreateApplication(string org, string appName)
        {
            Application appInfo = new Application
            {
                Id = $"{org}/{appName}",
                VersionId = "rocket",
                Title = new Dictionary<string, string>(),
                Org = org,
                DataTypes = new List<DataType> { new DataType { Id = "sign-datatype" } }
            };

            appInfo.Title.Add("nb", "Tittel");

            return appInfo;
        }
    }
}