#nullable enable

using Microsoft.AspNetCore.Mvc;

namespace Altinn.Platform.Storage.Models
{
    /// <summary>
    /// Represents members to filter, sort, and manage instances.
    /// </summary>
    public class StudioInstanceParameters
    {
        /// <summary>
        /// The organization identifier.
        /// </summary>
        [FromRoute(Name = "org")]
        public required string Org { get; set; }

        /// <summary>
        /// The application identifier.
        /// </summary>
        [FromRoute(Name = "app")]
        public required string App { get; set; }

        /// <summary>
        /// Gets or sets the archive reference.
        /// </summary>
        [FromQuery(Name = "archiveReference")]
        internal string? ArchiveReference { get; set; }

        /// <summary>
        /// The current task identifier.
        /// </summary>
        [FromQuery(Name = "process.currentTask")]
        public string? ProcessCurrentTask { get; set; }

        /// <summary>
        /// A value indicating whether the process is completed.
        /// </summary>
        [FromQuery(Name = "process.isComplete")]
        public bool? ProcessIsComplete { get; set; }

        /// <summary>
        /// The last changed date.
        /// </summary>
        [FromQuery(Name = "lastChanged")]
        public string[]? LastChanged { get; set; }

        /// <summary>
        /// The creation date.
        /// </summary>
        [FromQuery(Name = "created")]
        public string[]? Created { get; set; }

        /// <summary>
        /// Confirmed = false is a compact version of ExcludeConfirmedBy indicating
        /// ExcludeConfirmedBy for the org that invokes the request
        /// </summary>
        [FromQuery(Name = "confirmed")]
        public bool? Confirmed { get; set; }

        /// <summary>
        /// A value indicating whether the instance is soft deleted.
        /// </summary>
        [FromQuery(Name = "status.isSoftDeleted")]
        public bool? IsSoftDeleted { get; set; }

        /// <summary>
        /// A value indicating whether the instance is hard deleted.
        /// </summary>
        [FromQuery(Name = "status.isHardDeleted")]
        public bool? IsHardDeleted { get; set; }

        /// <summary>
        /// A value indicating whether the instance is archived.
        /// </summary>
        [FromQuery(Name = "status.isArchived")]
        public bool? IsArchived { get; set; }

        /// <summary>
        /// The continuation token.
        /// </summary>
        [FromQuery(Name = "continuationToken")]
        public string? ContinuationToken { get; set; }

        /// <summary>
        /// The page size.
        /// </summary>
        [FromQuery(Name = "size")]
        public int? Size { get; set; }

        /// <summary>
        /// Converts to standard instance query parameters object
        /// </summary>
        public InstanceQueryParameters ToInstanceQueryParameters()
        {
            return new InstanceQueryParameters()
            {
                Org = Org,
                AppId = $"{Org}/{App}",
                ArchiveReference = ArchiveReference,
                ProcessCurrentTask = ProcessCurrentTask,
                ProcessIsComplete = ProcessIsComplete,
                LastChanged = LastChanged,
                Created = Created,
                Confirmed = Confirmed,
                IsSoftDeleted = IsSoftDeleted,
                IsHardDeleted = IsHardDeleted,
                IsArchived = IsArchived,
                ContinuationToken = ContinuationToken,
                Size = Size,
                MainVersionInclude = 3,
            };
        }
    }
}
