using System.Globalization;
using System.Runtime.CompilerServices;

namespace TravelPlanner.IntegrationTests;

/// <summary>Pins the default culture so assertions on English texts don't depend on the machine's locale.</summary>
internal static class CultureSetup
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.GetCultureInfo("en-GB");
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo("en-GB");
    }
}
