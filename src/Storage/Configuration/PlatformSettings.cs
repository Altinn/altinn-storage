namespace Altinn.Platform.Storage.Configuration
{
    /// <summary>
    /// Represents a set of configuration options when communicating with the platform API.
    /// Instances of this class is initialised with values from app settings. Some values can be overridden by environment variables.
    /// </summary>
    public class PlatformSettings
    {
        /// <summary>
        /// Gets or sets the url for the Authorization API endpoint.
        /// </summary>
        public string ApiAuthorizationEndpoint { get; set; }

        /// <summary>
        /// Gets or sets the url for the Profile API endpoint
        /// </summary>
        public string ApiProfileEndpoint { get; set; }
    }
}