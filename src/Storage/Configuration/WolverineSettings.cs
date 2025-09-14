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
        /// Feature flag to enable sending messages to the outbox.
        /// </summary>
        public bool EnableOutbox { get; set; } = true;

        /// <summary>
        /// Connection string for the postgres db
        /// </summary>
        public string PostgresConnectionString { get; set; }

        /// <summary>
        /// Connection string for the Service Bus.
        /// </summary>
        public string ServiceBusConnectionString { get; set; }

        /// <summary>
        /// Batch size when polling for new messages in the outbox table.
        /// </summary>
        public int PollMaxSize { get; set; } = 100;

        /// <summary>
        /// How many milliseconds to wait between polling the outbox table for new messages.
        /// </summary>
        public int PollIdleTimeMs { get; set; } = 1000;

        /// <summary>
        /// How many milliseconds to wait when an error occurs when polling the outbox table
        /// </summary>
        public int PollErrorDelayMs { get; set; } = 10000;

        /// <summary>
        /// The number of seconds a lease is valid when acquired
        /// </summary>
        public int LeaseSecs { get; set; } = 120;

        /// <summary>
        /// The interval, in seconds, between attempts to acquire the poll master role.
        /// </summary>
        public int TryGettingPollMasterIntervalSecs { get; set; } = 600;
    }
}
