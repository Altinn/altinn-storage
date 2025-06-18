namespace Altinn.Platform.Storage.Configuration
{
    /// <summary>
    /// Settings for Wolverine, which is used for event processing in the platform.
    /// </summary>
    public class WolverineSettings
    {
        /// <summary>
        /// Feature flag to enable sending messages to the message bus.
        /// </summary>
        public bool EnableSending { get; set; }

        /// <summary>
        /// Connection string for the postgres db
        /// </summary>
        public string PostgresConnectionString { get; set; }

        /// <summary>
        /// Connection string for the Service Bus.
        /// </summary>
        public string ServiceBusConnectionString { get; set; }
    }
}
