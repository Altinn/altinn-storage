using System;
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.ApplicationInsights;
using Npgsql;

namespace Altinn.Platform.Storage.Repository
{
    /// <summary>
    /// Helper to track application insights dependencies for PostgreSQL invocations
    /// </summary>
    /// <remarks>
    /// Initializes a new instance of the <see cref="TelemetryTracker"/> class.
    /// </remarks>
    /// <param name="telemetryClient">Telemetry client from DI</param>
    /// <param name="cmd">The npgsql cmd</param>
    public class TelemetryTracker(TelemetryClient telemetryClient, NpgsqlCommand cmd) : IDisposable
    {
        private readonly DateTime _startTime = DateTime.Now;
        private readonly Stopwatch _timer = Stopwatch.StartNew();
        private readonly TelemetryClient _telemetryClient = telemetryClient;
        private readonly NpgsqlCommand _cmd = cmd;
        private readonly Dictionary<string, string> _props = [];
        private bool _tracked = false;

        /// <summary>
        /// ElapsedMilliseconds
        /// </summary>
        public long ElapsedMilliseconds
        {
            get { return _timer.ElapsedMilliseconds; }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Track the PostgreSQL invocation
        /// <paramref name="success">Outcome of invocation</paramref>
        /// </summary>
        public void Track(bool success = true)
        {
            _timer.Stop();
            string properties = null;
            if (_props.Count > 0)
            {
                properties = " ";
                foreach (var prop in _props)
                {
                    properties += $"{prop.Key}: {prop.Value}; ";
                }

                properties = properties[..^2];
            }

            _telemetryClient?.TrackDependency("Postgres", _cmd.CommandText, properties, _startTime, _timer.Elapsed, success);

            _tracked = true;
        }

        /// <summary>
        /// Add property to log
        /// </summary>
        /// <param name="key">Key</param>
        /// <param name="value">Value</param>
        public void Add(string key, string value)
        {
            _props.Add(key, value);
        }

        /// <summary>
        /// Method to satisfy the dispose pattern
        /// </summary>
        /// <param name="disposing">Has disposed already been called?</param>
        protected virtual void Dispose(bool disposing)
        {
            if (!_tracked && disposing)
            {
                Track(false);
                _tracked = true;
            }
        }
    }
}
