using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Altinn.Platform.Storage.Configuration;
using Altinn.Platform.Storage.Controllers;
using Altinn.Platform.Storage.Interface.Enums;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Repository;
using Altinn.Platform.Storage.Services;
using Altinn.Platform.Storage.UnitTest.Utils;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

using Moq;

using Xunit;

namespace Altinn.Platform.Storage.UnitTest.TestingControllers
{
    public class DataControllerUnitTests
    {
        private static List<string> _forbiddenUpdateProps = new List<string>() { "/created", "/createdBy", "/id", "/instanceGuid", "/blobStoragePath", "/dataType" };
        private int _instanceOwnerPartyId = 1337;
        private string _org = "ttd";
        private string _appId = "ttd/apps-test";
        private string _dataType = "attachment";

        [Fact]
        public async Task Get_VerifyDataRepositoryUpdateInput()
        {
            // Arrange
            List<string> expectedPropertiesForPatch = new() { "/isRead" };
            (DataController TestController, Mock<IDataRepository> DataRepositoryMock) = GetTestController(expectedPropertiesForPatch);

            // Act
            await TestController.Get(12345, Guid.NewGuid(), Guid.NewGuid());

            // Assert
            DataRepositoryMock.Verify(d => d.Update(It.IsAny<Guid>(), It.IsAny<Guid>(), It.Is<Dictionary<string, object>>(p => VerifyPropertyListInput(expectedPropertiesForPatch.Count, expectedPropertiesForPatch, p))), Times.Once);
        }

        [Fact]
        public async Task OverwriteData_VerifyDataRepositoryUpdateInput()
        {
            // Arrange
            List<string> expectedPropertiesForPatch = new() { "/contentType", "/filename", "/lastChangedBy", "/lastChanged", "/refs", "/size", "/fileScanResult" };

            (DataController TestController, Mock<IDataRepository> DataRepositoryMock) = GetTestController(expectedPropertiesForPatch, true);

            // Act
            await TestController.OverwriteData(_instanceOwnerPartyId, Guid.NewGuid(), Guid.NewGuid());

            // Assert
            DataRepositoryMock.Verify(d => d.Update(It.IsAny<Guid>(), It.IsAny<Guid>(), It.Is<Dictionary<string, object>>(p => VerifyPropertyListInput(expectedPropertiesForPatch.Count, expectedPropertiesForPatch, p))), Times.Once);
        }

        [Fact]
        public async Task Update_VerifyDataRepositoryUpdateInput()
        {
            // Arrange
            List<string> expectedPropertiesForPatch = new() { "/locked", "/refs", "/tags", "/deleteStatus", "/lastChanged", "/lastChangedBy" };

            (DataController TestController, Mock<IDataRepository> DataRepositoryMock) = GetTestController(expectedPropertiesForPatch, true);

            var instanceGuid = Guid.NewGuid();
            var dataElementId = Guid.NewGuid();
            var input = new DataElement { Id = $"{dataElementId}", InstanceGuid = $"{instanceGuid}" };

            // Act
            await TestController.Update(_instanceOwnerPartyId, instanceGuid, dataElementId, input);

            // Assert
            DataRepositoryMock.Verify(d => d.Update(It.IsAny<Guid>(), It.IsAny<Guid>(), It.Is<Dictionary<string, object>>(p => VerifyPropertyListInput(expectedPropertiesForPatch.Count, expectedPropertiesForPatch, p))), Times.Once);
        }


        [Fact]
        public async Task Delete_VerifyDataRepositoryUpdateInput()
        {
            // Arrange
            List<string> expectedPropertiesForPatch = new() { "/deleteStatus" };
            (DataController TestController, Mock<IDataRepository> DataRepositoryMock) = GetTestController(expectedPropertiesForPatch);

            // Act
            await TestController.Delete(12345, Guid.NewGuid(), Guid.NewGuid(), true);

            // Assert
            DataRepositoryMock.Verify(d => d.Update(It.IsAny<Guid>(), It.IsAny<Guid>(), It.Is<Dictionary<string, object>>(p => VerifyPropertyListInput(expectedPropertiesForPatch.Count, expectedPropertiesForPatch, p))), Times.Once);
        }

        [Fact]
        public async Task SetFileScanStatus_VerifyDataRepositoryUpdateInput()
        {
            // Arrange
            List<string> expectedPropertiesForPatch = new() { "/fileScanResult" };
            (DataController TestController, Mock<IDataRepository> DataRepositoryMock) = GetTestController(expectedPropertiesForPatch);

            // Act
            await TestController.SetFileScanStatus(Guid.NewGuid(), Guid.NewGuid(), new FileScanStatus { FileScanResult = FileScanResult.Infected });

            // Assert
            DataRepositoryMock.Verify(d => d.Update(It.IsAny<Guid>(), It.IsAny<Guid>(), It.Is<Dictionary<string, object>>(p => VerifyPropertyListInput(expectedPropertiesForPatch.Count, expectedPropertiesForPatch, p))), Times.Once);
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
        
        private (DataController TestController, Mock<IDataRepository> DataRepositoryMock) GetTestController(List<string> expectedPropertiesForPatch, bool includeRequestBody = false)
        {
            Mock<IDataRepository> _dataRepositoryMock = new();
            Mock<IInstanceRepository> _instanceRepositoryMock = new();
            Mock<IApplicationRepository> _applicationRepositoryMock = new();
            Mock<IInstanceEventRepository> _instanceEventRepositoryMock = new();
            Mock<IDataService> _dataServiceMock = new();

            _dataRepositoryMock
                .Setup(
                d => d.Update(
                    It.IsAny<Guid>(),
                    It.IsAny<Guid>(),
                    It.Is<Dictionary<string, object>>(propertyList => VerifyPropertyListInput(expectedPropertiesForPatch.Count, expectedPropertiesForPatch, propertyList))))
                  .ReturnsAsync(new DataElement());


            _dataRepositoryMock
            .Setup(d => d.Read(It.IsAny<Guid>(), It.IsAny<Guid>()))
            .ReturnsAsync((Guid instanceGuid, Guid dataElementId) =>
            {
                return new DataElement
                {
                    Id = dataElementId.ToString(),
                    InstanceGuid = instanceGuid.ToString(),
                    DataType = _dataType,
                    IsRead = false,
                    ContentType = "application/octet-stream",
                    BlobStoragePath = $"ttd/apps-test/{instanceGuid}/data/{dataElementId}"
                };
            });

            _dataRepositoryMock
                .Setup(d => d.ReadDataFromStorage(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(new MemoryStream(Encoding.UTF8.GetBytes("whatever")));

            _dataRepositoryMock
               .Setup(d => d.WriteDataToStorage(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>()))
               .ReturnsAsync((123145864564, DateTimeOffset.Now));

            _instanceRepositoryMock
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
                       Org = _org,
                       AppId = _appId
                   };
               });

            _applicationRepositoryMock
                .Setup(ar => ar.FindOne(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(new Application()
                {
                    DataTypes = new List<DataType>() {
                        new DataType {
                            Id = _dataType,
                            AppLogic = new() {
                                AutoDeleteOnProcessEnd = true
                            }
                        }
                    }
                });

            _instanceEventRepositoryMock
                .Setup(ier => ier.InsertInstanceEvent(It.IsAny<InstanceEvent>()));

            _dataServiceMock
                .Setup(d => d.StartFileScan(It.IsAny<Instance>(), It.IsAny<DataType>(), It.IsAny<DataElement>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()));

            Mock<HttpContext> httpContextMock = new();
            httpContextMock
                .Setup(c => c.User).Returns(PrincipalUtil.GetPrincipal(200001, 1337));

            if (includeRequestBody)
            {
                Mock<HttpRequest> requestMock = new();

                requestMock.Setup(r => r.Headers).Returns(new HeaderDictionary());
                requestMock.Setup(r => r.ContentType).Returns("application/pdf");
                requestMock.Setup(r => r.Headers).Returns(new HeaderDictionary() { { "Content-Disposition", new StringValues("attachment; filename=\"filename.jpg\"; size=12348") } });

                requestMock.Setup(r => r.Body).Returns(new MemoryStream(Encoding.UTF8.GetBytes("whatever")));


                httpContextMock
                .Setup(c => c.Request).Returns(requestMock.Object);
            }

            ControllerContext _controllerContext = new ControllerContext()
            {
                HttpContext = httpContextMock.Object
            };



            IOptions<GeneralSettings> _generalSettings = Options.Create(new GeneralSettings() { Hostname = "https://altinn.no/" });


            var sut = new DataController(
                     _dataRepositoryMock.Object,
                     _instanceRepositoryMock.Object,
                     _applicationRepositoryMock.Object,
                     _instanceEventRepositoryMock.Object,
                     _dataServiceMock.Object,
                     _generalSettings)
            {
                ControllerContext = _controllerContext
            };

            return (sut, _dataRepositoryMock);
        }
    }
}
