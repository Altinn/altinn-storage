using System;
using System.Collections.Generic;

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
        private const string _mainVersionExcludeParameterName = "mainVersionExclude";
        private const string _mainVersionIncludeParameterName = "mainVersionInclude";
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
        /// Gets or sets altinn version to include
        /// </summary>
        [FromQuery(Name = _mainVersionIncludeParameterName)]
        public int? MainVersionInclude { get; set; }

        /// <summary>
        /// Gets or sets altinn version to exclude
        /// </summary>
        [FromQuery(Name = _mainVersionExcludeParameterName)]
        public int? MainVersionExclude { get; set; }

        /// <summary>
        /// Gets or sets an array of application identifiers.
        /// </summary>
        internal string[] AppIds { get; set; }

        /// <summary>
        /// Gets or sets the archive reference.
        /// </summary>
        internal string ArchiveReference { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the status is active or soft deleted.
        /// </summary>
        internal bool? IsActiveOrSoftDeleted { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the status is archived or soft deleted.
        /// </summary>
        internal bool? IsArchivedOrSoftDeleted { get; set; }

        /// <summary>
        /// Gets or sets the list of instance owner party IDs.
        /// </summary>
        internal int?[] InstanceOwnerPartyIds { get; set; }

        /// <summary>
        /// Gets or sets the message box interval.
        /// </summary>
        internal string[] MsgBoxInterval { get; set; }

        /// <summary>
        /// Gets or sets the search string.
        /// </summary>
        internal string SearchString { get; set; }

        /// <summary>
        /// Gets or sets the value by which the result will be sorted.
        /// </summary>
        internal string SortBy { get; set; }

        /// <summary>
        /// Generates the PostgreSQL parameters from the query parameters
        /// </summary>
        /// <returns>Dictionary with PostgreSQL parameters</returns>
        public Dictionary<string, object> GeneratePostgreSQLParameters()
        {
            var postgresParams = new Dictionary<string, object>();

            AddParamIfNotNull(postgresParams, _sizeParameterName, Size);
            AddParamIfNotNull(postgresParams, _appIdParameterName, AppId);
            AddParamIfNotNull(postgresParams, _statusIsArchivedParameterName, IsArchived);
            AddParamIfNotNull(postgresParams, _statusIsSoftDeletedParameterName, IsSoftDeleted);
            AddParamIfNotNull(postgresParams, _statusIsHardDeletedParameterName, IsHardDeleted);
            AddParamIfNotNull(postgresParams, _processIsCompleteParameterName, ProcessIsComplete);
            AddParamIfNotNull(postgresParams, _instanceOwnerPartyIdParameterName, InstanceOwnerPartyId);
            AddParamIfNotNull(postgresParams, _statusIsActiveOrSoftDeletedParameterName, IsActiveOrSoftDeleted);
            AddParamIfNotNull(postgresParams, _statusIsArchivedOrSoftDeletedParameterName, IsArchivedOrSoftDeleted);
            AddParamIfNotNull(postgresParams, _mainVersionExcludeParameterName, MainVersionExclude);
            AddParamIfNotNull(postgresParams, _mainVersionIncludeParameterName, MainVersionInclude);

            AddParamIfNotEmpty(postgresParams, _orgParameterName, Org);
            AddParamIfNotEmpty(postgresParams, _appIdsParameterName, AppIds);
            AddParamIfNotEmpty(postgresParams, _currentTaskParameterName, ProcessCurrentTask);
            AddParamIfNotEmpty(postgresParams, _instanceOwnerPartyIdsParameterName, InstanceOwnerPartyIds);
            AddParamIfNotEmpty(postgresParams, _archiveReferenceParameterName, ArchiveReference?.ToLower());
            AddParamIfNotEmpty(postgresParams, _excludeConfirmedByParameterName, GetExcludeConfirmedBy(ExcludeConfirmedBy));

            AddDateParamIfNotNull(postgresParams, _dueBeforeParameterName, DueBefore);
            AddDateParamIfNotNull(postgresParams, _creationDateParameterName, Created);
            AddDateParamIfNotNull(postgresParams, _lastChangedParameterName, LastChanged);
            AddDateParamIfNotNull(postgresParams, _visibleAfterParameterName, VisibleAfter);
            AddDateParamIfNotNull(postgresParams, _processEndedParameterName, ProcessEnded);
            AddDateParamIfNotNull(postgresParams, _messageBoxIntervalParameterName, MsgBoxInterval);

            if (!string.IsNullOrEmpty(SortBy))
            {
                postgresParams.Add(_sortAscendingParameterName, !SortBy.StartsWith("desc:", StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrEmpty(SearchString))
            {
                postgresParams.Add(_searchStringParameterName, $"%{SearchString}%");
            }

            if (string.IsNullOrEmpty(ContinuationToken))
            {
                postgresParams.Add(_continueIndexParameterName, -1);
                postgresParams.Add(_lastChangedIndexParameterName, DateTime.MinValue);
            }
            else if (ContinuationToken.Contains(';'))
            {
                var tokens = ContinuationToken.Split(';');
                postgresParams.Add(_continueIndexParameterName, long.Parse(tokens[1]));
                postgresParams.Add(_lastChangedIndexParameterName, new DateTime(long.Parse(tokens[0]), DateTimeKind.Utc));
            }

            return postgresParams;
        }

        /// <summary>
        /// Checks if the combination of InstanceOwnerPartyId, InstanceOwnerPartyIds, and InstanceOwnerIdentifier in the query parameters is invalid.
        /// </summary>
        /// <returns>
        /// <c>true</c> if both InstanceOwnerPartyId (or InstanceOwnerPartyIds) and InstanceOwnerIdentifier are present; otherwise, <c>false</c>.
        /// </returns>
        public bool IsInvalidInstanceOwnerCombination()
        {
            return (InstanceOwnerPartyId.HasValue || InstanceOwnerPartyIds != null) && !string.IsNullOrEmpty(InstanceOwnerIdentifier);
        }

        /// <summary>
        /// Adds a parameter to the dictionary if the value is not null or empty.
        /// </summary>
        /// <param name="postgresParams">The dictionary to add the parameter to.</param>
        /// <param name="paramName">The name of the parameter.</param>
        /// <param name="value">The value of the parameter.</param>
        private static void AddParamIfNotEmpty(Dictionary<string, object> postgresParams, string paramName, object value)
        {
            if (value == null)
            {
                return;
            }

            var valueAsString = value.ToString();
            if (string.IsNullOrEmpty(valueAsString))
            {
                return;
            }

            postgresParams.Add(GetPgParamName(paramName), value);
        }

        /// <summary>
        /// Adds a parameter to the dictionary if the value is not null.
        /// </summary>
        /// <param name="postgresParams">The dictionary to add the parameter to.</param>
        /// <param name="paramName">The name of the parameter.</param>
        /// <param name="value">The value of the parameter.</param>
        private static void AddParamIfNotNull(Dictionary<string, object> postgresParams, string paramName, object value)
        {
            if (value == null)
            {
                return;
            }

            postgresParams.Add(GetPgParamName(paramName), value);
        }

        /// <summary>
        /// Adds a date parameter to the dictionary if the query values are not null or empty.
        /// </summary>
        /// <param name="postgresParams">The dictionary to add the parameter to.</param>
        /// <param name="paramName">The name of the parameter.</param>
        /// <param name="queryValues">The query values containing the date.</param>
        private static void AddDateParamIfNotNull(Dictionary<string, object> postgresParams, string paramName, StringValues queryValues)
        {
            if (StringValues.IsNullOrEmpty(queryValues))
            {
                return;
            }

            foreach (string value in queryValues)
            {
                try
                {
                    string @operator = value.Split(':')[0];
                    string dateValue = value[(@operator.Length + 1)..];
                    string postgresParamName = GetPgParamName($"{paramName}_{@operator}");
                    postgresParams.Add(postgresParamName, DateTimeHelper.ParseAndConvertToUniversalTime(dateValue));
                }
                catch
                {
                    throw new ArgumentException($"Invalid date expression: {value} for query key: {paramName}");
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
            if (StringValues.IsNullOrEmpty(queryValues))
            {
                return null;
            }

            string[] confirmations = new string[queryValues.Count];

            for (int i = 0; i < queryValues.Count; i++)
            {
                confirmations[i] = $"[{{\"StakeholderId\":\"{queryValues[i]}\"}}]";
            }

            return confirmations;
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
