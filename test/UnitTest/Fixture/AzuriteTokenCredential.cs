using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Azure.Core;

namespace Altinn.Platform.Storage.UnitTest.Fixture;

/// <summary>
/// Issues bearer tokens for Azure Storage that Azurite accepts with <c>--oauth basic</c>, which checks a
/// token's audience, issuer and lifetime but not its signature.
/// </summary>
public sealed class AzuriteTokenCredential : TokenCredential
{
    private int _requestCount;

    /// <summary>
    /// Gets the number of tokens issued.
    /// </summary>
    public int RequestCount => Volatile.Read(ref _requestCount);

    /// <inheritdoc/>
    public override AccessToken GetToken(
        TokenRequestContext requestContext,
        CancellationToken cancellationToken
    )
    {
        Interlocked.Increment(ref _requestCount);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        DateTimeOffset expiresOn = now.AddHours(1);
        string header = """{"alg":"RS256","typ":"JWT"}""";
        string payload = $$"""
            {"aud":"https://storage.azure.com","iss":"https://sts.windows.net/{{Guid.Empty}}/","iat":{{now.ToUnixTimeSeconds()}},"nbf":{{now.ToUnixTimeSeconds()}},"exp":{{expiresOn.ToUnixTimeSeconds()}},"oid":"{{Guid.Empty}}","tid":"{{Guid.Empty}}"}
            """;
        return new AccessToken(
            $"{Base64UrlEncode(header)}.{Base64UrlEncode(payload)}.unsigned",
            expiresOn
        );
    }

    /// <inheritdoc/>
    public override ValueTask<AccessToken> GetTokenAsync(
        TokenRequestContext requestContext,
        CancellationToken cancellationToken
    )
    {
        return ValueTask.FromResult(GetToken(requestContext, cancellationToken));
    }

    private static string Base64UrlEncode(string value)
    {
        return Convert
            .ToBase64String(Encoding.UTF8.GetBytes(value))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}
