#nullable disable

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Altinn.Platform.Storage.Repository;

/// <summary>
/// The System.Text.Json options every jsonb column is written and read with.
/// </summary>
/// <remarks>
/// The persisted document shape is a contract with the SQL functions, which address keys such as
/// <c>Status->>'ReadStatus'</c> directly, and with every row already stored. The options are therefore
/// pinned here instead of being left to whatever the framework or Npgsql considers the default, and
/// the shape they produce is held still by the <c>PersistedJsonContractTests</c> snapshots. Attributes
/// on the models take precedence over these options, so a System.Text.Json attribute added to a model
/// that is persisted changes the stored shape regardless of what is configured here.
/// </remarks>
public static class PersistedJson
{
    /// <summary>
    /// Options producing the persisted shape: PascalCase property names, nulls written, empty
    /// collections written, enums as numbers unless the model declares a converter, and unknown
    /// properties skipped when reading.
    /// </summary>
    public static readonly JsonSerializerOptions Options = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        JsonSerializerOptions options = new()
        {
            PropertyNamingPolicy = null,
            DictionaryKeyPolicy = null,
            PropertyNameCaseInsensitive = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
            IgnoreReadOnlyProperties = false,
            IncludeFields = false,
            NumberHandling = JsonNumberHandling.Strict,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip,
            WriteIndented = false,
        };
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }
}
