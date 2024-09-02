using Microsoft.AspNetCore.Mvc;

namespace Altinn.Platform.Storage.Models
{
    /// <summary>
    /// Represents query parameters to retrieve instances data.
    /// </summary>
    public class InstanceQueryParameters
    {
        /// <summary>
        /// Gets or sets the application owner identifier.
        /// </summary>
        [FromQuery]
        public string Org { get; set; }

        /// <summary>
        /// Gets or sets the application identifier.
        /// </summary>
        [FromQuery]
        public string AppId { get; set; }

        /// <summary>
        /// Gets or sets the application creation time.
        /// </summary>
        [FromQuery]
        public string Created { get; set; }

        /// <summary>
        /// Gets or sets the date and time by which the instance is due.
        /// </summary>
        [FromQuery]
        public string DueBefore { get; set; }

        /// <summary>
        /// Gets or sets the task identifier within the running process.
        /// </summary>
        [FromQuery(Name = "process.currentTask")]
        public string CurrentTaskId { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the process is completed.
        /// </summary>
        [FromQuery(Name = "process.isComplete")]
        public bool? ProcessIsComplete { get; set; }

        /// <summary>
        /// Gets or sets the final state of the process.
        /// </summary>
        [FromQuery(Name = "process.endEvent")]
        public string ProcessEndEvent { get; set; }

        /// <summary>
        /// Gets or sets the end value of the process.
        /// </summary>
        [FromQuery(Name = "process.ended")]
        public string ProcessEnded { get; set; }

        /// <summary>
        /// Gets or sets the instance owner party identifier.
        /// </summary>
        [FromQuery(Name = "instanceOwner.partyId")]
        public int? InstanceOwnerPartyId { get; set; }

        /// <summary>
        /// Gets or sets the instance owner identifier.
        /// </summary>
        [FromHeader(Name = "X-Ai-InstanceOwnerIdentifier")]
        public string InstanceOwnerIdentifier { get; set; }

        /// <summary>
        /// Gets or sets the date and time when this instance was last modified.
        /// </summary>
        [FromQuery]
        public string LastChanged { get; set; }

        /// <summary>
        /// Gets or sets the date and time after which this instance becomes visible.
        /// </summary>
        [FromQuery(Name = "visibleAfter")]
        public string VisibleAfter { get; set; }

        /// <summary>
        /// Gets or sets a value to hide instances already confirmed by stakeholder.
        /// </summary>
        [FromQuery]
        public string ExcludeConfirmedBy { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether this instances is soft deleted.
        /// </summary>
        [FromQuery(Name = "status.isSoftDeleted")]
        public bool IsSoftDeleted { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether this instances is hard deleted.
        /// </summary>
        [FromQuery(Name = "status.isHardDeleted")]
        public bool IsHardDeleted { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether this instances is archived.
        /// </summary>
        [FromQuery(Name = "status.isArchived")]
        public bool IsArchived { get; set; }

        /// <summary>
        /// Gets or sets the continuation token for pagination.
        /// </summary>
        [FromQuery]
        public string ContinuationToken { get; set; }

        /// <summary>
        /// Gets or sets the size of a single page.
        /// </summary>
        [FromQuery]
        public int? Size { get; set; }
    }
}
