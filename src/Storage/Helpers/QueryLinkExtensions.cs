using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Primitives;

namespace Altinn.Platform.Storage.Helpers;

/// <summary>
/// Builds the links a <see cref="QueryResponse{T}"/> hands back to the caller.
/// </summary>
public static class QueryLinkExtensions
{
    private const string _continuationTokenParameterName = "continuationToken";

    /// <summary>
    /// Rebuilds the current request as an absolute platform URL, replacing any continuation
    /// token the caller sent with the supplied one.
    /// </summary>
    /// <param name="request">The request being answered.</param>
    /// <param name="hostname">The platform hostname the link should point at.</param>
    /// <param name="continuationToken">The token the link should continue from, or <c>null</c> to keep the caller's own.</param>
    /// <returns>An absolute URL.</returns>
    public static string BuildContinuationLink(
        this HttpRequest request,
        string hostname,
        string? continuationToken
    )
    {
        string url = request.Path;
        string host = $"https://platform.{hostname}";
        string queryString = request.QueryString.Value ?? string.Empty;

        if (string.IsNullOrEmpty(continuationToken))
        {
            return $"{host}{url}{queryString}";
        }

        Dictionary<string, StringValues> queryParams = QueryHelpers.ParseQuery(queryString);

        IEnumerable<KeyValuePair<string, string>> flattenedQueryParams = queryParams
            .SelectMany(
                x => x.Value,
                (col, value) => new KeyValuePair<string, string>(col.Key, value ?? string.Empty)
            )
            .Where(e => e.Key != _continuationTokenParameterName);

        QueryBuilder queryBuilder = new(flattenedQueryParams)
        {
            { _continuationTokenParameterName, continuationToken },
        };
        string? newQueryString = queryBuilder.ToQueryString().Value;

        return $"{host}{url}{newQueryString}";
    }
}
