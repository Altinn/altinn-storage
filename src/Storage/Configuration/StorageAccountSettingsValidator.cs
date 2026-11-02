using Microsoft.Extensions.Options;

namespace Altinn.Platform.Storage.Configuration;

/// <summary>
/// Validates <see cref="StorageAccountSettings"/> when the application starts.
/// </summary>
public sealed class StorageAccountSettingsValidator : IValidateOptions<StorageAccountSettings>
{
    /// <inheritdoc/>
    public ValidateOptionsResult Validate(string? name, StorageAccountSettings options)
    {
        return options.CacheSeconds > 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(
                $"StorageAccounts:CacheSeconds must be greater than zero, but is {options.CacheSeconds}."
            );
    }
}
