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
        /// Gets or sets the id of the instance which the data element belongs to.
        /// This field is normally not populated if data element is part of instance metadata.
        /// </summary>
        [JsonProperty(PropertyName = "instanceGuid")]
        public string InstanceGuid { get; set; }

        /// <summary>
        /// Gets or sets Id of DataElement concerned
        /// </summary>
        [JsonProperty(PropertyName = "dataElementId")]
        public string DataElementId { get; set; }

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
