using System;
using System.Runtime.CompilerServices;
using Bannerlord.UIExtenderEx;
using SmithingSuite.Integration;

namespace SmithingSuite.UI
{
    /// <summary>
    /// Registers the vanilla-UI mixins with UIExtenderEx (SPEC-SLICE4-5.md Slice 5, TODO 11-12). All
    /// contact with that library is isolated behind a guarded, non-inlined boundary - mirroring
    /// CompanionManagementSuite's <c>UiExtensionBridge</c> - so a missing or incompatible
    /// UIExtenderEx costs only the inline crafting advisor; the rest of the suite keeps working
    /// (AC15).
    /// </summary>
    public static class UiExtensionBridge
    {
        private const string ModuleName = "SmithingSuite";

        public static void Register()
        {
            try
            {
                RegisterCore();
                SmithingLog.Line("UIExtenderEx mixins registered - crafting advisor panel active");
            }
            catch (Exception ex)
            {
                SmithingLog.Line("UIExtenderEx unavailable (" + ex.GetType().Name
                    + ") - inline crafting advisor disabled, everything else unaffected");
            }
            finally
            {
                // Runs at OnSubModuleLoad, long before any campaign (and any town-menu run) can
                // exist - the batching optimisation does not apply here, so flush immediately or
                // this line waits for a run that may be a whole session away (see
                // SmithingLog.Flush()).
                SmithingLog.Flush();
            }
        }

        // Kept out of Register() and marked non-inlinable: referencing UIExtenderEx types inside
        // Register() itself would force the JIT to resolve them the moment Register() is compiled,
        // which can happen before the try/catch around this call even runs. Isolating the reference
        // in its own never-inlined method means the type resolution happens at the call site below,
        // inside the try.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void RegisterCore()
        {
            UIExtender extender = UIExtender.Create(ModuleName);
            extender.Register(typeof(UiExtensionBridge).Assembly);
            extender.Enable();
        }
    }
}
