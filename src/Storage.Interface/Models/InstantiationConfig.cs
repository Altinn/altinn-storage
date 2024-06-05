using Newtonsoft.Json;

namespace Altinn.Platform.Storage.Interface.Models
{
    /// <summary>
    /// Configuration related to instantiations in an app.
    /// </summary>
    public class InstantiationConfig 
    {
        /// <summary>
        /// Gets or sets a property indicating whether manual instantiation is disabled.
        /// </summary>
        [JsonProperty(PropertyName = "manualInstantiationDisabled")]
        public bool ManualInstantiationDisabled { get; set; }
    }
}
