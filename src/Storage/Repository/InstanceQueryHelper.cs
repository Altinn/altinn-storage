using System;
using System.Collections.Generic;
using System.Linq;
using Altinn.Platform.Storage.Helpers;
using Altinn.Platform.Storage.Interface.Models;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Primitives;

namespace Altinn.Platform.Storage.Repository
{
    /// <summary>
    /// Helper methods for instance query
    /// </summary>
    public static class InstanceQueryHelper
    {
        /// <summary>
        /// Build query from parameters
        /// </summary>
        /// <param name="queryParams">queryParams</param>
        /// <param name="queryBuilder">queryBuilder</param>
        /// <param name="options">options</param>
        /// <returns>The instances</returns>
        internal static IQueryable<Instance> BuildQueryFromParameters(Dictionary<string, StringValues> queryParams, IQueryable<Instance> queryBuilder, QueryRequestOptions options)
        {
            foreach (KeyValuePair<string, StringValues> param in queryParams)
            {
                string queryParameter = param.Key;
                StringValues queryValues = param.Value;

                if (queryParameter.Equals("appId"))
                {
                    queryBuilder = queryBuilder.Where(i => queryValues.Contains(i.AppId));
                    continue;
                }

                if (queryParameter.Equals("instanceOwner.partyId"))
                {
                    if (queryValues.Count == 1)
                    {
                        var partyId = queryValues.First();
                        options.PartitionKey = new PartitionKey(partyId);
                        queryBuilder = queryBuilder.Where(i => partyId == i.InstanceOwner.PartyId);
                    }
                    else
                    {
                        queryBuilder = queryBuilder.Where(i => queryValues.Contains(i.InstanceOwner.PartyId));
                    }

                    continue;
                }

                foreach (string queryValue in queryValues)
                {
                    switch (queryParameter)
                    {
                        case "size":
                        case "continuationToken":
                            // handled outside this method, it is a valid parameter.
                            break;
                        case "org":
                            queryBuilder = queryBuilder.Where(i => i.Org == queryValue);
                            break;
                        case "lastChanged":
                            queryBuilder = QueryBuilderForLastChangedDateTime(queryBuilder, queryValue);
                            break;

                        case "dueBefore":
                            queryBuilder = QueryBuilderForDueBefore(queryBuilder, queryValue);
                            break;

                        case "visibleAfter":
                            queryBuilder = QueryBuilderForVisibleAfter(queryBuilder, queryValue);
                            break;

                        case "created":
                            queryBuilder = QueryBuilderForCreated(queryBuilder, queryValue);
                            break;

                        case "process.currentTask":
                            queryBuilder = queryBuilder.Where(i => i.Process.CurrentTask.ElementId == queryValue);
                            break;

                        case "process.isComplete":
                            bool isComplete = bool.Parse(queryValue);
                            if (isComplete)
                            {
                                queryBuilder = queryBuilder.Where(i => i.Process.Ended != null);
                            }
                            else
                            {
                                queryBuilder = queryBuilder.Where(i => i.Process.CurrentTask != null);
                            }

                            break;

                        case "process.ended":
                            queryBuilder = QueryBuilderForEnded(queryBuilder, queryValue);
                            break;

                        case "excludeConfirmedBy":
                            queryBuilder = QueryBuilderExcludeConfirmedBy(queryBuilder, queryValue);
                            break;
                        case "language":
                            break;
                        case "status.isArchived":
                            bool isArchived = bool.Parse(queryValue);
                            queryBuilder = queryBuilder.Where(i => i.Status.IsArchived == isArchived);

                            break;
                        case "status.isSoftDeleted":
                            bool isSoftDeleted = bool.Parse(queryValue);
                            queryBuilder = queryBuilder.Where(i => i.Status.IsSoftDeleted == isSoftDeleted);

                            break;
                        case "status.isHardDeleted":
                            bool isHardDeleted = bool.Parse(queryValue);
                            queryBuilder = queryBuilder.Where(i => i.Status.IsHardDeleted == isHardDeleted);

                            break;
                        case "status.isArchivedOrSoftDeleted":
                            if (bool.Parse(queryValue))
                            {
                                queryBuilder = queryBuilder.Where(i => i.Status.IsArchived || i.Status.IsSoftDeleted);
                            }

                            break;
                        case "status.isActiveOrSoftDeleted":
                            if (bool.Parse(queryValue))
                            {
                                queryBuilder = queryBuilder.Where(i => !i.Status.IsArchived || i.Status.IsSoftDeleted);
                            }

                            break;
                        case "sortBy":
                            queryBuilder = QueryBuilderForSortBy(queryBuilder, queryValue);

                            break;
                        case "archiveReference":
                            queryBuilder = queryBuilder.Where(i => i.Id.EndsWith(queryValue.ToLower()));

                            break;
                        default:
                            throw new ArgumentException($"Unknown query parameter: {queryParameter}");
                    }
                }
            }

            return queryBuilder;
        }

        /// <summary>
        /// Convert timestamp based query params predicate to postgres predicate
        /// </summary>
        /// <param name="timestampKey">Key from query</param>
        /// <param name="queryParams">The query params</param>
        /// <param name="parameterNr">Positional parameter nr i postqres query</param>
        /// <param name="timestampColumn">Postgres column name if different from timestampkey</param>
        /// <returns>Postgres predicate and datetime object to use in postgres api</returns>
        internal static (string Predicate, DateTime TimeStamp) ConvertTimestampParameter(string timestampKey, Dictionary<string, StringValues> queryParams, int parameterNr, string timestampColumn = null)
        {
            if (!queryParams.ContainsKey(timestampKey))
            {
                return (null, DateTime.MinValue);
            }

            timestampColumn ??= timestampKey;
            string timestampValue = queryParams[timestampKey].First();
            string @operator = timestampValue.Split(':')[0] switch
            {
                "gt" => ">",
                "gte" => ">=",
                "lt" => "<",
                "lte" => "<=",
                "eq" => "=",
                _ => throw new Exception("Unexpeted parameter " + timestampValue),
            };
            return ($" AND {timestampColumn} {@operator} ${parameterNr}", ParseDateTimeIntoUtc(timestampValue[(timestampValue.Split(':')[0].Length + 1)..]));
        }

        // Limitations in queryBuilder.Where interface forces me to duplicate the datetime methods
        private static IQueryable<Instance> QueryBuilderForDueBefore(IQueryable<Instance> queryBuilder, string queryValue)
        {
            DateTime dateValue;

            if (queryValue.StartsWith("gt:"))
            {
                dateValue = ParseDateTimeIntoUtc(queryValue[3..]);
                return queryBuilder.Where(i => i.DueBefore > dateValue);
            }

            if (queryValue.StartsWith("gte:"))
            {
                dateValue = ParseDateTimeIntoUtc(queryValue[4..]);
                return queryBuilder.Where(i => i.DueBefore >= dateValue);
            }

            if (queryValue.StartsWith("lt:"))
            {
                dateValue = ParseDateTimeIntoUtc(queryValue[3..]);
                return queryBuilder.Where(i => i.DueBefore < dateValue);
            }

            if (queryValue.StartsWith("lte:"))
            {
                dateValue = ParseDateTimeIntoUtc(queryValue[4..]);
                return queryBuilder.Where(i => i.DueBefore <= dateValue);
            }

            if (queryValue.StartsWith("eq:"))
            {
                dateValue = ParseDateTimeIntoUtc(queryValue[3..]);
                return queryBuilder.Where(i => i.DueBefore == dateValue);
            }

            dateValue = ParseDateTimeIntoUtc(queryValue);
            return queryBuilder.Where(i => i.DueBefore == dateValue);
        }

        // Limitations in queryBuilder.Where interface forces me to duplicate the datetime methods
        private static IQueryable<Instance> QueryBuilderForLastChangedDateTime(IQueryable<Instance> queryBuilder, string queryValue)
        {
            DateTime dateValue;

            if (queryValue.StartsWith("gt:"))
            {
                dateValue = ParseDateTimeIntoUtc(queryValue[3..]);
                return queryBuilder.Where(i => i.LastChanged > dateValue);
            }

            if (queryValue.StartsWith("gte:"))
            {
                dateValue = ParseDateTimeIntoUtc(queryValue[4..]);
                return queryBuilder.Where(i => i.LastChanged >= dateValue);
            }

            if (queryValue.StartsWith("lt:"))
            {
                dateValue = ParseDateTimeIntoUtc(queryValue[3..]);
                return queryBuilder.Where(i => i.LastChanged < dateValue);
            }

            if (queryValue.StartsWith("lte:"))
            {
                dateValue = ParseDateTimeIntoUtc(queryValue[4..]);
                return queryBuilder.Where(i => i.LastChanged <= dateValue);
            }

            if (queryValue.StartsWith("eq:"))
            {
                dateValue = ParseDateTimeIntoUtc(queryValue[3..]);
                return queryBuilder.Where(i => i.LastChanged == dateValue);
            }

            dateValue = ParseDateTimeIntoUtc(queryValue);
            return queryBuilder.Where(i => i.LastChanged == dateValue);
        }

        // Limitations in queryBuilder.Where interface forces me to duplicate the datetime methods
        private static IQueryable<Instance> QueryBuilderForEnded(IQueryable<Instance> queryBuilder, string queryValue)
        {
            DateTime dateValue;

            if (queryValue.StartsWith("gt:"))
            {
                dateValue = ParseDateTimeIntoUtc(queryValue[3..]);
                return queryBuilder.Where(i => i.Process.Ended > dateValue);
            }

            if (queryValue.StartsWith("gte:"))
            {
                dateValue = ParseDateTimeIntoUtc(queryValue[4..]);
                return queryBuilder.Where(i => i.Process.Ended >= dateValue);
            }

            if (queryValue.StartsWith("lt:"))
            {
                dateValue = ParseDateTimeIntoUtc(queryValue[3..]);
                return queryBuilder.Where(i => i.Process.Ended < dateValue);
            }

            if (queryValue.StartsWith("lte:"))
            {
                dateValue = ParseDateTimeIntoUtc(queryValue[4..]);
                return queryBuilder.Where(i => i.Process.Ended <= dateValue);
            }

            if (queryValue.StartsWith("eq:"))
            {
                dateValue = ParseDateTimeIntoUtc(queryValue[3..]);
                return queryBuilder.Where(i => i.Process.Ended == dateValue);
            }

            dateValue = ParseDateTimeIntoUtc(queryValue);
            return queryBuilder.Where(i => i.Process.Ended == dateValue);
        }

        private static IQueryable<Instance> QueryBuilderExcludeConfirmedBy(IQueryable<Instance> queryBuilder, string queryValue)
        {
            return queryBuilder.Where(i =>

                // A slightly more readable variant would be to use All( != ), but All() isn't supported.
                !i.CompleteConfirmations.Any(cc => cc.StakeholderId == queryValue));
        }

        private static IQueryable<Instance> QueryBuilderForSortBy(IQueryable<Instance> queryBuilder, string queryValue)
        {
            string[] value = queryValue.Split(':');
            string direction = value[0].ToLower();
            string property = value[1];

            if (!direction.Equals("desc") && !direction.Equals("asc"))
            {
                throw new ArgumentException($"Invalid direction for sorting: {direction}");
            }

            switch (property)
            {
                case "lastChanged":
                    if (direction.Equals("desc"))
                    {
                        queryBuilder = queryBuilder.OrderByDescending(i => i.LastChanged);
                    }
                    else
                    {
                        queryBuilder = queryBuilder.OrderBy(i => i.LastChanged);
                    }

                    break;
                default:
                    throw new ArgumentException($"Cannot sort on property: {property}");
            }

            return queryBuilder;
        }

        // Limitations in queryBuilder.Where interface forces me to duplicate the datetime methods
        private static IQueryable<Instance> QueryBuilderForCreated(IQueryable<Instance> queryBuilder, string queryValue)
        {
            DateTime dateValue;

            if (queryValue.StartsWith("gt:"))
            {
                dateValue = ParseDateTimeIntoUtc(queryValue[3..]);
                return queryBuilder.Where(i => i.Created > dateValue);
            }

            if (queryValue.StartsWith("gte:"))
            {
                dateValue = ParseDateTimeIntoUtc(queryValue[4..]);
                return queryBuilder.Where(i => i.Created >= dateValue);
            }

            if (queryValue.StartsWith("lt:"))
            {
                dateValue = ParseDateTimeIntoUtc(queryValue[3..]);
                return queryBuilder.Where(i => i.Created < dateValue);
            }

            if (queryValue.StartsWith("lte:"))
            {
                dateValue = ParseDateTimeIntoUtc(queryValue[4..]);
                return queryBuilder.Where(i => i.Created <= dateValue);
            }

            if (queryValue.StartsWith("eq:"))
            {
                dateValue = ParseDateTimeIntoUtc(queryValue[3..]);
                return queryBuilder.Where(i => i.Created == dateValue);
            }

            dateValue = ParseDateTimeIntoUtc(queryValue);
            return queryBuilder.Where(i => i.Created == dateValue);
        }

        // Limitations in queryBuilder.Where interface forces me to duplicate the datetime methods
        private static IQueryable<Instance> QueryBuilderForVisibleAfter(IQueryable<Instance> queryBuilder, string queryValue)
        {
            DateTime dateValue;

            if (queryValue.StartsWith("gt:"))
            {
                dateValue = ParseDateTimeIntoUtc(queryValue[3..]);
                return queryBuilder.Where(i => i.VisibleAfter > dateValue);
            }

            if (queryValue.StartsWith("gte:"))
            {
                dateValue = ParseDateTimeIntoUtc(queryValue[4..]);
                return queryBuilder.Where(i => i.VisibleAfter >= dateValue);
            }

            if (queryValue.StartsWith("lt:"))
            {
                dateValue = ParseDateTimeIntoUtc(queryValue[3..]);
                return queryBuilder.Where(i => i.VisibleAfter < dateValue);
            }

            if (queryValue.StartsWith("lte:"))
            {
                dateValue = ParseDateTimeIntoUtc(queryValue[4..]);
                return queryBuilder.Where(i => i.VisibleAfter <= dateValue);
            }

            if (queryValue.StartsWith("eq:"))
            {
                dateValue = ParseDateTimeIntoUtc(queryValue[3..]);
                return queryBuilder.Where(i => i.VisibleAfter == dateValue);
            }

            dateValue = ParseDateTimeIntoUtc(queryValue);
            return queryBuilder.Where(i => i.VisibleAfter == dateValue);
        }

        private static DateTime ParseDateTimeIntoUtc(string queryValue)
        {
            return DateTimeHelper.ParseAndConvertToUniversalTime(queryValue);
        }

    }
}
