using System;
using Altinn.Platform.Storage.Interface.Enums;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using TextJson = System.Text.Json.Serialization;

namespace Altinn.Platform.Storage.Interface.Models
{
    /// <summary>
    /// Represents file scan status for a data element
    /// </summary>
    [JsonObject(ItemNullValueHandling = NullValueHandling.Ignore)]
    public class FileScanStatus
    {
        /// <summary>
        /// Gets or sets the blob save timestamp.
        /// </summary>
        [JsonProperty(PropertyName = "timestamp")]
        public DateTime Timestamp { get; set; }

        /// <summary>
        /// Gets or sets the file scan result.
        /// </summary>
        [JsonProperty(PropertyName = "fileScanResult")]
        [JsonConverter(typeof(StringEnumConverter))]
        [TextJson.JsonConverter(typeof(TextJson.JsonStringEnumConverter))]
        public FileScanResult FileScanResult { get; set; }

        /// <inheritdoc/>
        public override string ToString()
        {
            return JsonConvert.SerializeObject(this);
        }
    }
}
