using System.Collections.Generic;
using System.Threading.Tasks;

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
        Task<List<string>> GetXsls(string org, string app, int lformId, string language, int xslType);

        /// <summary>
        /// Insert a stylesheet for a data element (a2 sub/main form page)
        /// </summary>
        Task CreateXsl(string org, string app, int lformId, string language, int pageNumber, string xsl, int xslType);

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

        /// <summary>
        /// Create an a2 migration state
        /// </summary>
        Task CreateMigrationState(int a2ArchiveReference);

        /// <summary>
        /// Update an a2 migration state
        /// </summary>
        Task UpdateStartMigrationState(int a2ArchiveReference, string instanceGuid);

        /// <summary>
        /// Update an a2 migration state
        /// </summary>
        Task UpdateCompleteMigrationState(string instanceGuid);

        /// <summary>
        /// Delete an a2 migration state
        /// </summary>
        Task DeleteMigrationState(string instanceGuid);

        /// <summary>
        /// Get the instance id of the migration
        /// </summary>
        /// <returns>The instance id of the migration</returns>
        Task<string> GetMigrationInstanceId(int a2ArchiveReference);
    }
}
