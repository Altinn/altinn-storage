using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Altinn.Platform.Storage.Models;

namespace Altinn.Platform.Storage.Repository;

/// <summary>
/// Serializes typed mutation arguments without mutating inputs.
/// </summary>
internal static class InstanceMutationJson
{
    private static readonly JsonSerializerOptions _options = new()
    {
        Converters = { new ChangeConverterFactory(), new TimestampConverter() },
    };

    internal static string? Serialize<T>(T value)
    {
        return value is null ? null : JsonSerializer.Serialize(value, _options);
    }

    private sealed class ChangeConverterFactory : JsonConverterFactory
    {
        public override bool CanConvert(Type typeToConvert)
        {
            return typeToConvert.IsGenericType
                && typeToConvert.GetGenericTypeDefinition() == typeof(Change<>);
        }

        public override JsonConverter CreateConverter(
            Type typeToConvert,
            JsonSerializerOptions options
        )
        {
            Type converterType = typeof(ChangeConverter<>).MakeGenericType(
                typeToConvert.GetGenericArguments()
            );
            return (JsonConverter)Activator.CreateInstance(converterType, nonPublic: true)!;
        }
    }

    private sealed class ChangeConverter<T> : JsonConverter<Change<T>>
    {
        public override Change<T> Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options
        )
        {
            return Change<T>.Set(JsonSerializer.Deserialize<T>(ref reader, options)!);
        }

        public override void Write(
            Utf8JsonWriter writer,
            Change<T> value,
            JsonSerializerOptions options
        )
        {
            JsonSerializer.Serialize(writer, value.Value, options);
        }
    }

    private sealed class TimestampConverter : JsonConverter<DateTime>
    {
        public override DateTime Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options
        )
        {
            return reader.GetDateTime();
        }

        public override void Write(
            Utf8JsonWriter writer,
            DateTime value,
            JsonSerializerOptions options
        )
        {
            writer.WriteStringValue(MutationTimestamp.NormalizeForPostgres(value));
        }
    }
}
