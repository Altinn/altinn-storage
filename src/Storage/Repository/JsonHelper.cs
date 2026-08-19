#nullable disable

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Altinn.Platform.Storage.Repository;

/// <summary>
/// Json serialization utilities
/// </summary>
public class JsonHelper
{
    /// <summary>
    /// Custom serializer
    /// </summary>
    public static class CustomSerializer
    {
        /// <summary>
        /// Serialize with a white list of properties to serialize. The white list is applied to
        /// every object in the graph, so nested objects must have their properties listed too.
        /// </summary>
        /// <param name="obj">Object to serialize</param>
        /// <param name="propertiesToSerialize">White list of properties to serialize</param>
        /// <returns>The serialized object</returns>
        public static string Serialize(object obj, List<string> propertiesToSerialize)
        {
            return Serialize(obj, _ => propertiesToSerialize);
        }

        /// <summary>
        /// Serialize with a per-type white list of properties to serialize. Types that are absent
        /// from the white list are serialized in full, so a nested object is only trimmed when its
        /// own type is listed.
        /// </summary>
        /// <param name="obj">Object to serialize</param>
        /// <param name="propertiesToSerialize">White list of properties to serialize, per type</param>
        /// <returns>The serialized object</returns>
        public static string Serialize(
            object obj,
            Dictionary<Type, List<string>> propertiesToSerialize
        ) => Serialize(obj, propertiesToSerialize.GetValueOrDefault);

        private static string Serialize(object obj, Func<Type, List<string>> resolveProperties)
        {
            JsonSerializerOptions options = new()
            {
                TypeInfoResolver = new DefaultJsonTypeInfoResolver
                {
                    Modifiers = { new PropertyModifier(resolveProperties).ModifyTypeInfo },
                },
            };

            return JsonSerializer.Serialize(obj, options);
        }
    }

    private sealed class PropertyModifier
    {
        private readonly Func<Type, List<string>> _resolveProperties;

        /// <summary>
        /// Initialize with a lookup from type to the properties to serialize for that type.
        /// Returning null leaves the type untouched.
        /// </summary>
        /// <param name="resolveProperties">Properties to serialize, by type</param>
        public PropertyModifier(Func<Type, List<string>> resolveProperties) =>
            _resolveProperties = resolveProperties;

        /// <summary>
        /// Callback in system.text.json
        /// </summary>
        /// <param name="ti">The object to serialize</param>
        public void ModifyTypeInfo(JsonTypeInfo ti)
        {
            if (ti.Kind != JsonTypeInfoKind.Object)
            {
                return;
            }

            var propertiesToSerialize = _resolveProperties(ti.Type);
            if (propertiesToSerialize is null)
            {
                return;
            }

            for (int i = 0; i < ti.Properties.Count; i++)
            {
                if (!propertiesToSerialize.Contains(ti.Properties[i].Name))
                {
                    ti.Properties.RemoveAt(i--);
                }
            }
        }
    }
}
