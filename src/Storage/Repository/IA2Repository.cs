using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

using Altinn.Platform.Storage.Interface.Models;

namespace Altinn.Platform.Storage.Repository
{
    /// <summary>
    /// Interface to talk to the a2 repository
    /// </summary>
    public interface IA2Repository
    {
        /// <summary>
        /// Get the stylesheets for a data element (a2 sub/main form)
        /// </summary>
        /// <returns>A list of stylesheets</returns>
        Task<List<string>> GetXsls(string org, string app, int lformId, string language);

        /// <summary>
        /// Insert a stylesheet for a data element (a2 sub/main form page)
        /// </summary>
        Task CreateXsl(string org, string app, int lformId, string language, int pageNumber, string xsl);

        /// <summary>
        /// Insert an a2 codelist
        /// </summary>
        Task CreateCodelist(string name, string language, int version, string codelist);

        /// <summary>
        /// Insert an a2 image
        /// </summary>
        Task CreateImage(string name, byte[] image);

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
}
