using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Repository;
using Altinn.Platform.Storage.UnitTest.Utils;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest.TestingRepositories;

[Collection("StoragePostgreSQL")]
public class TextTests : IClassFixture<TextFixture>
{
    private const string App1 = "app1";
    private const string App2 = "app2";
    private const string AppId1 = $"{App1}/app1";
    private const string AppId2 = $"{App2}/app2";

    private readonly Application _a1 = new() { Id = AppId1, Org = "ttd", Title = new() { { "nb", "t1" } } };
    private readonly Application _a2 = new() { Id = AppId2, Org = "ttd", Title = new() { { "nb", "t2" } } };

    private readonly TextResource _tr1 = new() { Org = "ttd", Language = "nb", Resources = new() { new() { Id = "i1", Value = "v1" }, new() { Id = "i2", Value = "v2" } } };
    private readonly TextResource _tr2 = new() { Org = "ttd", Language = "nn", Resources = new() { new() { Id = "i3", Value = "v3" }, new() { Id = "i4", Value = "v4" } } };
    private readonly TextResource _tr3 = new() { Org = "ttd", Language = "nb", Resources = new() { new() { Id = "i5", Value = "v5" }, new() { Id = "i6", Value = "v6" } } };

    private readonly TextFixture _textFixture;

    public TextTests(TextFixture textFixture)
    {
        _textFixture = textFixture;

        string sql = "delete from storage.texts; delete from storage.applications";
        _ = PostgresUtil.RunSql(sql).Result;
        _ = _textFixture.ApplicationRepo.Create(_a1).Result;
    }

    /// <summary>
    /// Test create
    /// </summary>
    [Fact]
    public async Task Text_Create_Ok()
    {
        // Arrange

        // Act
        await _textFixture.TextRepo.Create("ttd", "app1", _tr1);

        // Assert
        string sql = $"select count(*) from storage.texts where org = 'ttd' and app = 'app1';";
        int count = await PostgresUtil.RunCountQuery(sql);
        Assert.Equal(1, count);
    }

    /// <summary>
    /// Test create when a text resource already exists
    /// </summary>
    [Fact]
    public async Task Text_Upsert_Ok()
    {
        // Arrange
        TextResource text = await _textFixture.TextRepo.Create("ttd", "app1", _tr1);
        text.Resources.RemoveAt(0);

        // Act
        await _textFixture.TextRepo.Create("ttd", "app1", text);

        // Assert
        string sql = $"select count(*) from storage.texts where org = 'ttd' and app = 'app1';";
        int count = await PostgresUtil.RunCountQuery(sql);
        Assert.Equal(1, count);
        text = await PostgresUtil.RunQuery<TextResource>($"select textresource from storage.texts where org = 'ttd' and app = 'app1';");
        Assert.Single(text.Resources);
    }

    /// <summary>
    /// Test get
    /// </summary>
    [Fact]
    public async Task Text_Get_Applist_Ok()
    {
        // Arrange
        await _textFixture.ApplicationRepo.Create(_a2);
        await _textFixture.TextRepo.Create("ttd", "app1", _tr1);
        await _textFixture.TextRepo.Create("ttd", "app1", _tr2);
        await _textFixture.TextRepo.Create("ttd", "app2", _tr3);

        // Act
        var t1 = await _textFixture.TextRepo.Get(new() { "ttd/app1", "ttd/app2" }, "nb");
        var t2 = await _textFixture.TextRepo.Get(new() { "ttd/app1" }, "nb");
        var t3 = await _textFixture.TextRepo.Get(new() { "ttd/app1" }, "en");

        // Assert
        Assert.Equal(2, t1.Count);
        Assert.Single(t2);
        Assert.Empty(t3);
    }

    /// <summary>
    /// Test get
    /// </summary>
    [Fact]
    public async Task Text_Get_App_Ok()
    {
        // Arrange
        await _textFixture.ApplicationRepo.Create(_a2);
        await _textFixture.TextRepo.Create("ttd", "app1", _tr1);
        await _textFixture.TextRepo.Create("ttd", "app1", _tr2);
        await _textFixture.TextRepo.Create("ttd", "app2", _tr1);

        // Act
        var t1 = await _textFixture.TextRepo.Get("ttd", "app1", "nb");
        var t2 = await _textFixture.TextRepo.Get("ttd", "app2", "en");

        // Assert
        Assert.NotNull(t1);
        Assert.Null(t2);
    }

    /// <summary>
    /// Test update
    /// </summary>
    [Fact]
    public async Task Text_Update_Ok()
    {
        // Arrange
        await _textFixture.TextRepo.Create("ttd", "app1", _tr1);
        _tr1.Resources.Add(new() { Id = "i99", Value = "v99" });

        // Act
        var t1 = await _textFixture.TextRepo.Update("ttd", "app1", _tr1);

        // Assert
        string sql = $"select count(*) from storage.texts;";
        int count = await PostgresUtil.RunCountQuery(sql);
        Assert.Equal(1, count);
        Assert.Contains(t1.Resources, r => r.Id == "i99");
    }

    /// <summary>
    /// Test delete
    /// </summary>
    [Fact]
    public async Task Text_Delete_Ok()
    {
        // Arrange
        await _textFixture.TextRepo.Create("ttd", "app1", _tr1);

        // Act
        bool deleteOk = await _textFixture.TextRepo.Delete("ttd", "app1", "nb");
        bool deleteFailed = await _textFixture.TextRepo.Delete("ttd", "app1", "en");

        // Assert
        string sql = $"select count(*) from storage.texts;";
        int count = await PostgresUtil.RunCountQuery(sql);
        Assert.True(deleteOk);
        Assert.False(deleteFailed);
        Assert.Equal(0, count);
    }
}

public class TextFixture
{
    public ITextRepository TextRepo { get; set; }

    public IApplicationRepository ApplicationRepo { get; set; }

    public TextFixture()
    {
        var serviceList = ServiceUtil.GetServices(new List<Type>() { typeof(ITextRepository), typeof(IApplicationRepository) });
        TextRepo = (ITextRepository)serviceList.First(i => i.GetType() == typeof(PgTextRepository));
        ApplicationRepo = (IApplicationRepository)serviceList.First(i => i.GetType() == typeof(PgApplicationRepository));
    }
}
