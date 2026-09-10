using System;
using System.Runtime.CompilerServices;

namespace Altinn.Platform.Storage.UnitTest;

/// <summary>
/// Process wide setup applied before any test in the assembly runs.
/// </summary>
internal static class TestSetup
{
    private const string _reloadKey = "ReloadConfigurationOnChange";

    /// <summary>
    /// A test run builds hundreds of hosts in one process and never rewrites a configuration file,
    /// so configuration reloading is turned off. It has to be an environment variable because the
    /// value is read while the host builds its configuration, before appsettings.unittest.json is
    /// merged in.
    /// </summary>
    [ModuleInitializer]
    internal static void Initialize()
    {
        // Not overwritten if set, so the watching can be turned back on from the shell.
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(_reloadKey)))
        {
            Environment.SetEnvironmentVariable(_reloadKey, "false");
        }
    }
}
