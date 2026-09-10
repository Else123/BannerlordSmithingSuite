using SmithingSuite.Integration;
using SmithingSuite.UI;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace SmithingSuite
{
    /// <summary>
    /// Module entry point. Registers <see cref="SmithingBehavior"/> (the town-menu smithing run)
    /// and <see cref="TravelStaminaBehavior"/> (Slice 6's hourly travel-recovery balance change)
    /// for campaign games. MCM settings (<see cref="SmithingSuiteMcmSettings"/>) need no explicit
    /// registration here - <c>AttributeGlobalSettings</c> self-registers - and are bridged into
    /// <see cref="SmithingSuiteConfig"/> on each town-menu use, not here.
    /// </summary>
    public class SmithingSuiteSubModule : MBSubModuleBase
    {
        protected override void OnSubModuleLoad()
        {
            base.OnSubModuleLoad();

            // UIExtenderEx registration is pure reflection + flag flips, safe this early (SPEC-
            // SLICE4-5.md Slice 5, TODO 11) - guarded internally so a missing/incompatible
            // UIExtenderEx cannot stop the rest of the mod from loading (AC15).
            UiExtensionBridge.Register();
        }

        protected override void InitializeGameStarter(Game game, IGameStarter gameStarterObject)
        {
            if (game.GameType is Campaign)
            {
                CampaignGameStarter campaignGameStarter = (CampaignGameStarter)gameStarterObject;
                campaignGameStarter.AddBehavior(new SmithingBehavior(new SmithingConnector()));
                campaignGameStarter.AddBehavior(new TravelStaminaBehavior());
            }
        }
    }
}
