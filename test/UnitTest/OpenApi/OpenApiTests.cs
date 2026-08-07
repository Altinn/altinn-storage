using System.Threading.Tasks;
using Altinn.Platform.Storage.UnitTest.Fixture;
using Argon;
using Microsoft.OpenApi;
using VerifyTests;
using VerifyXunit;
using Xunit;
using Xunit.Abstractions;

namespace Altinn.Platform.Storage.UnitTest.OpenApi;

public class OpenApiTests(TestApplicationFactory<Program> factory)
    : IClassFixture<TestApplicationFactory<Program>>
{
    [Fact]
    public async Task VerifyFullSwagger()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/swagger/v1/swagger.json");
        var swaggerJson = await response.Content.ReadAsStringAsync();
        // output.WriteLine(swaggerJson);
        response.EnsureSuccessStatusCode();
        var readResult = OpenApiDocument.Parse(swaggerJson);
        Assert.NotNull(readResult.Diagnostic);
        Assert.Empty(readResult.Diagnostic.Errors);
        Assert.NotNull(readResult.Document);
        var document = readResult.Document;
        // document.Info.Version = ""; // This includes the nuget version
        var parsedDoc = await document.SerializeAsJsonAsync(
            readResult.Diagnostic.SpecificationVersion
        );
        await Verifier.VerifyJson(parsedDoc, _verifySettings);
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
