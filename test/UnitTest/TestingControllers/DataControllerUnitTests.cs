using Altinn.Platform.Storage.Configuration;
using Altinn.Platform.Storage.Controllers;
using Altinn.Platform.Storage.Interface.Enums;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Repository;
using Altinn.Platform.Storage.UnitTest.Utils;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

using Moq;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Xunit;

namespace Altinn.Platform.Storage.UnitTest.TestingControllers
{
    public class DataControllerUnitTests
    {
        private static readonly List<string> _forbiddenUpdateProps = new List<string>() { "/created", "/createdBy", "/id", "/instanceGuid", "/blobStoragePath", "/dataType" };
        private readonly Mock<IInstanceRepository> _instanceRepositoryMock = new();
        private readonly Mock<IDataRepository> _dataRepositoryMock = new();
        private readonly Mock<IApplicationRepository> _applicationRepositoryMock = new();
        private readonly Mock<IInstanceEventRepository> _instanceEventRepositoryMock = new();
        private readonly ControllerContext _controllerContext;
        private readonly IOptions<GeneralSettings> _generalSettings;


        public DataControllerUnitTests()
        {
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
                        Org = "ttd",
                        AppId = "ttd/apps-test"
                    };
                });

            _applicationRepositoryMock
                .Setup(ar => ar.FindOne(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(new Application()
                {
                    DataTypes = new List<DataType>() {
                        new DataType {
                            Id = "attachment",
                            AppLogic = new() {
                                AutoDeleteOnProcessEnd = true
                            }
                        }
                    }
                });

            _instanceEventRepositoryMock
                .Setup(ier => ier.InsertInstanceEvent(It.IsAny<InstanceEvent>()));


            Mock<HttpContext> httpContextMock = new();
            httpContextMock
                .Setup(c => c.User).Returns(PrincipalUtil.GetPrincipal(200001, 1337));

            _controllerContext = new ControllerContext()
            {
                HttpContext = httpContextMock.Object
            };

            _generalSettings = Options.Create(new GeneralSettings() { Hostname = "https://altinn.no/" });
        }

        [Fact]
        public async Task Get_VerifyDataRepositoryUpdateInput()
        {
            SetupReadForDataElementRepository();
            _dataRepositoryMock
                .Setup(
                d => d.Update(
                    It.IsAny<Guid>(),
                    It.IsAny<Guid>(),
                    It.Is<Dictionary<string, object>>(propertyList => VerifyPropertyListInput(1, new List<string>() { "/isRead" }, propertyList))));

            Mock<HttpContext> contextMock = new();
            contextMock.Setup(c => c.User).Returns(PrincipalUtil.GetPrincipal(200001, 1337));

            var sut = new DataController(
                _dataRepositoryMock.Object,
                _instanceRepositoryMock.Object,
                _applicationRepositoryMock.Object,
                _instanceEventRepositoryMock.Object,
                null,
                _generalSettings)
            {
                ControllerContext = _controllerContext
            };

            // Act
            await sut.Get(12345, Guid.NewGuid(), Guid.NewGuid());

            // Assert
            _dataRepositoryMock.Verify(d => d.Update(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object>>()), Times.Once);
        }

        [Fact]
        public async Task Delete_VerifyDataRepositoryUpdateInput()
        {
            SetupReadForDataElementRepository();
            _dataRepositoryMock
                .Setup(
                d => d.Update(
                    It.IsAny<Guid>(),
                    It.IsAny<Guid>(),
                    It.Is<Dictionary<string, object>>(propertyList => VerifyPropertyListInput(1, new List<string>() { "/isRead" }, propertyList))));

            var sut = new DataController(
                _dataRepositoryMock.Object,
                _instanceRepositoryMock.Object,
                _applicationRepositoryMock.Object,
                _instanceEventRepositoryMock.Object,
                null,
                _generalSettings)
            {
                ControllerContext = _controllerContext
            };

            // Act
            await sut.Delete(12345, Guid.NewGuid(), Guid.NewGuid(), true);

            // Assert
            _dataRepositoryMock.Verify(d => d.Update(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object>>()), Times.Once);
        }

        [Fact]
        public async Task SetFileScanStatus_VerifyDataRepositoryUpdateInput()
        {
            _dataRepositoryMock
                .Setup(
                d => d.Update(
                    It.IsAny<Guid>(),
                    It.IsAny<Guid>(),
                    It.Is<Dictionary<string, object>>(propertyList => VerifyPropertyListInput(1, new List<string>() { "/fileScanResult" }, propertyList))));

            var sut = new DataController(
                _dataRepositoryMock.Object,
                _instanceRepositoryMock.Object,
                _applicationRepositoryMock.Object,
                _instanceEventRepositoryMock.Object,
                null,
                _generalSettings);

            // Act
            await sut.SetFileScanStatus(Guid.NewGuid(), Guid.NewGuid(), new FileScanStatus { FileScanResult = FileScanResult.Infected });

            // Assert
            _dataRepositoryMock.Verify(d => d.Update(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object>>()), Times.Once);
        }

        private void SetupReadForDataElementRepository()
        {
            _dataRepositoryMock
             .Setup(d => d.Read(It.IsAny<Guid>(), It.IsAny<Guid>()))
             .ReturnsAsync((Guid instanceGuid, Guid dataElementId) =>
             {
                 return new DataElement
                 {
                     Id = dataElementId.ToString(),
                     InstanceGuid = instanceGuid.ToString(),
                     DataType = "attachment",
                     IsRead = false,
                     ContentType = "application/octet-stream",
                     BlobStoragePath = $"ttd/apps-test/{instanceGuid}/data/{dataElementId}"
                 };
             });

            _dataRepositoryMock
                .Setup(d => d.ReadDataFromStorage(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(new MemoryStream(Encoding.UTF8.GetBytes("whatever")));
        }

        private static bool VerifyPropertyListInput(int expectedPropCount, List<string> expectedProperties, Dictionary<string, object> propertyList)
        {
            if (propertyList.Count != expectedPropCount)
            {
                // property list should only contain one element when called from this controller method
                return false;
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
                return false;
            }

            return true;
        }
    }
}
