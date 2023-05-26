using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Authorization;
using Altinn.Platform.Storage.Configuration;
using Altinn.Platform.Storage.Controllers;
using Altinn.Platform.Storage.Interface.Enums;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Repository;
using Altinn.Platform.Storage.Services;
using Altinn.Platform.Storage.UnitTest.Utils;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Cosmos;
using Moq;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest.TestingControllers
{
    public class DataLockControllerUnitTests
    {
        private static List<string> _forbiddenUpdateProps = new List<string>()
            { "/created", "/createdBy", "/id", "/instanceGuid", "/blobStoragePath", "/dataType", "/contentType", "/filename", "/lastChangedBy", "/lastChanged", "/refs", "/size", "/fileScanResult", "/tags", "/deleteStatus", };

        private int _instanceOwnerPartyId = 1337;
        private string _org = "ttd";
        private string _appId = "ttd/apps-test";
        private string _dataType = "attachment";

        [Fact]
        public async Task Lock_patches_locked_property_when_not_locked()
        {
            // Arrange
            List<string> expectedPropertiesForPatch = new() { "/locked" };
            (DataLockController testController, Mock<IDataRepository> dataRepositoryMock, Mock<IInstanceRepository> instanceRepoMock) = GetTestController(expectedPropertiesForPatch, true);

            // Act
            var instanceGuid = Guid.NewGuid();
            var dataElementId = Guid.NewGuid();
            var result = await testController.Lock(12345, instanceGuid, dataElementId);

            // Assert
            Assert.IsType<OkObjectResult>(result.Result);
            instanceRepoMock.Verify(i => i.GetOne(12345, instanceGuid), Times.Once);
            instanceRepoMock.VerifyNoOtherCalls();
            dataRepositoryMock.Verify(d => d.Update(instanceGuid, dataElementId, It.Is<Dictionary<string, object>>(p => VerifyPropertyListInput(expectedPropertiesForPatch.Count, expectedPropertiesForPatch, p))), Times.Once);
            dataRepositoryMock.VerifyNoOtherCalls();
        }
        
        [Fact]
        public async Task Lock_returns_StatusCode_from_CosmosExpcetion()
        {
            // Arrange
            List<string> expectedPropertiesForPatch = new() { "/locked" };
            (DataLockController testController, Mock<IDataRepository> dataRepositoryMock, Mock<IInstanceRepository> instanceRepoMock) = GetTestController(expectedPropertiesForPatch, true, new CosmosException("NotFound", System.Net.HttpStatusCode.NotFound, 0, string.Empty, 0));

            // Act
            var instanceGuid = Guid.NewGuid();
            var dataElementId = Guid.NewGuid();
            var result = await testController.Lock(12345, instanceGuid, dataElementId);
            
            // Assert
            Assert.IsType<StatusCodeResult>(result.Result);
            Assert.Equal(404, ((StatusCodeResult)result.Result).StatusCode);
            instanceRepoMock.Verify(i => i.GetOne(12345, instanceGuid), Times.Once);
            instanceRepoMock.VerifyNoOtherCalls();
            dataRepositoryMock.Verify(d => d.Update(instanceGuid, dataElementId, It.Is<Dictionary<string, object>>(p => VerifyPropertyListInput(expectedPropertiesForPatch.Count, expectedPropertiesForPatch, p))), Times.Once);
            dataRepositoryMock.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task UnLock_patches_locked_property()
        {
            // Arrange
            List<string> expectedPropertiesForPatch = new() { "/locked" };
            (DataLockController testController, Mock<IDataRepository> dataRepositoryMock, Mock<IInstanceRepository> instanceRepoMock) = GetTestController(expectedPropertiesForPatch, true);

            // Act
            var instanceGuid = Guid.NewGuid();
            var dataElementId = Guid.NewGuid();
            var result = await testController.Unlock(12345, instanceGuid, dataElementId);
            
            // Assert
            Assert.IsType<OkObjectResult>(result.Result);
            instanceRepoMock.Verify(i => i.GetOne(12345, instanceGuid), Times.Once);
            instanceRepoMock.VerifyNoOtherCalls();
            dataRepositoryMock.Verify(d => d.Update(instanceGuid, dataElementId, It.Is<Dictionary<string, object>>(p => VerifyPropertyListInput(expectedPropertiesForPatch.Count, expectedPropertiesForPatch, p))), Times.Once);
            dataRepositoryMock.VerifyNoOtherCalls();
        }
        
        [Fact]
        public async Task UnLock_returns_StatusCode_from_CosmosExpcetion()
        {
            // Arrange
            List<string> expectedPropertiesForPatch = new() { "/locked" };
            (DataLockController testController, Mock<IDataRepository> dataRepositoryMock, Mock<IInstanceRepository> instanceRepoMock) = GetTestController(expectedPropertiesForPatch, true, new CosmosException("NotFound", System.Net.HttpStatusCode.NotFound, 0, string.Empty, 0));

            // Act
            var instanceGuid = Guid.NewGuid();
            var dataElementId = Guid.NewGuid();
            var result = await testController.Unlock(12345, instanceGuid, dataElementId);
            
            // Assert
            Assert.IsType<StatusCodeResult>(result.Result);
            Assert.Equal(404, ((StatusCodeResult)result.Result).StatusCode);
            instanceRepoMock.Verify(i => i.GetOne(12345, instanceGuid), Times.Once);
            instanceRepoMock.VerifyNoOtherCalls();
            dataRepositoryMock.Verify(d => d.Update(instanceGuid, dataElementId, It.Is<Dictionary<string, object>>(p => VerifyPropertyListInput(expectedPropertiesForPatch.Count, expectedPropertiesForPatch, p))), Times.Once);
            dataRepositoryMock.VerifyNoOtherCalls();
        }
        
        [Fact]
        public async Task UnLock_returns_Forbidden_if_unauthorized()
        {
            // Arrange
            List<string> expectedPropertiesForPatch = new() { "/locked" };
            (DataLockController testController, Mock<IDataRepository> dataRepositoryMock, Mock<IInstanceRepository> instanceRepoMock) = GetTestController(expectedPropertiesForPatch, false);

            // Act
            var instanceGuid = Guid.NewGuid();
            var dataElementId = Guid.NewGuid();
            var result = await testController.Unlock(12345, instanceGuid, dataElementId);
            
            // Assert
            Assert.IsType<ForbidResult>(result.Result);
            instanceRepoMock.Verify(i => i.GetOne(12345, instanceGuid), Times.Once);
            instanceRepoMock.VerifyNoOtherCalls();
            dataRepositoryMock.VerifyNoOtherCalls();
        }

        private static bool VerifyPropertyListInput(int expectedPropCount, List<string> expectedProperties, Dictionary<string, object> propertyList)
        {
            if (propertyList.Count != expectedPropCount)
            {
                throw new ArgumentOutOfRangeException(nameof(propertyList), "Property list does not contain expected number of properties");
            }

            foreach (string expectedProp in expectedProperties)
            {
                if (!propertyList.ContainsKey(expectedProp))
                {
                    return false;
                }
            }

            if (propertyList.Keys.Intersect(_forbiddenUpdateProps).Any())
            {
                throw new ArgumentException("Forbidden property attempted updated in dataElement. Check `_forbiddenUpdateProps` for reference", nameof(propertyList));
            }

            return true;
        }

        private (DataLockController TestController, Mock<IDataRepository> DataRepositoryMock, Mock<IInstanceRepository>) GetTestController(List<string> expectedPropertiesForPatch, bool authorized, CosmosException exception = null)
        {
            Mock<IDataRepository> dataRepositoryMock = new();
            Mock<IInstanceRepository> instanceRepositoryMock = new();
            Mock<IAuthorization> authorizationMock = new();
            
            if (exception == null)
            {
                dataRepositoryMock
                    .Setup(
                        d => d.Update(
                            It.IsAny<Guid>(),
                            It.IsAny<Guid>(),
                            It.Is<Dictionary<string, object>>(propertyList => VerifyPropertyListInput(expectedPropertiesForPatch.Count, expectedPropertiesForPatch, propertyList))))
                    .ReturnsAsync(new DataElement());
            }
            else
            {
                dataRepositoryMock
                    .Setup(
                        d => d.Update(
                            It.IsAny<Guid>(),
                            It.IsAny<Guid>(),
                            It.Is<Dictionary<string, object>>(propertyList => VerifyPropertyListInput(expectedPropertiesForPatch.Count, expectedPropertiesForPatch, propertyList))))
                    .ThrowsAsync(exception);
            }

            authorizationMock
                .Setup(a => a.AuthorizeAnyOfInstanceActions(It.IsAny<Instance>(), It.IsAny<List<string>>(), It.IsAny<string>()))
                .ReturnsAsync(authorized);

            instanceRepositoryMock
                .Setup(ir => ir.GetOne(It.IsAny<int>(), It.IsAny<Guid>()))
                .ReturnsAsync((int partyId, Guid instanceGuid) =>
                {
                    return new Instance
                    {
                        Id = $"{partyId}/{instanceGuid}",
                        InstanceOwner = new()
                        {
                            PartyId = partyId.ToString()
                        },
                        Process = new()
                        {
                            CurrentTask = new()
                            {
                                ElementId = "Task_1"
                            }
                        },
                        Org = _org,
                        AppId = _appId
                    };
                });

            Mock<HttpContext> httpContextMock = new();
            httpContextMock
                .Setup(c => c.User).Returns(PrincipalUtil.GetPrincipal(200001, 1337));

            ControllerContext controllerContext = new ControllerContext()
            {
                HttpContext = httpContextMock.Object
            };

            var sut = new DataLockController(
                instanceRepositoryMock.Object,
                dataRepositoryMock.Object,
                authorizationMock.Object)
            {
                ControllerContext = controllerContext
            };

            return (sut, dataRepositoryMock, instanceRepositoryMock);
        }
    }
}
