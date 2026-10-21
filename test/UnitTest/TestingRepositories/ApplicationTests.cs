#nullable disable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Helpers;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Repository;
using Altinn.Platform.Storage.UnitTest.Utils;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest.TestingRepositories;

[Collection("StoragePostgreSQL")]
public class ApplicationTests : IClassFixture<ApplicationFixture>
{
    private const string App1 = "app1";
    private const string App2 = "app2";
    private const string App3 = "app3";
    private const string AppId1 = $"{App1}/app1";
    private const string AppId2 = $"{App2}/app2";
    private const string AppId3 = $"{App3}/app3";
    private readonly ApplicationFixture _applicationFixture;

    private readonly Application _a1 = new()
    {
        Id = AppId1,
        Org = "ttd",
        Title = new() { { "nb", "t1" } },
    };
    private readonly Application _a2 = new()
    {
        Id = AppId2,
        Org = "ttd",
        Title = new() { { "nb", "t2" } },
    };
    private readonly Application _a3 = new()
    {
        Id = AppId3,
        Org = "skd",
        Title = new() { { "nb", "t3" }, { "en", "t3b" } },
    };

    public ApplicationTests(ApplicationFixture applicationFixture)
    {
        _applicationFixture = applicationFixture;

        string sql = "delete from storage.applications";
        _ = PostgresUtil.RunSql(sql).Result;
    }

    public static TheoryData<DateTimeKind, DateTime> ValidToByKind =>
        new()
        {
            { DateTimeKind.Utc, new DateTime(2026, 10, 21, 10, 0, 0, DateTimeKind.Utc) },
            {
                DateTimeKind.Unspecified,
                new DateTime(2026, 10, 21, 10, 0, 0, DateTimeKind.Unspecified)
            },
            {
                DateTimeKind.Local,
                new DateTime(2026, 10, 21, 10, 0, 0, DateTimeKind.Utc).ToLocalTime()
            },
        };

    /// <summary>
    /// ValidTo is normalized to UTC before it is written (see ApplicationsController), so the stored
    /// value must be in UTC form and read back as the same instant regardless of the original kind.
    /// </summary>
    [Theory]
    [MemberData(nameof(ValidToByKind))]
    public async Task Application_ValidTo_IsStoredAndReadBackAsUtc(
        DateTimeKind kind,
        DateTime validTo
    )
    {
        // Arrange
        string app = $"validto-{kind}".ToLowerInvariant();
        Application application = new()
        {
            Id = $"ttd/{app}",
            Org = "ttd",
            Title = new() { { "nb", "t" } },
            ValidTo = DateTimeHelper.ConvertToUniversalTime(validTo),
        };

        // Act
        await _applicationFixture.ApplicationRepo.Create(application);
        string stored = await PostgresUtil.RunQuery<string>(
            $"select application->>'ValidTo' from storage.applications where org = 'ttd' and app = '{app}'"
        );
        Application readBack = await _applicationFixture.ApplicationRepo.FindOne(
            application.Id,
            "ttd"
        );

        // Assert
        Assert.Equal("2026-10-21T10:00:00Z", stored);
        Assert.Equal(DateTimeKind.Utc, readBack.ValidTo.Value.Kind);
        Assert.Equal(
            new DateTime(2026, 10, 21, 10, 0, 0, DateTimeKind.Utc),
            readBack.ValidTo.Value
        );
    }

    /// <summary>
    /// Test create
    /// </summary>
    [Fact]
    public async Task Application_Create_Ok()
    {
        // Arrange

        // Act
        Application a = await _applicationFixture.ApplicationRepo.Create(_a1);

        // Assert
        string sql =
            $"select count(*) from storage.applications where app = '{App1}' and org = 'ttd'";
        int count = await PostgresUtil.RunCountQuery(sql);
        Assert.Equal(1, count);
        Assert.Equal(_a1.Id, a.Id);
    }

    /// <summary>
    /// Test create when an application already exists
    /// </summary>
    [Fact]
    public async Task Application_Upsert_Ok()
    {
        // Arrange
        Application a = await _applicationFixture.ApplicationRepo.Create(_a1);
        a.VersionId = "v1";
        a.CreatedBy = "testUser";
        a.Created = DateTime.Now.AddDays(-10);

        // Act
        await _applicationFixture.ApplicationRepo.Create(a);

        // Assert
        string sql =
            $"select count(*) from storage.applications where app = '{App1}' and org = 'ttd'";
        int count = await PostgresUtil.RunCountQuery(sql);
        Assert.Equal(1, count);
        Assert.Equal(_a1.Id, a.Id);

        sql =
            $"select count(*) from storage.applications where app = '{App1}' and org = 'ttd' and application ->> 'VersionId' = 'v1'";
        count = await PostgresUtil.RunCountQuery(sql);
        Assert.Equal(1, count);

        sql =
            $"select count(*) from storage.applications where app = '{App1}' and org = 'ttd' and application ->> 'VersionId' = '{a.VersionId}' and application ->> 'Created' = '{a.Created}' and application ->> 'CreatedBy' = '{a.CreatedBy}'";
        count = await PostgresUtil.RunCountQuery(sql);
        Assert.Equal(0, count);
    }

    /// <summary>
    /// Test FindOne
    /// </summary>
    [Fact]
    public async Task Application_FindOne_Ok()
    {
        // Arrange
        await _applicationFixture.ApplicationRepo.Create(_a1);

        // Act
        Application a = await _applicationFixture.ApplicationRepo.FindOne(AppId1, "ttd");

        // Assert
        Assert.Equal(AppId1, a.Id);
    }

    /// <summary>
    /// Test FindByOrg
    /// </summary>
    [Fact]
    public async Task Application_FindByOrg_Ok()
    {
        // Arrange
        await _applicationFixture.ApplicationRepo.Create(_a1);
        await _applicationFixture.ApplicationRepo.Create(_a2);
        await _applicationFixture.ApplicationRepo.Create(_a3);

        // Act
        List<Application> apps = await _applicationFixture.ApplicationRepo.FindByOrg("ttd");

        // Assert
        Assert.Equal(2, apps.Count);
    }

    /// <summary>
    /// Test FindAll
    /// </summary>
    [Fact]
    public async Task Application_FindAll_Ok()
    {
        // Arrange
        await _applicationFixture.ApplicationRepo.Create(_a1);
        await _applicationFixture.ApplicationRepo.Create(_a2);
        await _applicationFixture.ApplicationRepo.Create(_a3);

        // Act
        List<Application> apps = await _applicationFixture.ApplicationRepo.FindAll();

        // Assert
        Assert.Equal(3, apps.Count);
    }

    /// <summary>
    /// Test GetAllAppTitles
    /// </summary>
    [Fact]
    public async Task Application_GetAllAppTitles_Ok()
    {
        // Arrange
        await _applicationFixture.ApplicationRepo.Create(_a1);
        await _applicationFixture.ApplicationRepo.Create(_a2);
        await _applicationFixture.ApplicationRepo.Create(_a3);

        // Act
        Dictionary<string, string> titles =
            await _applicationFixture.ApplicationRepo.GetAllAppTitles();

        // Assert
        Assert.Equal(3, titles.Count);
        Assert.True(titles.Count(t => t.Key == _a1.Id && t.Value == "t1") == 1);
        Assert.True(titles.Count(t => t.Key == _a2.Id && t.Value == "t2") == 1);
        Assert.True(
            titles.Count(t => t.Key == _a3.Id && (t.Value == "t3;t3b" || t.Value == "t3b;t3")) == 1
        );
    }

    /// <summary>
    /// Test Delete
    /// </summary>
    [Fact]
    public async Task Application_Delete_Ok()
    {
        // Arrange
        await _applicationFixture.ApplicationRepo.Create(_a1);

        // Act
        bool deleted = await _applicationFixture.ApplicationRepo.Delete(AppId1, "ttd");

        // Assert
        string sql =
            $"select count(*) from storage.applications where app = '{App1}' and org = 'ttd'";
        int count = await PostgresUtil.RunCountQuery(sql);
        Assert.Equal(0, count);
        Assert.True(deleted);
    }

    /// <summary>
    /// Test Update
    /// </summary>
    [Fact]
    public async Task Application_Update_Ok()
    {
        // Arrange
        await _applicationFixture.ApplicationRepo.Create(_a1);
        _a1.VersionId = "v1";

        // Act
        await _applicationFixture.ApplicationRepo.Update(_a1);

        // Assert
        string sql =
            $"select count(*) from storage.applications where application ->> 'VersionId' = 'v1'";
        int count = await PostgresUtil.RunCountQuery(sql);
        Assert.Equal(1, count);
    }
}

public class ApplicationFixture
{
    public IApplicationRepository ApplicationRepo { get; set; }

    public ApplicationFixture()
    {
        var serviceList = ServiceUtil.GetServices(
            new List<Type>() { typeof(IApplicationRepository) }
        );
        ApplicationRepo = (IApplicationRepository)
            serviceList.First(i => i.GetType() == typeof(PgApplicationRepository));
    }
}
