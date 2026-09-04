using System.Threading.Tasks;
using Altinn.Platform.Storage.OpenApi;
using Altinn.Platform.Storage.UnitTest.Fixture;
using Argon;
using Microsoft.OpenApi;
using VerifyTests;
using VerifyXunit;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest.OpenApi;

public class OpenApiTests(TestApplicationFactory<Program> factory)
    : IClassFixture<TestApplicationFactory<Program>>
{
    [Fact]
    public async Task VerifyFullSwagger()
    {
        string parsedDoc = await GetSwaggerDocument(SwaggerExtensions.CompleteSwaggerDocName);

        await Verifier.VerifyJson(parsedDoc, _verifySettings);
    }

    [Fact]
    public async Task VerifyApimSwagger()
    {
        string parsedDoc = await GetSwaggerDocument(SwaggerExtensions.ApimSwaggerDocName);

        await Verifier.VerifyJson(parsedDoc, _verifySettings);
    }

    [Fact]
    public async Task VerifyPublicDocSwagger()
    {
        string parsedDoc = await GetSwaggerDocument(SwaggerExtensions.V1PublicSwaggerDocName);

        await Verifier.VerifyJson(parsedDoc, _verifySettings);
    }

    private async Task<string> GetSwaggerDocument(string documentName)
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"/swagger/{documentName}/swagger.json");
        var swaggerJson = await response.Content.ReadAsStringAsync();
        response.EnsureSuccessStatusCode();
        var readResult = OpenApiDocument.Parse(swaggerJson);
        Assert.NotNull(readResult.Diagnostic);
        Assert.Empty(readResult.Diagnostic.Errors);
        Assert.NotNull(readResult.Document);

        return await readResult.Document.SerializeAsJsonAsync(
            readResult.Diagnostic.SpecificationVersion
        );
    }

    private static VerifySettings _verifySettings
    {
        get
        {
            VerifySettings settings = new();
            settings.UseStrictJson();
            settings.DontScrubGuids();
            settings.DontIgnoreEmptyCollections();
            settings.AddExtraSettings(settings =>
                settings.MetadataPropertyHandling = MetadataPropertyHandling.Ignore
            );
            return settings;
        }
    }
}
