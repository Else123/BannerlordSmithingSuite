using System;
using Helpers;
using SmithingSuite.Logic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.ComponentInterfaces;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Localization;

namespace SmithingSuite.Integration
{
    /// <summary>
    /// Owns the "Automatic smithing" town menu option. Mirrors the AutoTrader's town-menu
    /// pattern: registers on <see cref="CampaignEvents.OnSessionLaunchedEvent"/>, gates the
    /// option through <see cref="SettlementAccessModel"/>, then on trigger delegates the
    /// two-phase run to <see cref="SmithingRun"/> (engine-free, Refactor A) and reports the
    /// resulting <see cref="RunReport"/>. Contains no smelt/refine orchestration or TaleWorlds
    /// smithing logic of its own anymore - that lives in the connector and the engine-free
    /// planner/orchestration layer.
    ///
    /// Diagnostics: every run also writes a detailed trace via <see cref="SmithingLogTrace"/> (an
    /// <see cref="ISmithingTrace"/> implementation backed by <see cref="SmithingLog"/>) so a
    /// tester can verify every acceptance criterion from one town-menu click without attaching a
    /// debugger. Per-action execution detail (success/failure and why) is logged by
    /// <see cref="SmithingConnector"/> itself, since it is the only layer that knows the reason a
    /// specific action failed.
    /// </summary>
    public class SmithingBehavior : CampaignBehaviorBase
    {
        private readonly ISmithingConnector _connector;
        private readonly ISmithingTrace _trace = new SmithingLogTrace();

        public SmithingBehavior(ISmithingConnector connector)
        {
            _connector = connector;
        }

        public override void RegisterEvents()
        {
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnGameLoaded);
        }

        public override void SyncData(IDataStore dataStore)
        {
            // Slice 1 persists nothing: the plan is recomputed fresh from live state every time
            // the option is triggered.
        }

        private void OnGameLoaded(CampaignGameStarter campaignGameStarter)
        {
            // Bridged once here too (code review Slice 6 finding A): this is the only listener
            // guaranteed to run at the start of every session, so a fresh session starts with the
            // player's saved MCM settings even if the player never opens the town menu that
            // session before some other path (travel stamina, the crafting-screen advisor) reads
            // SmithingSuiteConfig/ScoreWeights. Safe no-op if MCM has not registered the page yet.
            SmithingSuiteMcmSettings.Apply();

            if (campaignGameStarter == null)
            {
                return;
            }

            // Position resolved by lookup, never an absolute index: PlayerTownVisitCampaignBehavior
            // has already populated "town" with ~13 vanilla options by the time this listener runs,
            // and AddOption is a raw list insert - an absolute index (the old "4") drifts the moment
            // vanilla's own registration order changes. Anchored on vanilla's own "town_smithy"
            // ("Enter smithy") option (verified by decompiling PlayerTownVisitCampaignBehavior) so
            // ours lands directly after it; falls back to -1 (append before Leave) if that anchor
            // cannot be found. Text is passed RAW (with the "{=id}" marker), never .ToString()'d -
            // CampaignGameStarter.AddGameMenuOption wraps it in its own TextObject internally, and a
            // pre-resolved string would freeze the language at registration time.
            int index = FindIndexAfterOption(campaignGameStarter, "town", "town_smithy");
            campaignGameStarter.AddGameMenuOption("town", "smithingsuite_town",
                "{=SmithingSuite_MenuOption}Smelt and refine at the smithy",
                new GameMenuOption.OnConditionDelegate(SmithingTownCondition),
                new GameMenuOption.OnConsequenceDelegate(SmithingTownConsequence), false, index, false);
        }

        // Returns the position directly after the option whose IdString is anchorOptionId, or -1
        // if no such option exists in the menu yet (caller then appends before Leave, same as
        // vanilla's own -1 convention - see AddGameMenuOption). GetPresumedGameMenu and
        // GameMenu.MenuOptions are both public TaleWorlds API (verified by decompiling
        // CampaignGameStarter/GameMenu), so this needs no reflection.
        private static int FindIndexAfterOption(CampaignGameStarter campaignGameStarter, string menuId, string anchorOptionId)
        {
            // Guarded rather than trusted: this runs inside the OnSessionLaunched listener, so an
            // escaping NullReferenceException would break session launch itself, not merely this
            // one menu option.
            GameMenu menu = campaignGameStarter.GetPresumedGameMenu(menuId);
            if (menu != null)
            {
                int position = 0;
                foreach (GameMenuOption option in menu.MenuOptions)
                {
                    if (option.IdString == anchorOptionId)
                    {
                        return position + 1;
                    }

                    position++;
                }
            }

            // Falling back is safe but it silently moves the option, and the placement is the one
            // thing about this feature a player notices before using it. Say which path was taken
            // so a "why is it in the wrong place" report is answerable from the log alone. Flushed
            // because no run is open yet at session launch.
            if (SmithingLog.IsVerbose)
            {
                SmithingLog.Line("SmithingBehavior: menu anchor '" + anchorOptionId + "' not found in menu '"
                    + menuId + "' - appending the option instead of placing it after the anchor");
                SmithingLog.Flush();
            }

            return -1;
        }

        // Craft (not Trade) is the settlement action that actually gates smithy access - it is
        // disabled, for example, while a town is under siege, matching what the smithing menu
        // itself requires.
        private bool SmithingTownCondition(MenuCallbackArgs args)
        {
            bool shouldBeDisabled;
            TextObject disabledText;
            bool canPlayerDo = Campaign.Current.Models.SettlementAccessModel.CanMainHeroDoSettlementAction(
                Settlement.CurrentSettlement, SettlementAccessModel.SettlementAction.Craft,
                out shouldBeDisabled, out disabledText);
            args.optionLeaveType = GameMenuOption.LeaveType.Craft;
            return MenuHelper.SetOptionProperties(args, canPlayerDo, shouldBeDisabled, disabledText);
        }

        private void SmithingTownConsequence(MenuCallbackArgs args)
        {
            try
            {
                // Bridged on each use rather than cached, exactly like the AutoTrader does: a
                // value the player changes in MCM mid-session must take effect on the very next
                // run without a restart (SPEC-SLICE6.md AC10). Safe no-op if MCM has not
                // registered the page yet. Inside the try/finally (code review Slice 6 finding
                // E.2) so a bridging exception cannot escape the menu callback unguarded, and so
                // SmithingLog.EndRun below still flushes whatever this run managed to log.
                SmithingSuiteMcmSettings.Apply();

                string settlementName = Settlement.CurrentSettlement != null && Settlement.CurrentSettlement.Name != null
                    ? Settlement.CurrentSettlement.Name.ToString()
                    : "?";
                SmithingLog.BeginRun(settlementName);

                // Read here (not inside SmithingRun) because SmithingRun is engine-free and must
                // not reference SmithingSuiteConfig - see SmithingRun.Execute's doc.
                int smeltSharePercent = SmithingSuiteConfig.SmeltStaminaSharePercent;
                RunReport report = SmithingRun.Execute(_connector, smeltSharePercent, _trace);

                if (report.NothingHappened)
                {
                    _connector.Report(NothingToDoMessage());
                    return;
                }

                ReportSummary(report);
            }
            catch (Exception ex)
            {
                // This is the primary feature's only entry point, invoked directly from the
                // game's menu callback - every other entry point in this mod is deliberately
                // fail-safe (the advisor mixin, TravelStaminaBehavior, EngineDesignEvaluator,
                // SmithingLog itself), so an unhandled exception here would be the one place an
                // exception escapes into a crash dialog instead of degrading gracefully. Log the
                // detail for diagnosis, then tell the player honestly that the run failed rather
                // than silently doing nothing or letting the game crash.
                SmithingLog.Line("SmithingTownConsequence: run failed with an unexpected exception - " + ex);
                _connector.Report(RunFailedMessage());
            }
            finally
            {
                // Flushes this run's buffered trace to disk exactly once. try/finally so the
                // early "nothing to do" return above - and any exception - still flushes instead
                // of silently losing this run's lines (see SmithingLog.EndRun).
                SmithingLog.EndRun();
            }
        }

        // One green, full-sentence message PER HERO (vanilla's own convention for a positive
        // completion - see e.g. trade/recruit outcomes), instead of one \n-packed block with a
        // header - vanilla never builds a message that way. Stamina spent is derived from a
        // before/after snapshot pair spanning BOTH phases, not from the planned smelt/refine cost -
        // see RunReport.From, which computed staminaSpent for each entry the same way.
        private void ReportSummary(RunReport report)
        {
            foreach (RunReportEntry entry in report.Entries)
            {
                _connector.Report(HeroSummaryMessage(entry), ReportTone.Positive);
            }
        }

        private static string HeroSummaryMessage(RunReportEntry entry)
        {
            return new TextObject(
                    "{=SmithingSuite_HeroSummary}{HERO} smelted {SMELTED} {?(SMELTED > 1)}items{?}item{\\?} and " +
                    "refined {REFINED} {?(REFINED > 1)}items{?}item{\\?}, spending {STAMINA} stamina.",
                    null)
                .SetTextVariable("HERO", entry.HeroName)
                .SetTextVariable("SMELTED", entry.Smelted)
                .SetTextVariable("REFINED", entry.Refined)
                .SetTextVariable("STAMINA", entry.StaminaSpent)
                .ToString();
        }

        private static string NothingToDoMessage()
        {
            return new TextObject(
                "{=SmithingSuite_NothingToDo}Nobody in your party has the stamina or materials to smith right now.",
                null).ToString();
        }

        private static string RunFailedMessage()
        {
            return new TextObject("{=SmithingSuite_RunFailed}Smithing was interrupted.", null).ToString();
        }
    }
}
