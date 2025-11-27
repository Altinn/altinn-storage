using System;
using System.IO;
using System.Reflection;
using System.Text.Json;

namespace Altinn.Platform.Storage.Interface.Tests;

public static class TestdataHelper
{
    private static readonly JsonSerializerOptions _jsonSerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private const string BASE_RESOURCE_PATH = "Altinn.Platform.Storage.Interface.Tests";

    public static T LoadDataFromEmbeddedResourceAsType<T>(string resourcePath)
    {
        var resourceString = LoadDataFromEmbeddedResource(resourcePath);
        T? obj = JsonSerializer.Deserialize<T>(resourceString, _jsonSerializerOptions);

        if (obj == null)
        {
            throw new InvalidDataException(
                $"Unable to deserialize stream for resource {resourcePath}"
            );
        }

        return obj;
    }

    public static string LoadDataFromEmbeddedResourceAsString(string resourcePath)
    {
        var resourceStream = LoadDataFromEmbeddedResource(resourcePath);

        using var reader = new StreamReader(resourceStream);
        string text = reader.ReadToEnd();

        return text;
    }

    public static Stream LoadDataFromEmbeddedResource(string resourcePath)
    {
        var assembly = Assembly.GetExecutingAssembly();
        Stream? resourceStream = assembly.GetManifestResourceStream(
            GetFullResourcePath(resourcePath)
        );

        if (resourceStream == null)
        {
            throw new InvalidOperationException(
                $"Unable to find test data embedded in the test assembly with the given path {GetFullResourcePath(resourcePath)}"
            );
        }

        resourceStream.Seek(0, SeekOrigin.Begin);

        return resourceStream;
    }

    private static string GetFullResourcePath(string resourcePath)
    {
        if (resourcePath.StartsWith(BASE_RESOURCE_PATH))
        {
            return resourcePath;
        }

        return $"{BASE_RESOURCE_PATH}.{resourcePath}";
    }
}
