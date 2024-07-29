using System.IO;
using System.Text;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Models;

namespace Altinn.Platform.Storage.Services
{
    /// <summary>
    /// This interface describes the required methods and features of a application service implementation.
    /// </summary>
    public interface IA2OndemandFormattingService
    {
        /// <summary>
        /// Get html
        /// </summary>
        /// <param name="printXslList">printXslList</param>
        /// <param name="xmlData">xmlData</param>
        /// <param name="archiveStamp">archiveStamp</param>
        /// <param name="languageID">languageID</param>
        /// <returns>Html as stream</returns>
        Stream GetHTML(PrintViewXslBEList printXslList, Stream xmlData, string archiveStamp, int languageID);
    }
}
