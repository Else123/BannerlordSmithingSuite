using SmithingSuite.Integration;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
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
        protected override void InitializeGameStarter(Game game, IGameStarter gameStarterObject)
        {
            if (game.GameType is Campaign)
            {
                CampaignGameStarter campaignGameStarter = (CampaignGameStarter)gameStarterObject;
                campaignGameStarter.AddBehavior(new SmithingBehavior(new SmithingConnector()));
                campaignGameStarter.AddBehavior(new TravelStaminaBehavior());
            }
        }

        public override void OnGameInitializationFinished(Game game)
        {
            base.OnGameInitializationFinished(game);

            InformationManager.DisplayMessage(new InformationMessage(
                new TextObject("{=SSWelcome}Thanks for using Smithing Suite! Open a town menu for automatic smithing.", null).ToString()));
        }
    }
}
