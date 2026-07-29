using System;
using System.Collections.Generic;
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

            campaignGameStarter.AddGameMenuOption("town", "smithingsuite_town",
                new TextObject("{=SSAutoSmith}Automatic smithing", null).ToString(),
                new GameMenuOption.OnConditionDelegate(SmithingTownCondition),
                new GameMenuOption.OnConsequenceDelegate(SmithingTownConsequence), false, 4, false);
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
                SmithingLog.Screen("Smithing Suite: running at " + settlementName + "...");

                // Read here (not inside SmithingRun) because SmithingRun is engine-free and must
                // not reference SmithingSuiteConfig - see SmithingRun.Execute's doc.
                int smeltSharePercent = SmithingSuiteConfig.SmeltStaminaSharePercent;
                RunReport report = SmithingRun.Execute(_connector, smeltSharePercent, _trace);

                if (report.NothingHappened)
                {
                    _connector.Report(NothingToDoMessage());
                    return;
                }

                _connector.Report(BuildSummary(report));
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

        // Stamina spent is derived from a before/after snapshot pair spanning BOTH phases, not
        // from the planned smelt/refine cost - see RunReport.From, which computed staminaSpent
        // for each entry the same way.
        private static string BuildSummary(RunReport report)
        {
            var lines = new List<string>();
            foreach (RunReportEntry entry in report.Entries)
            {
                lines.Add(new TextObject(
                        "{=SSSmithSummaryLine}{HERO}: {SMELTED} smelted, {REFINED} refined, {STAMINA} stamina spent",
                        null)
                    .SetTextVariable("HERO", entry.HeroName)
                    .SetTextVariable("SMELTED", entry.Smelted)
                    .SetTextVariable("REFINED", entry.Refined)
                    .SetTextVariable("STAMINA", entry.StaminaSpent)
                    .ToString());
            }

            if (lines.Count == 0)
            {
                return NothingToDoMessage();
            }

            string header = new TextObject("{=SSSmeltSummaryHeader}Automatic smithing:", null).ToString();
            return header + "\n" + string.Join("\n", lines);
        }

        private static string NothingToDoMessage()
        {
            return new TextObject(
                "{=SSNothingToSmelt}Nothing to smelt: every smith is out of crafting stamina, or there are no unlocked smeltable items.",
                null).ToString();
        }

        private static string RunFailedMessage()
        {
            return new TextObject(
                "{=SSRunFailed}Automatic smithing failed unexpectedly and was stopped. See smithingsuite.log for details.",
                null).ToString();
        }
    }
}
