using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
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
        private static readonly JsonSerializerOptions _options = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        private readonly int _instanceOwnerPartyId = 1337;
        private readonly string _org = "ttd";
        private readonly string _appId = "ttd/apps-test";
        private readonly string _dataType = "attachment";

        [Fact]
        public async Task Get_VerifyDataRepositoryUpdateInput()
        {
            // Arrange
            List<string> expectedPropertiesForPatch = new() { "/isRead" };
            (DataController testController, Mock<IDataRepository> dataRepositoryMock) = GetTestController(expectedPropertiesForPatch);

            // Act
            await testController.Get(12345, Guid.NewGuid(), Guid.NewGuid());

            // Assert
            dataRepositoryMock.Verify(d => d.Update(It.IsAny<Guid>(), It.IsAny<Guid>(), It.Is<Dictionary<string, object>>(p => VerifyPropertyListInput(expectedPropertiesForPatch.Count, expectedPropertiesForPatch, p))), Times.Once);
        }

        [Fact]
        public async Task OverwriteData_VerifyDataRepositoryUpdateInput()
        {
            // Arrange
            List<string> expectedPropertiesForPatch = new() { "/contentType", "/filename", "/lastChangedBy", "/lastChanged", "/refs", "/size", "/fileScanResult", "/references" };

            (DataController testController, Mock<IDataRepository> dataRepositoryMock) = GetTestController(expectedPropertiesForPatch, true);

            // Act
            await testController.OverwriteData(_instanceOwnerPartyId, Guid.NewGuid(), Guid.NewGuid());

            // Assert
            dataRepositoryMock.Verify(d => d.Update(It.IsAny<Guid>(), It.IsAny<Guid>(), It.Is<Dictionary<string, object>>(p => VerifyPropertyListInput(expectedPropertiesForPatch.Count, expectedPropertiesForPatch, p))), Times.Once);
        }

        [Fact]
        public async Task Update_VerifyDataRepositoryUpdateInput()
        {
            // Arrange
            List<string> expectedPropertiesForPatch = new() { "/locked", "/refs", "/references", "/tags", "/userDefinedMetadata", "/metadata", "/deleteStatus", "/lastChanged", "/lastChangedBy" };

            (DataController testController, Mock<IDataRepository> dataRepositoryMock) = GetTestController(expectedPropertiesForPatch, true);

            var instanceGuid = Guid.NewGuid();
            var dataElementId = Guid.NewGuid();
            var input = new DataElement { Id = $"{dataElementId}", InstanceGuid = $"{instanceGuid}" };

            // Act
            await testController.Update(_instanceOwnerPartyId, instanceGuid, dataElementId, input);

            // Assert
            dataRepositoryMock.Verify(d => d.Update(It.IsAny<Guid>(), It.IsAny<Guid>(), It.Is<Dictionary<string, object>>(p => VerifyPropertyListInput(expectedPropertiesForPatch.Count, expectedPropertiesForPatch, p))), Times.Once);
        }

        [Fact]
        public async Task Delete_VerifyDataRepositoryUpdateInput()
        {
            // Arrange
            List<string> expectedPropertiesForPatch = new() { "/deleteStatus", "/lastChanged", "/lastChangedBy" };
            (DataController testController, Mock<IDataRepository> dataRepositoryMock) = GetTestController(expectedPropertiesForPatch);

            // Act
            await testController.Delete(12345, Guid.NewGuid(), Guid.NewGuid(), true);

            // Assert
            dataRepositoryMock.Verify(d => d.Update(It.IsAny<Guid>(), It.IsAny<Guid>(), It.Is<Dictionary<string, object>>(p => VerifyPropertyListInput(expectedPropertiesForPatch.Count, expectedPropertiesForPatch, p))), Times.Once);
        }

        [Fact]
        public async Task SetFileScanStatus_VerifyDataRepositoryUpdateInput()
        {
            // Arrange
            List<string> expectedPropertiesForPatch = new() { "/fileScanResult" };
            (DataController testController, Mock<IDataRepository> dataRepositoryMock) = GetTestController(expectedPropertiesForPatch);

            // Act
            await testController.SetFileScanStatus(Guid.NewGuid(), Guid.NewGuid(), new FileScanStatus { FileScanResult = FileScanResult.Infected });

            // Assert
            dataRepositoryMock.Verify(d => d.Update(It.IsAny<Guid>(), It.IsAny<Guid>(), It.Is<Dictionary<string, object>>(p => VerifyPropertyListInput(expectedPropertiesForPatch.Count, expectedPropertiesForPatch, p))), Times.Once);
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
            Mock<IDataRepository> dataRepositoryMock = new();
            Mock<IBlobRepository> blobRepositoryMock = new();
            Mock<IInstanceRepository> instanceRepositoryMock = new();
            Mock<IApplicationRepository> applicationRepositoryMock = new();
            Mock<IInstanceEventService> instanceEventServiceMock = new();
            Mock<IDataService> dataServiceMock = new();

            dataRepositoryMock
                .Setup(
                d => d.Update(
                    It.IsAny<Guid>(),
                    It.IsAny<Guid>(),
                    It.Is<Dictionary<string, object>>(propertyList => VerifyPropertyListInput(expectedPropertiesForPatch.Count, expectedPropertiesForPatch, propertyList))))
                  .ReturnsAsync(new DataElement());

            dataRepositoryMock
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

            blobRepositoryMock
                .Setup(d => d.ReadBlob(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(new MemoryStream(Encoding.UTF8.GetBytes("whatever")));

            blobRepositoryMock
               .Setup(d => d.WriteBlob(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>()))
               .ReturnsAsync((123145864564, DateTimeOffset.Now));

            instanceRepositoryMock
               .Setup(ir => ir.GetOne(It.IsAny<Guid>(), It.IsAny<bool>()))
            .ReturnsAsync((Guid instanceGuid, bool includeDataElements) =>
            {
                return (new Instance()
                {
                    Id = $"555/{instanceGuid}",
                    InstanceOwner = new()
                    {
                        PartyId = "555"
                    },
                    Org = _org,
                    AppId = _appId,
                    Data = includeDataElements ? GetDataElements(instanceGuid) : null
                }, 0);
            });

            applicationRepositoryMock
                .Setup(ar => ar.FindOne(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(new Application()
                {
                    DataTypes = new List<DataType>()
                    {
                        new DataType
                        {
                            Id = _dataType,
                            AppLogic = new()
                            {
                                AutoDeleteOnProcessEnd = true
                            }
                        }
                    }
                });

            instanceEventServiceMock
                .Setup(ier => ier.DispatchEvent(It.IsAny<InstanceEventType>(), It.IsAny<Instance>(), It.IsAny<DataElement>()));

            dataServiceMock
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

            ControllerContext controllerContext = new ControllerContext()
            {
                HttpContext = httpContextMock.Object
            };

            IOptions<GeneralSettings> generalSettings = Options.Create(new GeneralSettings() { Hostname = "https://altinn.no/" });

            var sut = new DataController(
                     dataRepositoryMock.Object,
                     blobRepositoryMock.Object,
                     instanceRepositoryMock.Object,
                     applicationRepositoryMock.Object,
                     dataServiceMock.Object,
                     instanceEventServiceMock.Object,
                     generalSettings,
                     null)
            {
                ControllerContext = controllerContext
            };

            return (sut, dataRepositoryMock);
        }

        private static List<DataElement> GetDataElements(Guid instanceGuid)
        {
            List<DataElement> dataElements = new List<DataElement>();
            string dataElementsPath = GetDataElementsPath();

            string[] dataElementPaths = Directory.GetFiles(dataElementsPath);
            foreach (string elementPath in dataElementPaths)
            {
                string content = File.ReadAllText(elementPath);
                DataElement dataElement = JsonSerializer.Deserialize<DataElement>(content, _options);
                if (dataElement.InstanceGuid.Contains(instanceGuid.ToString()))
                {
                    dataElements.Add(dataElement);
                }
            }

            return dataElements;
        }

        private static string GetDataElementsPath()
        {
            string unitTestFolder = Path.GetDirectoryName(new Uri(typeof(DataControllerUnitTests).Assembly.Location).LocalPath);
            return Path.Combine(unitTestFolder, "..", "..", "..", "data", "postgresdata", "dataelements");
        }
    }
}
