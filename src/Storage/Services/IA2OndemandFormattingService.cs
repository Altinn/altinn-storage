using System.IO;
using System.Text;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Models;

namespace Altinn.Platform.Storage.Services
{
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
        /// <param name="archiveStamp">Timestamp used for water mark</param>
        /// <returns>Html as stream</returns>
        Stream GetFormdataHtml(PrintViewXslBEList printXslList, Stream xmlData, string archiveStamp);
    }
}
