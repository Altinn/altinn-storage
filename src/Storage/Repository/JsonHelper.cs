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
        /// Serialize with a white list of properties to serialize
        /// </summary>
        /// <param name="obj">Object to serialize</param>
        /// <param name="propertiesToSerialize">White list of properties to serialize</param>
        /// <returns>The serialized object</returns>
        public static string Serialize(object obj, List<string> propertiesToSerialize)
        {
            JsonSerializerOptions options = new()
            {
                TypeInfoResolver = new DefaultJsonTypeInfoResolver
                {
                    Modifiers = { new PropertyModifier(propertiesToSerialize).ModifyTypeInfo },
                },
            };

            return JsonSerializer.Serialize(obj, options);
        }
    }

    private sealed class PropertyModifier
    {
        private readonly List<string> _propertiesToSerialize;

        /// <summary>
        /// Initialize with properties to serialize
        /// </summary>
        /// <param name="propertiesToSerialize">Properties to serialize</param>
        public PropertyModifier(List<string> propertiesToSerialize) =>
            _propertiesToSerialize = propertiesToSerialize;

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

            for (int i = 0; i < ti.Properties.Count; i++)
            {
                if (!_propertiesToSerialize.Contains(ti.Properties[i].Name))
                {
                    ti.Properties.RemoveAt(i--);
                }
            }
        }
    }
}
