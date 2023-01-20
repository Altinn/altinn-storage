using Newtonsoft.Json;

namespace Altinn.Platform.Storage.Interface.Models
{
    /// <summary>
    /// Represents file scan status for a data element
    /// </summary>
    [JsonObject(ItemNullValueHandling = NullValueHandling.Ignore)]
    public class FileScanStatus
    {
        /// <summary>
        /// Gets or sets the MD5 content hash computed by Azure Blob Storage
        /// </summary>
        [JsonProperty(PropertyName = "contentHash")]
        public byte[] ContentHash { get; set; }

        /// <summary>
        /// Gets or sets the scan result
        /// </summary>
        [JsonProperty(PropertyName = "fileScanResult")]
        public string FileScanResult { get; set; }

        /// <inheritdoc/>
        public override string ToString()
        {
            return JsonConvert.SerializeObject(this);
        }
    }
}
