using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Clients;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Models;
using Altinn.Platform.Storage.Repository;
using Altinn.Platform.Storage.Services;

using Moq;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest.TestingServices
{
    public class DataServiceTests
    {
        [Fact]
        public async Task PerformFileScanTest_EnableFileScanIsFalse_ScanIsNotqueued()
        {
            // Arrange
            Mock<IFileScanQueueClient> fileScanMock = new Mock<IFileScanQueueClient>();
            Mock<IDataRepository> dataRepositoryMock = new Mock<IDataRepository>();

            DataService target = new DataService(fileScanMock.Object, dataRepositoryMock.Object);

            Instance instance = new Instance();
            DataType dataType = new DataType { EnableFileScan = false };
            DataElement dataElement = new DataElement { };
            DateTimeOffset blobTimestamp = DateTimeOffset.UtcNow;

            // Act
            await target.StartFileScan(instance, dataType, dataElement, blobTimestamp, CancellationToken.None);

            // Assert
            fileScanMock.Verify(f => f.EnqueueFileScan(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never());
        }

        [Fact]
        public async Task PerformFileScanTest_EnableFileScanIsTrue_ScanIsQueued()
        {
            // Arrange
            Mock<IFileScanQueueClient> fileScanMock = new Mock<IFileScanQueueClient>();
            Mock<IDataRepository> dataRepositoryMock = new Mock<IDataRepository>();
            
            DataService target = new DataService(fileScanMock.Object, dataRepositoryMock.Object);

            Instance instance = new Instance { Id = "343243/guid" };
            DataType dataType = new DataType { EnableFileScan = true };
            DataElement dataElement = new DataElement { };
            DateTimeOffset blobTimestamp = DateTimeOffset.UtcNow;

            // Act
            await target.StartFileScan(instance, dataType, dataElement, blobTimestamp, CancellationToken.None);

            // Assert
            fileScanMock.Verify(
                f => f.EnqueueFileScan(
                    It.Is<string>(c => c.Contains($"\"instanceId\":\"343243/guid\"")), It.IsAny<CancellationToken>()), 
                Times.Once());
        }

        [Fact]
        public async Task GenerateSha256Hash_Success()
        {
            // Arrange
            Mock<IFileScanQueueClient> fileScanQueueClientMock = new Mock<IFileScanQueueClient>();
            Mock<IDataRepository> dataRepositoryMock = new Mock<IDataRepository>();
            
            Guid id = Guid.NewGuid();
            string blobStoragePath = "/ttd/some-app";
            DataElement dataElement = new DataElement
            {
                Id = id.ToString(),
                BlobStoragePath = blobStoragePath
            };
            
            dataRepositoryMock.Setup(drm => drm.Read(It.IsAny<Guid>(), It.IsAny<Guid>())).ReturnsAsync(dataElement);
            dataRepositoryMock.Setup(
                drm => drm.ReadDataFromStorage(It.IsAny<string>(), blobStoragePath))
                .ReturnsAsync(new MemoryStream(Encoding.UTF8.GetBytes("whatever")));

            DataService dataService = new DataService(fileScanQueueClientMock.Object, dataRepositoryMock.Object);
            
            // Act
            (string fileHash, ServiceError serviceError) = await dataService.GenerateSha256Hash("ttd", Guid.NewGuid(), id);

            // Assert
            Assert.NotNull(fileHash);
            Assert.Null(serviceError);
            dataRepositoryMock.VerifyAll();
        }

        [Fact]
        public async Task GenerateSha256Hash_Failed_DataElementNotExists()
        {
            // Arrange
            Mock<IFileScanQueueClient> fileScanQueueClientMock = new Mock<IFileScanQueueClient>();
            Mock<IDataRepository> dataRepositoryMock = new Mock<IDataRepository>();

            DataService dataService = new DataService(fileScanQueueClientMock.Object, dataRepositoryMock.Object);

            // Act
            (string fileHash, ServiceError serviceError) = await dataService.GenerateSha256Hash("ttd", Guid.NewGuid(), Guid.NewGuid());

            // Assert
            Assert.Null(fileHash);
            Assert.Equal(404, serviceError.ErrorCode);
        }

        [Fact]
        public async Task GenerateSha256Hash_Failed_FiletNotExists()
        {
            // Arrange
            Mock<IFileScanQueueClient> fileScanQueueClientMock = new Mock<IFileScanQueueClient>();
            Mock<IDataRepository> dataRepositoryMock = new Mock<IDataRepository>();

            DataElement dataElement = new DataElement
            {
                Id = Guid.NewGuid().ToString(),
                BlobStoragePath = "/ttd/some-app"
            };
            
            dataRepositoryMock.Setup(drm => drm.Read(It.IsAny<Guid>(), It.IsAny<Guid>())).ReturnsAsync(dataElement);

            DataService dataService = new DataService(fileScanQueueClientMock.Object, dataRepositoryMock.Object);

            // Act
            (string fileHash, ServiceError serviceError) = await dataService.GenerateSha256Hash("ttd", Guid.NewGuid(), Guid.NewGuid());

            // Assert
            Assert.Null(fileHash);
            Assert.Equal(404, serviceError.ErrorCode);
        }

        [Fact]
        public async Task UploadDataAndCreateDataElement_Success()
        {   
            // Arrange
            Mock<IFileScanQueueClient> fileScanQueueClientMock = new Mock<IFileScanQueueClient>();
            Mock<IDataRepository> dataRepositoryMock = new Mock<IDataRepository>();
            dataRepositoryMock.Setup(
                drm => drm.WriteDataToStorage(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>()))
                .ReturnsAsync((666, DateTimeOffset.Now));

            DataElement dataElement = new DataElement
            {
                Id = Guid.NewGuid().ToString(),
                BlobStoragePath = "/ttd/some-app"
            };

            DataService dataService = new DataService(fileScanQueueClientMock.Object, dataRepositoryMock.Object);

            // Act
            await dataService.UploadDataAndCreateDataElement("ttd", new MemoryStream(Encoding.UTF8.GetBytes("whatever")), dataElement);

            // Assert
            dataRepositoryMock.VerifyAll();
        }
    }
}
