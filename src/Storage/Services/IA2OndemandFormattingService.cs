#nullable disable

using System.IO;

namespace Altinn.Platform.Storage.Services;

/// <summary>
/// This interface describes the required methods for getting ondemand content
/// </summary>
public interface IA2OndemandFormattingService
{
    /// <summary>
    /// Get html
    /// </summary>
    /// <param name="printXslList">printXslList</param>
    /// <param name="xmlData">xmlData</param>
    /// <returns>Html as string</returns>
    string GetFormdataHtml(PrintViewXslBEList printXslList, Stream xmlData);
}
