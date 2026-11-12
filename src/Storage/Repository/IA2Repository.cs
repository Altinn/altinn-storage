#nullable disable

using System.Collections.Generic;
using System.Threading.Tasks;

namespace Altinn.Platform.Storage.Repository;

/// <summary>
/// Interface to talk to the a2 repository
/// </summary>
public interface IA2Repository
{
    /// <summary>
    /// Get the stylesheets for a data element (a2 sub/main form)
    /// </summary>
    /// <returns>A list of stylesheets</returns>
    Task<List<(string Xsl, bool IsPortrait)>> GetXsls(
        string org,
        string app,
        int lformId,
        string language,
        int xslType
    );

    /// <summary>
    /// Get an a2 codelist
    /// </summary>
    /// <returns>Codelist</returns>
    Task<string> GetCodelist(string name, string preferredLanguage);

    /// <summary>
    /// Get an a2 image
    /// </summary>
    /// <returns>Image</returns>
    Task<byte[]> GetImage(string name);
}
