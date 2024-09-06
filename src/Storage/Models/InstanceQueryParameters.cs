﻿using System;
using System.Collections.Generic;
using System.Linq;

using Altinn.Platform.Storage.Helpers;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Primitives;

namespace Altinn.Platform.Storage.Models
{
    /// <summary>
    /// Represents query parameters to retrieve instances data.
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
        private const string _lastChangedParameterName = "lastChanged";
        private const string _messageBoxIntervalParameterName = "msgBoxInterval";
        private const string _orgParameterName = "org";
        private const string _processEndEventParameterName = "process.endEvent";
        private const string _processEndedParameterName = "process.ended";
        private const string _processIsCompleteParameterName = "process.isComplete";
        private const string _searchStringParameterName = "searchString";
        private const string _sizeParameterName = "size";
        private const string _sortByParameterName = "sortBy";
        private const string _statusIsActiveOrSoftDeletedParameterName = "status.isActiveOrSoftDeleted";
        private const string _statusIsArchivedOrSoftDeletedParameterName = "status.isArchivedOrSoftDeleted";
        private const string _statusIsArchivedParameterName = "status.isArchived";
        private const string _statusIsHardDeletedParameterName = "status.isHardDeleted";
        private const string _statusIsSoftDeletedParameterName = "status.isSoftDeleted";
        private const string _visibleAfterParameterName = "visibleAfter";

        /// <summary>
        /// Gets or sets the application identifier.
        /// </summary>
        [FromQuery(Name = _appIdParameterName)]
        public string AppId { get; set; }

        /// <summary>
        /// Gets or sets an array of application identifiers.
        /// </summary>
        [FromQuery(Name = _appIdsParameterName)]
        public string[] AppIds { get; set; }

        /// <summary>
        /// Gets or sets the archive reference.
        /// </summary>
        [FromQuery(Name = _archiveReferenceParameterName)]
        public string ArchiveReference { get; set; }

        /// <summary>
        /// Gets or sets the continuation token.
        /// </summary>
        [FromQuery(Name = _continuationTokenParameterName)]
        public string ContinuationToken { get; set; }

        /// <summary>
        /// Gets or sets the creation date.
        /// </summary>
        [FromQuery(Name = _creationDateParameterName)]
        public string Created { get; set; }

        /// <summary>
        /// Gets or sets the current task identifier.
        /// </summary>
        [FromQuery(Name = _currentTaskParameterName)]
        public string CurrentTaskId { get; set; }

        /// <summary>
        /// Gets or sets the due before date.
        /// </summary>
        [FromQuery(Name = _dueBeforeParameterName)]
        public string DueBefore { get; set; }

        /// <summary>
        /// Gets or sets a string that will hide instances already confirmed by stakeholder.
        /// </summary>
        [FromQuery(Name = _excludeConfirmedByParameterName)]
        public string ExcludeConfirmedBy { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the status is active or soft deleted.
        /// </summary>
        [FromQuery(Name = _statusIsActiveOrSoftDeletedParameterName)]
        public bool? IsActiveOrSoftDeleted { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the status is archived.
        /// </summary>
        [FromQuery(Name = _statusIsArchivedParameterName)]
        public bool? IsArchived { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the status is archived or soft deleted.
        /// </summary>
        [FromQuery(Name = _statusIsArchivedOrSoftDeletedParameterName)]
        public bool? IsArchivedOrSoftDeleted { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the status is hard deleted.
        /// </summary>
        [FromQuery(Name = _statusIsHardDeletedParameterName)]
        public bool? IsHardDeleted { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the status is soft deleted.
        /// </summary>
        [FromQuery(Name = _statusIsSoftDeletedParameterName)]
        public bool? IsSoftDeleted { get; set; }

        /// <summary>
        /// Gets or sets the instance owner identifier.
        /// </summary>
        [FromHeader(Name = _instanceOwnerIdentifierHeaderName)]
        public string InstanceOwnerIdentifier { get; set; }

        /// <summary>
        /// Gets or sets the instance owner party identifier.
        /// </summary>
        [FromHeader(Name = _instanceOwnerPartyIdParameterName)]
        public int? InstanceOwnerPartyId { get; set; }

        /// <summary>
        /// Gets or sets a list of instance owner party identifier.
        /// </summary>
        [FromHeader(Name = _instanceOwnerPartyIdParameterName)]
        public List<int?> InstanceOwnerPartyIdList { get; set; }

        /// <summary>
        /// Gets or sets the last changed date.
        /// </summary>
        [FromHeader(Name = _lastChangedParameterName)]
        public string LastChanged { get; set; }

        /// <summary>
        /// Gets or sets the message box interval.
        /// </summary>
        [FromHeader(Name = _messageBoxIntervalParameterName)]
        public string[] MsgBoxInterval { get; set; }

        /// <summary>
        /// Gets or sets the organization.
        /// </summary>
        [FromHeader(Name = _orgParameterName)]
        public string Org { get; set; }

        /// <summary>
        /// Gets or sets the process ended value.
        /// </summary>
        [FromQuery(Name = _processEndedParameterName)]
        public string ProcessEnded { get; set; }

        /// <summary>
        /// Gets or sets the process end state.
        /// </summary>
        [FromQuery(Name = _processEndEventParameterName)]
        public string ProcessEndEvent { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the process is completed.
        /// </summary>
        [FromQuery(Name = _processIsCompleteParameterName)]
        public bool? ProcessIsComplete { get; set; }

        /// <summary>
        /// Gets or sets the search string.
        /// </summary>
        [FromQuery(Name = _searchStringParameterName)]
        public string SearchString { get; set; }

        /// <summary>
        /// Gets or sets the page size.
        /// </summary>
        [FromQuery(Name = _sizeParameterName)]
        public int? Size { get; set; }

        /// <summary>
        /// Gets or sets the value by which the result will be sorted.
        /// </summary>
        [FromQuery(Name = _sortByParameterName)]
        public string SortBy { get; set; }

        /// <summary>
        /// Gets or sets the visible after date.
        /// </summary>
        [FromQuery(Name = _visibleAfterParameterName)]
        public string VisibleAfter { get; set; }

        /// <summary>
        /// Populates the instance properties from a request.
        /// </summary>
        /// <param name="request">The request.</param>
        /// <exception cref="ArgumentNullException">request</exception>
        public void PopulatePropertiesFromRequest(HttpRequest request)
        {
            var query = request?.Query ?? throw new ArgumentNullException();

            AppId = GetQueryValue(query, _appIdParameterName, AppId);
            AppIds = GetQueryValue(query, _appIdsParameterName, AppIds);
            ArchiveReference = GetQueryValue(query, _archiveReferenceParameterName, ArchiveReference);
            ContinuationToken = GetQueryValue(query, _continuationTokenParameterName, ContinuationToken);
            Created = GetQueryValue(query, _creationDateParameterName, Created);
            CurrentTaskId = GetQueryValue(query, _currentTaskParameterName, CurrentTaskId);
            DueBefore = GetQueryValue(query, _dueBeforeParameterName, DueBefore);
            ExcludeConfirmedBy = GetQueryValue(query, _excludeConfirmedByParameterName, ExcludeConfirmedBy);
            InstanceOwnerIdentifier = GetQueryValue(query, _instanceOwnerIdentifierHeaderName, InstanceOwnerIdentifier);
            IsActiveOrSoftDeleted = GetQueryValue(query, _statusIsActiveOrSoftDeletedParameterName, IsActiveOrSoftDeleted);
            IsArchived = GetQueryValue(query, _statusIsArchivedParameterName, IsArchived);
            IsArchivedOrSoftDeleted = GetQueryValue(query, _statusIsArchivedOrSoftDeletedParameterName, IsArchivedOrSoftDeleted);
            IsHardDeleted = GetQueryValue(query, _statusIsHardDeletedParameterName, IsHardDeleted);
            IsSoftDeleted = GetQueryValue(query, _statusIsSoftDeletedParameterName, IsSoftDeleted);
            LastChanged = GetQueryValue(query, _lastChangedParameterName, LastChanged);
            MsgBoxInterval = GetQueryValue(query, _messageBoxIntervalParameterName, MsgBoxInterval);
            Org = GetQueryValue(query, _orgParameterName, Org);
            ProcessEndEvent = GetQueryValue(query, _processEndEventParameterName, ProcessEndEvent);
            ProcessEnded = GetQueryValue(query, _processEndedParameterName, ProcessEnded);
            ProcessIsComplete = GetQueryValue(query, _processIsCompleteParameterName, ProcessIsComplete);
            SearchString = GetQueryValue(query, _searchStringParameterName, SearchString);
            SetInstanceOwnerPartyId(query);
            Size = GetQueryValue(query, _sizeParameterName, Size);
            SortBy = GetQueryValue(query, _sortByParameterName, SortBy);
            VisibleAfter = GetQueryValue(query, _visibleAfterParameterName, VisibleAfter);
        }

        /// <summary>
        /// Builds a query string with one replaced parameter.
        /// </summary>
        /// <param name="queryParamName">The name of the query parameter to be replaced.</param>
        /// <param name="newParamValue">The new value for the specified query parameter.</param>
        /// <returns>A query string with the specified parameter replaced.</returns>
        public string BuildQueryString(string queryParamName, string newParamValue)
        {
            var properties = GetType().GetProperties().Where(prop => prop.CanRead && prop.GetValue(this) != null).SelectMany(prop =>
            {
                var value = prop.GetValue(this);
                if (value is IEnumerable<string> enumerable)
                {
                    return enumerable.Select(v => new KeyValuePair<string, string>(prop.Name, v));
                }

                return [new KeyValuePair<string, string>(prop.Name, value.ToString())];
            }).ToList();

            properties.RemoveAll(x => x.Key == queryParamName);

            var queryBuilder = new QueryBuilder(properties)
            {
                { queryParamName, newParamValue }
            };

            return queryBuilder.ToQueryString().Value;
        }

        /// <summary>
        /// Generates the PostgreSQL parameters from the query parameters
        /// </summary>
        /// <returns>Dictionary with PostgreSQL parameters</returns>
        public Dictionary<string, object> GeneratePostgreSQLParameters()
        {
            Dictionary<string, object> postgresParams = new Dictionary<string, object>();

            if (InstanceOwnerPartyId != null)
            {
                postgresParams.Add(GetPgParamName(_instanceOwnerPartyIdParameterName), InstanceOwnerPartyId);
            }
            else if (InstanceOwnerPartyIdList?.Count > 0)
            {
                postgresParams.Add(GetPgParamName(_instanceOwnerPartyIdParameterName), InstanceOwnerPartyIdList.ToArray());
            }

            if (AppId != null)
            {
                postgresParams.Add(GetPgParamName(_appIdParameterName), AppId);
            }
            else if (AppIds != null)
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

            if (!string.IsNullOrEmpty(CurrentTaskId))
            {
                postgresParams.Add(GetPgParamName(_currentTaskParameterName), CurrentTaskId);
            }

            if (!string.IsNullOrEmpty(SearchString))
            {
                postgresParams.Add("_search_string", $"%{SearchString}%");
            }

            if (!string.IsNullOrEmpty(ArchiveReference))
            {
                postgresParams.Add(GetPgParamName(_archiveReferenceParameterName), ArchiveReference.ToLower());
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
                postgresParams.Add("_sort_ascending", !SortBy.StartsWith("desc:", StringComparison.OrdinalIgnoreCase));
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
        /// Converts a query parameter name to a PostgreSQL parameter name.
        /// </summary>
        /// <param name="queryParameter">The query parameter name.</param>
        /// <returns>The PostgreSQL parameter name.</returns>
        private static string GetPgParamName(string queryParameter)
        {
            return "_" + queryParameter.Replace(".", "_");
        }

        /// <summary>
        /// Gets an array of exclude confirmed by values from the query values.
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
        /// Gets an integer value from the query collection.
        /// </summary>
        /// <param name="query">The collection of query parameters.</param>
        /// <param name="key">The key to look for in the query collection.</param>
        /// <param name="currentValue">The current value to return if the key is not found or invalid.</param>
        /// <returns>An integer value from the query collection if found and valid; otherwise, the current value.</returns>
        private static int? GetQueryValue(IQueryCollection query, string key, int? currentValue)
        {
            return currentValue == null && query.TryGetValue(key, out StringValues value) && int.TryParse(value.LastOrDefault(), out int result)
                ? result
                : currentValue;
        }

        /// <summary>
        /// Gets a boolean value from the query collection.
        /// </summary>
        /// <param name="query">The collection of query parameters.</param>
        /// <param name="key">The key to look for in the query collection.</param>
        /// <param name="currentValue">The current value to return if the key is not found or invalid.</param>
        /// <returns>A boolean value from the query collection if found and valid; otherwise, the current value.</returns>
        private static bool? GetQueryValue(IQueryCollection query, string key, bool? currentValue)
        {
            return currentValue == null && query.TryGetValue(key, out StringValues value) && bool.TryParse(value.LastOrDefault(), out bool result)
                ? result
                : currentValue;
        }

        /// <summary>
        /// Gets a string value from the query collection.
        /// </summary>
        /// <param name="query">The collection of query parameters.</param>
        /// <param name="key">The key to look for in the query collection.</param>
        /// <param name="currentValue">The current value to return if the key is not found or invalid.</param>
        /// <returns>A string value from the query collection if found; otherwise, the current value.</returns>
        private static string GetQueryValue(IQueryCollection query, string key, string currentValue)
        {
            return string.IsNullOrEmpty(currentValue) && query.TryGetValue(key, out StringValues value) ? value.LastOrDefault() : currentValue;
        }

        /// <summary>
        /// Gets an array of string values from the query collection.
        /// </summary>
        /// <param name="query">The collection of query parameters.</param>
        /// <param name="key">The key to look for in the query collection.</param>
        /// <param name="currentValue">The current array of values to return if the key is not found or invalid.</param>
        /// <returns>An array of values from the query collection if found; otherwise, the current value.</returns>
        private static string[] GetQueryValue(IQueryCollection query, string key, string[] currentValue)
        {
            return currentValue == null && query.TryGetValue(key, out StringValues value) ? [.. value] : currentValue;
        }

        /// <summary>
        /// Sets the instance owner party identifier(s) from the query collection.
        /// </summary>
        /// <param name="query">The query collection.</param>
        private void SetInstanceOwnerPartyId(IQueryCollection query)
        {
            if (query.TryGetValue(_instanceOwnerPartyIdParameterName, out StringValues instanceOwnerPartyId))
            {
                if (InstanceOwnerPartyId == null && instanceOwnerPartyId.Count == 1)
                {
                    InstanceOwnerPartyId = int.TryParse(instanceOwnerPartyId.LastOrDefault(), out int partyId) ? partyId : null;
                    InstanceOwnerPartyIdList = null;
                }
                else if (InstanceOwnerPartyIdList == null && instanceOwnerPartyId.Count > 1)
                {
                    InstanceOwnerPartyIdList = instanceOwnerPartyId.Select(id => int.TryParse(id, out int partyId) ? (int?)partyId : null).ToList();
                    InstanceOwnerPartyId = null;
                }
            }
        }
    }
}
