#nullable disable

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Configuration;
using Altinn.Platform.Storage.Controllers;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Models;
using Altinn.Platform.Storage.Repository;
using Altinn.Platform.Storage.UnitTest.Extensions;
using Altinn.Platform.Storage.UnitTest.TestingRepositories;
using Altinn.Platform.Storage.UnitTest.Utils;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest.TestingControllers;

[Collection("StoragePostgreSQL")]
public class MigrationControllerTests : IClassFixture<InstanceFixture>
{
    private const int A2ArchiveReference = 4242;
    private readonly InstanceFixture _instanceFixture;

    public MigrationControllerTests(InstanceFixture instanceFixture)
    {
        _instanceFixture = instanceFixture;
        _ = PostgresUtil
            .RunSql(
                "delete from storage.dataelementblobversions; delete from storage.instances; delete from storage.dataelements;"
            )
            .Result;
    }

    [Theory]
    [InlineData(
        "legacy-prefix/045ea5db-6dd4-4476-b774-bdb2a09da7ea",
        "045ea5db-6dd4-4476-b774-bdb2a09da7ea"
    )]
    [InlineData(
        "9999/145ea5db-6dd4-4476-b774-bdb2a09da7ea/legacy-suffix",
        "145ea5db-6dd4-4476-b774-bdb2a09da7ea"
    )]
    public async Task CreateInstance_LegacyCompositeId_UsesHistoricalStorageTranslation(
        string incomingId,
        string expectedStorageId
    )
    {
        Instance incoming = TestData.Instance_1_1.Clone();
        incoming.Id = incomingId;
        incoming.DataValues = new Dictionary<string, string>
        {
            ["A2ArchRef"] = A2ArchiveReference.ToString(),
        };
        Mock<IA2Repository> a2Repository = new();
        a2Repository
            .Setup(repository => repository.GetA2MigrationInstanceId(A2ArchiveReference))
            .ReturnsAsync((string)null);
        a2Repository
            .Setup(repository => repository.CreateA2MigrationState(A2ArchiveReference))
            .Returns(Task.CompletedTask);
        a2Repository
            .Setup(repository =>
                repository.UpdateStartA2MigrationState(A2ArchiveReference, expectedStorageId)
            )
            .Returns(Task.CompletedTask);
        using MemoryCache memoryCache = new(new MemoryCacheOptions());
        MigrationController controller = CreateController(a2Repository.Object, memoryCache);

        ActionResult<Instance> result = await controller.CreateInstance(
            incoming,
            CancellationToken.None
        );

        CreatedResult created = Assert.IsType<CreatedResult>(result.Result);
        Instance response = Assert.IsType<Instance>(created.Value);
        Assert.Equal($"{incoming.InstanceOwner.PartyId}/{expectedStorageId}", response.Id);
        InstanceInternal persisted = await _instanceFixture.InstanceRepo.GetOne(
            Guid.Parse(expectedStorageId),
            false,
            CancellationToken.None
        );
        Assert.Equal(expectedStorageId, persisted.Id);
        a2Repository.VerifyAll();
    }

    private MigrationController CreateController(
        IA2Repository a2Repository,
        IMemoryCache memoryCache
    )
    {
        GeneralSettings settings = new() { PdfGeneratorEndpoint = "http://localhost/" };
        return new MigrationController(
            _instanceFixture.InstanceRepo,
            Mock.Of<IInstanceEventRepository>(),
            Mock.Of<IDataRepository>(),
            Mock.Of<IBlobRepository>(),
            Mock.Of<IApplicationRepository>(),
            a2Repository,
            Mock.Of<ITextRepository>(),
            NullLogger<MigrationController>.Instance,
            Options.Create(settings),
            Options.Create(new AzureStorageConfiguration()),
            Options.Create(settings),
            new HttpClient(),
            memoryCache
        );
    }
}
