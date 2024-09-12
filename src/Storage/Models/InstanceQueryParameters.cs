using System;
using System.Collections.Generic;
using System.Linq;

using Altinn.Platform.Storage.Helpers;

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Primitives;

namespace Altinn.Platform.Storage.Models
{
    /// <summary>
    /// Represents members to filter, sort, and manage instances.
    /// </summary>
    public class InstanceQueryParameters
    {
        private const string _appIdParameterName = "appId";
        private const string _appIdsParameterName = "appIds";
        private const string _archiveReferenceParameterName = "archiveReference";
        private const string _continuationTokenParameterName = "continuationToken";
        private const string _creationDateParameterName = "created";
        private const string _currentTaskParameterName = "process.currentTask";
        private const string _dueBeforeParameterName = "dueBefore";
        private const string _excludeConfirmedByParameterName = "excludeConfirmedBy";
        private const string _instanceOwnerIdentifierHeaderName = "X-Ai-InstanceOwnerIdentifier";
        private const string _instanceOwnerPartyIdParameterName = "instanceOwner.partyId";
        private const string _instanceOwnerPartyIdsParameterName = "instanceOwner.partyIds";
        private const string _lastChangedParameterName = "lastChanged";
        private const string _messageBoxIntervalParameterName = "msgBoxInterval";
        private const string _orgParameterName = "org";
        private const string _processEndEventParameterName = "process.endEvent";
        private const string _processEndedParameterName = "process.ended";
        private const string _processIsCompleteParameterName = "process.isComplete";
        private const string _sizeParameterName = "size";
        private const string _statusIsActiveOrSoftDeletedParameterName = "status.isActiveOrSoftDeleted";
        private const string _statusIsArchivedOrSoftDeletedParameterName = "status.isArchivedOrSoftDeleted";
        private const string _statusIsArchivedParameterName = "status.isArchived";
        private const string _statusIsHardDeletedParameterName = "status.isHardDeleted";
        private const string _statusIsSoftDeletedParameterName = "status.isSoftDeleted";
        private const string _visibleAfterParameterName = "visibleAfter";
        private const string _searchStringParameterName = "_search_string";
        private const string _sortAscendingParameterName = "_sort_ascending";
        private const string _continueIndexParameterName = "_continue_idx";
        private const string _lastChangedIndexParameterName = "_lastChanged_idx";

        /// <summary>
        /// The organization identifier.
        /// </summary>
        [FromQuery(Name = _orgParameterName)]
        public string Org { get; set; }

        /// <summary>
        /// The application identifier.
        /// </summary>
        [FromQuery(Name = _appIdParameterName)]
        public string AppId { get; set; }

        /// <summary>
        /// The current task identifier.
        /// </summary>
        [FromQuery(Name = _currentTaskParameterName)]
        public string ProcessCurrentTask { get; set; }

        /// <summary>
        /// A value indicating whether the process is completed.
        /// </summary>
        [FromQuery(Name = _processIsCompleteParameterName)]
        public bool? ProcessIsComplete { get; set; }

        /// <summary>
        /// The process end state.
        /// </summary>
        [FromQuery(Name = _processEndEventParameterName)]
        public string ProcessEndEvent { get; set; }

        /// <summary>
        /// The process ended value.
        /// </summary>
        [FromQuery(Name = _processEndedParameterName)]
        public string ProcessEnded { get; set; }

        /// <summary>
        /// The instance owner party identifier.
        /// </summary>
        [FromQuery(Name = _instanceOwnerPartyIdParameterName)]
        public int? InstanceOwnerPartyId { get; set; }

        /// <summary>
        /// The last changed date.
        /// </summary>
        [FromQuery(Name = _lastChangedParameterName)]
        public string LastChanged { get; set; }

        /// <summary>
        /// The creation date.
        /// </summary>
        [FromQuery(Name = _creationDateParameterName)]
        public string Created { get; set; }

        /// <summary>
        /// The visible after date time.
        /// </summary>
        [FromQuery(Name = _visibleAfterParameterName)]
        public string VisibleAfter { get; set; }

        /// <summary>
        /// The due before date time.
        /// </summary>
        [FromQuery(Name = _dueBeforeParameterName)]
        public string DueBefore { get; set; }

        /// <summary>
        /// A string that will hide instances already confirmed by stakeholder.
        /// </summary>
        [FromQuery(Name = _excludeConfirmedByParameterName)]
        public string ExcludeConfirmedBy { get; set; }

        /// <summary>
        /// A value indicating whether the instance is soft deleted.
        /// </summary>
        [FromQuery(Name = _statusIsSoftDeletedParameterName)]
        public bool? IsSoftDeleted { get; set; }

        /// <summary>
        /// A value indicating whether the instance is hard deleted.
        /// </summary>
        [FromQuery(Name = _statusIsHardDeletedParameterName)]
        public bool? IsHardDeleted { get; set; }

        /// <summary>
        /// A value indicating whether the instance is archived.
        /// </summary>
        [FromQuery(Name = _statusIsArchivedParameterName)]
        public bool? IsArchived { get; set; }

        /// <summary>
        /// The continuation token.
        /// </summary>
        [FromQuery(Name = _continuationTokenParameterName)]
        public string ContinuationToken { get; set; }

        /// <summary>
        /// The page size.
        /// </summary>
        [FromQuery(Name = _sizeParameterName)]
        public int? Size { get; set; }

        /// <summary>
        /// The instance owner identifier.
        /// </summary>
        [FromHeader(Name = _instanceOwnerIdentifierHeaderName)]
        public string InstanceOwnerIdentifier { get; set; }

        /// <summary>
        /// Gets or sets an array of application identifiers.
        /// </summary>
        public string[] AppIds { get; set; }

        /// <summary>
        /// Gets or sets the archive reference.
        /// </summary>
        public string ArchiveReference { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the status is active or soft deleted.
        /// </summary>
        public bool? IsActiveOrSoftDeleted { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the status is archived or soft deleted.
        /// </summary>
        public bool? IsArchivedOrSoftDeleted { get; set; }

        /// <summary>
        /// Gets or sets the list of instance owner party IDs.
        /// </summary>
        public List<int?> InstanceOwnerPartyIds { get; set; }

        /// <summary>
        /// Gets or sets the message box interval.
        /// </summary>
        public string[] MsgBoxInterval { get; set; }

        /// <summary>
        /// Gets or sets the search string.
        /// </summary>
        public string SearchString { get; set; }

        /// <summary>
        /// Gets or sets the value by which the result will be sorted.
        /// </summary>
        public string SortBy { get; set; }

        /// <summary>
        /// Generates the PostgreSQL parameters from the query parameters
        /// </summary>
        /// <returns>Dictionary with PostgreSQL parameters</returns>
        public Dictionary<string, object> GeneratePostgreSQLParameters()
        {
            Dictionary<string, object> postgresParams = [];

            if (InstanceOwnerPartyId != null)
            {
                postgresParams.Add(GetPgParamName(_instanceOwnerPartyIdParameterName), InstanceOwnerPartyId);
            }
            else if (InstanceOwnerPartyIds != null && InstanceOwnerPartyIds.Count > 0)
            {
                postgresParams.Add(GetPgParamName(_instanceOwnerPartyIdsParameterName), InstanceOwnerPartyIds.ToArray());
            }

            if (AppId != null)
            {
                postgresParams.Add(GetPgParamName(_appIdParameterName), AppId);
            }
            else if (AppIds != null && AppIds.Length > 0)
            {
                postgresParams.Add(GetPgParamName(_appIdsParameterName), AppIds.ToArray());
            }

            if (!string.IsNullOrEmpty(ExcludeConfirmedBy))
            {
                postgresParams.Add(GetPgParamName(_excludeConfirmedByParameterName), GetExcludeConfirmedBy(ExcludeConfirmedBy));
            }

            if (!string.IsNullOrEmpty(Org))
            {
                postgresParams.Add(GetPgParamName(_orgParameterName), Org);
            }

            if (!string.IsNullOrEmpty(ProcessCurrentTask))
            {
                postgresParams.Add(GetPgParamName(_currentTaskParameterName), ProcessCurrentTask);
            }

            if (!string.IsNullOrEmpty(SearchString))
            {
                postgresParams.Add(_searchStringParameterName, $"%{SearchString}%");
            }

            if (!string.IsNullOrEmpty(ArchiveReference))
            {
                postgresParams.Add(GetPgParamName(_archiveReferenceParameterName), ArchiveReference.ToLower());
            }

            if (Size != null)
            {
                postgresParams.Add(GetPgParamName(_sizeParameterName), Size);
            }

            if (IsArchived != null)
            {
                postgresParams.Add(GetPgParamName(_statusIsArchivedParameterName), IsArchived);
            }

            if (IsSoftDeleted != null)
            {
                postgresParams.Add(GetPgParamName(_statusIsSoftDeletedParameterName), IsSoftDeleted);
            }

            if (IsHardDeleted != null)
            {
                postgresParams.Add(GetPgParamName(_statusIsHardDeletedParameterName), IsHardDeleted);
            }

            if (ProcessIsComplete != null)
            {
                postgresParams.Add(GetPgParamName(_processIsCompleteParameterName), ProcessIsComplete);
            }

            if (IsArchivedOrSoftDeleted != null)
            {
                postgresParams.Add(GetPgParamName(_statusIsArchivedOrSoftDeletedParameterName), IsArchivedOrSoftDeleted);
            }

            if (IsActiveOrSoftDeleted != null)
            {
                postgresParams.Add(GetPgParamName(_statusIsActiveOrSoftDeletedParameterName), IsActiveOrSoftDeleted);
            }

            if (!string.IsNullOrEmpty(SortBy))
            {
                postgresParams.Add(_sortAscendingParameterName, !SortBy.StartsWith("desc:", StringComparison.OrdinalIgnoreCase));
            }

            if (LastChanged != null)
            {
                AddDateParam(_lastChangedParameterName, LastChanged, postgresParams, false);
            }

            if (Created != null)
            {
                AddDateParam(_creationDateParameterName, Created, postgresParams, false);
            }

            if (MsgBoxInterval != null)
            {
                AddDateParam(_messageBoxIntervalParameterName, MsgBoxInterval, postgresParams, false);
            }

            if (!string.IsNullOrEmpty(VisibleAfter))
            {
                AddDateParam(_visibleAfterParameterName, VisibleAfter, postgresParams, false);
            }

            if (!string.IsNullOrEmpty(DueBefore))
            {
                AddDateParam(_dueBeforeParameterName, DueBefore, postgresParams, false);
            }

            if (!string.IsNullOrEmpty(ProcessEnded))
            {
                AddDateParam(_processEndedParameterName, ProcessEnded, postgresParams, false);
            }

            if (string.IsNullOrEmpty(ContinuationToken))
            {
                postgresParams.Add(_continueIndexParameterName, -1);
                postgresParams.Add(_lastChangedIndexParameterName, DateTime.MinValue);
            }
            else
            {
                postgresParams.Add(_continueIndexParameterName, long.Parse(ContinuationToken.Split(';')[1]));
                postgresParams.Add(_lastChangedIndexParameterName, new DateTime(long.Parse(ContinuationToken.Split(';')[0]), DateTimeKind.Utc));
            }

            return postgresParams;
        }

        /// <summary>
        /// Adds date parameters to the PostgreSQL parameters dictionary.
        /// </summary>
        /// <param name="dateParam">The date parameter name.</param>
        /// <param name="queryValues">The query values containing date expressions.</param>
        /// <param name="postgresParams">The dictionary to add PostgreSQL parameters to.</param>
        /// <param name="valueAsString">Indicates whether to add the value as a string.</param>
        /// <exception cref="ArgumentException">Thrown when the date expression is invalid.</exception>
        private static void AddDateParam(string dateParam, StringValues queryValues, Dictionary<string, object> postgresParams, bool valueAsString)
        {
            foreach (string value in queryValues)
            {
                try
                {
                    string @operator = value.Split(':')[0];
                    string dateValue = value[(@operator.Length + 1)..];
                    string postgresParamName = GetPgParamName($"{dateParam}_{@operator}");
                    postgresParams.Add(postgresParamName, valueAsString ? dateValue : DateTimeHelper.ParseAndConvertToUniversalTime(dateValue));
                }
                catch
                {
                    throw new ArgumentException($"Invalid date expression: {value} for query key: {dateParam}");
                }
            }
        }

        /// <summary>
        /// Retrieves an array of exclude confirmed by values from the query values.
        /// </summary>
        /// <param name="queryValues">The query values containing stakeholder IDs.</param>
        /// <returns>An array of exclude confirmed by values.</returns>
        private static string[] GetExcludeConfirmedBy(StringValues queryValues)
        {
            List<string> confirmations = [];

            foreach (var queryParameter in queryValues)
            {
                confirmations.Add($"[{{\"StakeholderId\":\"{queryParameter}\"}}]");
            }

            return [.. confirmations];
        }

        /// <summary>
        /// Converts a query parameter name to a PostgreSQL parameter name.
        /// </summary>
        /// <param name="queryParameter">The query parameter name.</param>
        /// <returns>The PostgreSQL parameter name.</returns>
        private static string GetPgParamName(string queryParameter)
        {
            return "_" + queryParameter.Replace(".", "_");
        }
    }
}
