using System;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Clients;
using Altinn.Platform.Storage.Interface.Models;
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
    }
}
