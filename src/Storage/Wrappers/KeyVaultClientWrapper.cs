using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;

using Azure.Identity;
using Azure.Security.KeyVault.Secrets;

namespace Altinn.Platform.Storage.Wrappers
{
    /// <summary>
    /// Wrapper implementation for a KeyVaultClient. The wrapped client is created with a principal obtained through configuration.
    /// </summary>
    /// <remarks>This class is excluded from code coverage because it has no logic to be tested.</remarks>
    [ExcludeFromCodeCoverage]
    public class KeyVaultClientWrapper : IKeyVaultClientWrapper
    {
        /// <inheritdoc/>
        public async Task<string> GetSecretAsync(string vaultUri, string secretId)
        {
            // Credentials are set based on environment variables set in Program.cs
            SecretClient secretClient = new(new Uri(vaultUri), new DefaultAzureCredential());

            var secret = await secretClient.GetSecretAsync(secretId);

            return secret.Value.ToString();
        }
    }
}