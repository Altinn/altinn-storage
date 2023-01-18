namespace Altinn.Platform.Storage.Interface.Enums
{
    /// <summary>
    /// Represents different scanning results for when files are being scanned for malware.
    /// </summary>
    public enum FileScanResult
    {
        /// <summary>
        /// The scan status of the file is pending. This is the default value.
        /// </summary>
        Pending,

        /// <summary>
        /// The file scan did not find any malware in the file.
        /// </summary>
        Clean,

        /// <summary>
        /// The file scan found malware in the file.
        /// </summary>
        Infected
    }
}