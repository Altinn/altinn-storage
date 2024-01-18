using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Authorization;
using Altinn.Platform.Storage.Controllers;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Repository;
using Altinn.Platform.Storage.UnitTest.Utils;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest.TestingControllers
{
    public class DataLockControllerUnitTests
    {
        private static readonly List<string> _forbiddenUpdateProps = new List<string>()
            { "/created", "/createdBy", "/id", "/instanceGuid", "/blobStoragePath", "/dataType", "/contentType", "/filename", "/lastChangedBy", "/lastChanged", "/refs", "/size", "/fileScanResult", "/tags", "/deleteStatus", };

        private readonly string _org = "ttd";
        private readonly string _appId = "ttd/apps-test";

        [Fact]
        public async Task Lock_does_not_perform_lock_when_data_on_instance_marked_as_locked()
        {
            // Arrange
            List<string> expectedPropertiesForPatch = new() { "/locked" };
            var instanceGuid = Guid.NewGuid();
            var dataElementId = Guid.NewGuid();
            (DataLockController testController, Mock<IDataRepository> dataRepositoryMock, Mock<IInstanceRepository> instanceRepoMock) = GetTestController(expectedPropertiesForPatch, dataElementId, authorized: true, dataLocked: true);

            // Act
            var result = await testController.Lock(12345, instanceGuid, dataElementId);

            // Assert
            Assert.IsType<OkObjectResult>(result.Result);
            instanceRepoMock.Verify(i => i.GetOne(instanceGuid, true), Times.Once);
            instanceRepoMock.VerifyNoOtherCalls();
            dataRepositoryMock.VerifyNoOtherCalls();
        }
        
        [Fact]
        public async Task Lock_returns_StatusCode_from_CosmosExpcetion()
        {
            // Arrange
            var instanceGuid = Guid.NewGuid();
            var dataElementId = Guid.NewGuid();
            List<string> expectedPropertiesForPatch = new() { "/locked" };
            (DataLockController testController, Mock<IDataRepository> dataRepositoryMock, Mock<IInstanceRepository> instanceRepoMock) = GetTestController(expectedPropertiesForPatch, dataElementId, true, new RepositoryException("NotFound", System.Net.HttpStatusCode.NotFound));

            // Act
            var result = await testController.Lock(12345, instanceGuid, dataElementId);
            
            // Assert
            Assert.IsType<StatusCodeResult>(result.Result);
            Assert.Equal(404, ((StatusCodeResult)result.Result).StatusCode);
            instanceRepoMock.Verify(i => i.GetOne(instanceGuid, true), Times.Once);
            instanceRepoMock.VerifyNoOtherCalls();
            dataRepositoryMock.Verify(d => d.Update(instanceGuid, dataElementId, It.Is<Dictionary<string, object>>(p => VerifyPropertyListInput(expectedPropertiesForPatch.Count, expectedPropertiesForPatch, p))), Times.Once);
            dataRepositoryMock.VerifyNoOtherCalls();
        }
        
        [Fact]
        public async Task Lock_returns_NotFound_if_Instance_not_found()
        {
            // Arrange
            var instanceGuid = Guid.NewGuid();
            var dataElementId = Guid.NewGuid();
            List<string> expectedPropertiesForPatch = new() { "/locked" };
            (DataLockController testController, Mock<IDataRepository> dataRepositoryMock, Mock<IInstanceRepository> instanceRepoMock) = GetTestController(expectedPropertiesForPatch, dataElementId, true, new RepositoryException("NotFound", System.Net.HttpStatusCode.NotFound), false);

            // Act
            var result = await testController.Lock(12345, instanceGuid, dataElementId);
            
            // Assert
            Assert.IsType<NotFoundObjectResult>(result.Result);
            instanceRepoMock.Verify(i => i.GetOne(instanceGuid, true), Times.Once);
            instanceRepoMock.VerifyNoOtherCalls();
            dataRepositoryMock.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task UnLock_patches_locked_property()
        {
            // Arrange
            var instanceGuid = Guid.NewGuid();
            var dataElementId = Guid.NewGuid();
            List<string> expectedPropertiesForPatch = new() { "/locked" };
            (DataLockController testController, Mock<IDataRepository> dataRepositoryMock, Mock<IInstanceRepository> instanceRepoMock) = GetTestController(expectedPropertiesForPatch, dataElementId, true);

            // Act
            var result = await testController.Unlock(12345, instanceGuid, dataElementId);
            
            // Assert
            Assert.IsType<OkObjectResult>(result.Result);
            instanceRepoMock.Verify(i => i.GetOne(instanceGuid, true), Times.Once);
            instanceRepoMock.VerifyNoOtherCalls();
            dataRepositoryMock.Verify(d => d.Update(instanceGuid, dataElementId, It.Is<Dictionary<string, object>>(p => VerifyPropertyListInput(expectedPropertiesForPatch.Count, expectedPropertiesForPatch, p))), Times.Once);
            dataRepositoryMock.VerifyNoOtherCalls();
        }
        
        [Fact]
        public async Task UnLock_returns_StatusCode_from_CosmosExpcetion()
        {
            // Arrange
            var instanceGuid = Guid.NewGuid();
            var dataElementId = Guid.NewGuid();
            List<string> expectedPropertiesForPatch = new() { "/locked" };
            (DataLockController testController, Mock<IDataRepository> dataRepositoryMock, Mock<IInstanceRepository> instanceRepoMock) = GetTestController(expectedPropertiesForPatch, dataElementId, true, new RepositoryException("NotFound", System.Net.HttpStatusCode.NotFound));

            // Act
            var result = await testController.Unlock(12345, instanceGuid, dataElementId);
            
            // Assert
            Assert.IsType<StatusCodeResult>(result.Result);
            Assert.Equal(404, ((StatusCodeResult)result.Result).StatusCode);
            instanceRepoMock.Verify(i => i.GetOne(instanceGuid, true), Times.Once);
            instanceRepoMock.VerifyNoOtherCalls();
            dataRepositoryMock.Verify(d => d.Update(instanceGuid, dataElementId, It.Is<Dictionary<string, object>>(p => VerifyPropertyListInput(expectedPropertiesForPatch.Count, expectedPropertiesForPatch, p))), Times.Once);
            dataRepositoryMock.VerifyNoOtherCalls();
        }
        
        [Fact]
        public async Task UnLock_returns_Forbidden_if_unauthorized()
        {
            // Arrange
            var instanceGuid = Guid.NewGuid();
            var dataElementId = Guid.NewGuid();
            List<string> expectedPropertiesForPatch = new() { "/locked" };
            (DataLockController testController, Mock<IDataRepository> dataRepositoryMock, Mock<IInstanceRepository> instanceRepoMock) = GetTestController(expectedPropertiesForPatch, dataElementId, false);

            // Act
            var result = await testController.Unlock(12345, instanceGuid, dataElementId);
            
            // Assert
            Assert.IsType<ForbidResult>(result.Result);
            instanceRepoMock.Verify(i => i.GetOne(instanceGuid, true), Times.Once);
            instanceRepoMock.VerifyNoOtherCalls();
            dataRepositoryMock.VerifyNoOtherCalls();
        }
        
        [Fact]
        public async Task UnLock_returns_Forbidden_if_Instance_not_found()
        {
            // Arrange
            var instanceGuid = Guid.NewGuid();
            var dataElementId = Guid.NewGuid();
            List<string> expectedPropertiesForPatch = new() { "/locked" };
            (DataLockController testController, Mock<IDataRepository> dataRepositoryMock, Mock<IInstanceRepository> instanceRepoMock) = GetTestController(expectedPropertiesForPatch, dataElementId, true, new RepositoryException("NotFound", System.Net.HttpStatusCode.NotFound), false);

            // Act
            var result = await testController.Unlock(12345, instanceGuid, dataElementId);
            
            // Assert
            Assert.IsType<ForbidResult>(result.Result);
            instanceRepoMock.Verify(i => i.GetOne(instanceGuid, true), Times.Once);
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

        private (DataLockController TestController, Mock<IDataRepository> DataRepositoryMock, Mock<IInstanceRepository> InstanceRepositoryMock) GetTestController(List<string> expectedPropertiesForPatch, Guid dataGuid, bool authorized, RepositoryException exception = null, bool instanceFound = true, bool dataLocked = false)
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
                            It.Is<Guid>(g => g == dataGuid),
                            It.Is<Dictionary<string, object>>(propertyList => VerifyPropertyListInput(expectedPropertiesForPatch.Count, expectedPropertiesForPatch, propertyList))))
                    .ReturnsAsync(new DataElement());
            }
            else
            {
                dataRepositoryMock
                    .Setup(
                        d => d.Update(
                            It.IsAny<Guid>(),
                            It.Is<Guid>(g => g == dataGuid),
                            It.Is<Dictionary<string, object>>(propertyList => VerifyPropertyListInput(expectedPropertiesForPatch.Count, expectedPropertiesForPatch, propertyList))))
                    .ThrowsAsync(exception);
            }

            authorizationMock
                .Setup(a => a.AuthorizeAnyOfInstanceActions(It.IsAny<Instance>(), It.IsAny<List<string>>()))
                .ReturnsAsync(authorized);
            if (instanceFound)
            {
                instanceRepositoryMock
                    .Setup(ir => ir.GetOne(It.IsAny<Guid>(), true))
                    .ReturnsAsync((Guid instanceGuid, bool dummy) =>
                    {
                        return (new Instance
                        {
                            Id = $"555/{instanceGuid}",
                            InstanceOwner = new()
                            {
                                PartyId = "555"
                            },
                            Process = new()
                            {
                                CurrentTask = new()
                                {
                                    ElementId = "Task_1"
                                }
                            },
                            Data = new()
                            {
                                new()
                                {
                                    Id = dataGuid.ToString(),
                                    Locked = dataLocked
                                }
                            },
                            Org = _org,
                            AppId = _appId
                        }, 0);
                    });
            }
            else
            {
                instanceRepositoryMock
                    .Setup(ir => ir.GetOne(It.IsAny<Guid>(), true))
                    .ReturnsAsync((Guid instanceGuid, bool dummy) => (null, 0));
            }

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
