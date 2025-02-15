using System;
using Newtonsoft.Json;

namespace Altinn.Platform.Storage.Interface.Models
{
    /// <summary>
    /// Represents an object with information about how the data type is handled by the application logic.
    /// </summary>
    [JsonObject(ItemNullValueHandling = NullValueHandling.Ignore)]
    public class ApplicationLogic
    {
        /// <summary>
        /// Gets or sets a value indicating whether the app-backend will attempt to automatically create (or prefill)
        /// this data type when the task referred by taskId starts.
        /// </summary>
        [JsonProperty(PropertyName = "autoCreate")]
        public bool? AutoCreate { get; set; }

        /// <summary>
        /// Gets or sets the class type to instantiate when creating an instance of this data type.
        /// </summary>
        [JsonProperty(PropertyName = "classRef")]
        public string ClassRef { get; set; }

        /// <summary>
        /// Gets or sets the name and path to the data type schema.
        /// </summary>
        [JsonProperty(PropertyName = "schemaRef")]
        public string SchemaRef { get; set; }

        /// <summary>
        /// Specifies whether anonymous access is allowed in stateless mode or not for this particular data type.
        /// Defaults to false if not specified.
        /// </summary>
        [JsonProperty(PropertyName = "allowAnonymousOnStateless")]
        public bool AllowAnonymousOnStateless { get; set; } = false;

        /// <summary>
        /// Gets or sets a property indicating if data type should be automatically marked for hard deletion on process end.
        /// </summary>
        [JsonProperty(PropertyName = "autoDeleteOnProcessEnd")]
        public bool AutoDeleteOnProcessEnd { get; set; }

        /// <summary>
        /// Specifies whether users should be unable to create data of this type.
        /// Defaults to false if not specified.
        /// </summary>
        [JsonProperty(PropertyName = "disallowUserCreate")]
        public bool DisallowUserCreate { get; set; }

        /// <summary>
        /// Specifies whether users should be unable to delete data of this type.
        /// Defaults to false if not specified.
        /// </summary>
        [JsonProperty(PropertyName = "disallowUserDelete")]
        public bool DisallowUserDelete { get; set; }

        /// <summary>
        /// Specifies whether the data type is compatible with sub form usage.
        /// Defaults to false if not specified.
        /// </summary>
        [JsonProperty(PropertyName = "allowInSubform")]
        [Obsolete("This property is no longer in use and will be removed in the future.")]
        public bool AllowInSubform { get; set; }

        /// <summary>
        /// Gets or sets a property containing configuration for shadow fields for the data type.
        /// </summary>
        [JsonProperty(PropertyName = "shadowFields")]
        public ShadowFields ShadowFields { get; set; }
    }
}
