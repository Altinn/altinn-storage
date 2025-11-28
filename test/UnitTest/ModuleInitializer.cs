using System.Runtime.CompilerServices;
using VerifyTests;

namespace Altinn.Platform.Storage.UnitTest;

public static class ModuleInitializer
{
    [ModuleInitializer]
    public static void Initialize() => VerifierSettings.AutoVerify(includeBuildServer: false);
}
