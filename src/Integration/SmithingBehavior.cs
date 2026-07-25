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
    /// option through <see cref="SettlementAccessModel"/>, then on trigger builds a snapshot,
    /// asks <see cref="SmeltPlanner"/> for a plan, executes it through
    /// <see cref="ISmithingConnector"/>, and reports a summary. Contains no TaleWorlds smithing
    /// logic of its own - that lives in the connector and the engine-free planner.
    ///
    /// Diagnostics: every run also writes a detailed trace via <see cref="SmithingLog"/> (smith
    /// pools, candidates, plans, per-smith totals) so a tester can verify every acceptance
    /// criterion from one town-menu click without attaching a debugger. Per-action execution
    /// detail (success/failure and why) is logged by <see cref="SmithingConnector"/> itself,
    /// since it is the only layer that knows the reason a specific action failed.
    /// </summary>
    public class SmithingBehavior : CampaignBehaviorBase
    {
        // Matches MaterialStock's own slot count (IronOre=0 .. Charcoal=8) - duplicated here
        // only for the diagnostics log, which reads material counts through the public Get()
        // accessor rather than reaching into MaterialStock internals.
        private const int MaterialOrdinalCount = 9;

        private readonly ISmithingConnector _connector;

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
            string settlementName = Settlement.CurrentSettlement != null && Settlement.CurrentSettlement.Name != null
                ? Settlement.CurrentSettlement.Name.ToString()
                : "?";
            SmithingLog.BeginRun(settlementName);
            SmithingLog.Screen("Smithing Suite: running at " + settlementName + "...");

            IReadOnlyList<SmithSnapshot> smithsBefore = _connector.GetSmiths();
            Dictionary<string, int> smeltedCountByHero = RunSmeltPhase(smithsBefore);

            // Smelting produces the raw materials refining consumes (ore, wood -> iron chain,
            // charcoal), so it must run first, and refining must plan against a FRESH
            // post-smelt snapshot of both stamina and material stock rather than smithsBefore -
            // both have changed by the time refining plans.
            Dictionary<string, int> refinedCountByHero = RunRefinePhase();

            if (smeltedCountByHero.Count == 0 && refinedCountByHero.Count == 0)
            {
                SmithingLog.Line("Nothing to do this run.");
                _connector.Report(NothingToDoMessage());
                return;
            }

            IReadOnlyList<SmithSnapshot> smithsAfter = _connector.GetSmiths();
            LogRunSummary(smithsBefore, smithsAfter, smeltedCountByHero, refinedCountByHero);
            _connector.Report(BuildSummary(smithsBefore, smithsAfter, smeltedCountByHero, refinedCountByHero));
        }

        private Dictionary<string, int> RunSmeltPhase(IReadOnlyList<SmithSnapshot> smiths)
        {
            SmithingLog.Section("SMELT phase");
            LogSmithPool(smiths);

            IReadOnlyList<SmeltCandidate> candidates = _connector.GetSmeltCandidates();
            LogSmeltCandidates(candidates);

            SmeltPlan plan = SmeltPlanner.Plan(smiths, candidates);
            LogSmeltPlan(plan, smiths, candidates);

            Dictionary<string, int> smeltedCountByHero = ExecuteSmeltPlan(plan);
            LogSmeltedTotals(smeltedCountByHero, smiths);

            SmithingLog.Screen("Smithing Suite: smelted " + SumValues(smeltedCountByHero) + " item(s).");
            return smeltedCountByHero;
        }

        private Dictionary<string, int> RunRefinePhase()
        {
            SmithingLog.Section("REFINE phase");

            MaterialStock stockBefore = _connector.GetMaterialStock();
            LogMaterialStock("Material stock BEFORE", stockBefore);

            IReadOnlyList<SmithSnapshot> smiths = _connector.GetSmiths();
            LogRefineSmithPool(smiths);

            // No floors in this slice (null) - fully aggressive refining per the approved policy.
            RefinePlan plan = RefinePlanner.Plan(smiths, stockBefore, null);
            LogRefinePlan(plan, smiths);

            Dictionary<string, int> refinedCountByHero = ExecuteRefinePlan(plan);

            MaterialStock stockAfter = _connector.GetMaterialStock();
            LogMaterialStock("Material stock AFTER", stockAfter);

            SmithingLog.Screen("Smithing Suite: refined " + SumValues(refinedCountByHero) + " item(s).");
            return refinedCountByHero;
        }

        private Dictionary<string, int> ExecuteSmeltPlan(SmeltPlan plan)
        {
            SmithingLog.Line("Execution:");
            var smeltedCountByHero = new Dictionary<string, int>();
            foreach (SmeltAction action in plan.Actions)
            {
                if (!_connector.ExecuteSmelt(action.HeroId, action.ItemId))
                {
                    continue;
                }

                int countSoFar;
                smeltedCountByHero.TryGetValue(action.HeroId, out countSoFar);
                smeltedCountByHero[action.HeroId] = countSoFar + 1;
            }

            return smeltedCountByHero;
        }

        private Dictionary<string, int> ExecuteRefinePlan(RefinePlan plan)
        {
            SmithingLog.Line("Execution:");
            var refinedCountByHero = new Dictionary<string, int>();
            foreach (RefineAction action in plan.Actions)
            {
                if (!_connector.ExecuteRefine(action.HeroId, action.FormulaIndex))
                {
                    continue;
                }

                int countSoFar;
                refinedCountByHero.TryGetValue(action.HeroId, out countSoFar);
                refinedCountByHero[action.HeroId] = countSoFar + 1;
            }

            return refinedCountByHero;
        }

        private static void LogSmithPool(IReadOnlyList<SmithSnapshot> smiths)
        {
            SmithingLog.Line("Smith pool (" + smiths.Count + "):");
            foreach (SmithSnapshot smith in smiths)
            {
                SmithingLog.Line("  " + smith.Name + ": stamina=" + smith.Stamina + "/" + smith.MaxStamina
                    + ", smeltEnergyCost=" + smith.SmeltEnergyCost + ", capacity=" + smith.Capacity);
            }
        }

        private static void LogSmeltCandidates(IReadOnlyList<SmeltCandidate> candidates)
        {
            SmithingLog.Line("Smelt candidates (" + candidates.Count + "):");
            foreach (SmeltCandidate candidate in candidates)
            {
                string lockNote = candidate.IsLocked ? " [LOCKED]" : "";
                SmithingLog.Line("  " + candidate.Name + " x" + candidate.Count + lockNote);
            }
        }

        private static void LogSmeltPlan(SmeltPlan plan, IReadOnlyList<SmithSnapshot> smiths,
            IReadOnlyList<SmeltCandidate> candidates)
        {
            SmithingLog.Line("Smelt plan: " + plan.Actions.Count + " action(s).");
            foreach (SmeltAction action in plan.Actions)
            {
                SmithingLog.Line("  " + HeroDisplayName(smiths, action.HeroId) + " -> "
                    + ItemDisplayName(candidates, action.ItemId));
            }
        }

        private static void LogSmeltedTotals(Dictionary<string, int> smeltedCountByHero, IReadOnlyList<SmithSnapshot> smiths)
        {
            SmithingLog.Line("Smelted totals:");
            foreach (KeyValuePair<string, int> entry in smeltedCountByHero)
            {
                SmithingLog.Line("  " + HeroDisplayName(smiths, entry.Key) + ": " + entry.Value);
            }
        }

        private static void LogRefineSmithPool(IReadOnlyList<SmithSnapshot> smiths)
        {
            SmithingLog.Line("Smith pool (" + smiths.Count + "):");
            foreach (SmithSnapshot smith in smiths)
            {
                SmithingLog.Line("  " + smith.Name + ": stamina=" + smith.Stamina
                    + ", refineEnergyCost=" + smith.RefineEnergyCost
                    + ", refineCapacity=" + smith.RefineCapacity
                    + ", formulas=" + smith.RefineFormulas.Count);
                LogSmithFormulas(smith);
            }
        }

        // Listing each smith's available formulas (indexed, described) is what proves perk
        // gating in the log: a smith without the SteelMaker perk simply will not have an
        // Iron3->Iron4 entry in this list.
        private static void LogSmithFormulas(SmithSnapshot smith)
        {
            for (int i = 0; i < smith.RefineFormulas.Count; i++)
            {
                SmithingLog.Line("    [" + i + "] " + SmithingLog.Describe(smith.RefineFormulas[i]));
            }
        }

        private static void LogRefinePlan(RefinePlan plan, IReadOnlyList<SmithSnapshot> smiths)
        {
            SmithingLog.Line("Refine plan: " + plan.Actions.Count + " action(s).");
            foreach (RefineAction action in plan.Actions)
            {
                SmithingLog.Line("  " + HeroDisplayName(smiths, action.HeroId) + " -> ["
                    + action.FormulaIndex + "] " + DescribeFormula(smiths, action.HeroId, action.FormulaIndex));
            }
        }

        private static void LogMaterialStock(string label, MaterialStock stock)
        {
            var parts = new List<string>(MaterialOrdinalCount);
            for (int ordinal = 0; ordinal < MaterialOrdinalCount; ordinal++)
            {
                parts.Add(SmithingLog.MaterialName(ordinal) + "=" + stock.Get(ordinal));
            }

            SmithingLog.Line(label + ": " + string.Join(", ", parts));
        }

        private static void LogRunSummary(IReadOnlyList<SmithSnapshot> smithsBefore,
            IReadOnlyList<SmithSnapshot> smithsAfter, Dictionary<string, int> smeltedCountByHero,
            Dictionary<string, int> refinedCountByHero)
        {
            SmithingLog.Section("RUN SUMMARY");

            Dictionary<string, int> staminaBeforeByHero = BuildStaminaBeforeByHero(smithsBefore);
            foreach (SmithSnapshot smith in smithsAfter)
            {
                int smeltedCount;
                smeltedCountByHero.TryGetValue(smith.HeroId, out smeltedCount);
                int refinedCount;
                refinedCountByHero.TryGetValue(smith.HeroId, out refinedCount);
                if (smeltedCount == 0 && refinedCount == 0)
                {
                    continue;
                }

                int staminaBefore;
                staminaBeforeByHero.TryGetValue(smith.HeroId, out staminaBefore);
                int staminaSpent = staminaBefore - smith.Stamina;

                SmithingLog.Line("  " + smith.Name + ": smelted=" + smeltedCount + ", refined=" + refinedCount
                    + ", staminaSpent=" + staminaSpent);
            }
        }

        private static string HeroDisplayName(IReadOnlyList<SmithSnapshot> smiths, string heroId)
        {
            foreach (SmithSnapshot smith in smiths)
            {
                if (smith.HeroId == heroId)
                {
                    return smith.Name;
                }
            }

            return heroId;
        }

        private static string ItemDisplayName(IReadOnlyList<SmeltCandidate> candidates, string itemId)
        {
            foreach (SmeltCandidate candidate in candidates)
            {
                if (candidate.ItemId == itemId)
                {
                    return candidate.Name;
                }
            }

            return itemId;
        }

        private static string DescribeFormula(IReadOnlyList<SmithSnapshot> smiths, string heroId, int formulaIndex)
        {
            foreach (SmithSnapshot smith in smiths)
            {
                if (smith.HeroId != heroId)
                {
                    continue;
                }

                if (formulaIndex >= 0 && formulaIndex < smith.RefineFormulas.Count)
                {
                    return SmithingLog.Describe(smith.RefineFormulas[formulaIndex]);
                }
            }

            return "?";
        }

        private static int SumValues(Dictionary<string, int> counts)
        {
            int total = 0;
            foreach (int value in counts.Values)
            {
                total += value;
            }

            return total;
        }

        private static Dictionary<string, int> BuildStaminaBeforeByHero(IReadOnlyList<SmithSnapshot> smithsBefore)
        {
            var staminaBeforeByHero = new Dictionary<string, int>();
            foreach (SmithSnapshot smith in smithsBefore)
            {
                staminaBeforeByHero[smith.HeroId] = smith.Stamina;
            }

            return staminaBeforeByHero;
        }

        // Stamina spent is derived from a before/after snapshot pair spanning BOTH phases, not
        // from the planned smelt/refine cost, because the engine (not the plan) is the source of
        // truth for what stamina was actually deducted.
        private static string BuildSummary(IReadOnlyList<SmithSnapshot> smithsBefore,
            IReadOnlyList<SmithSnapshot> smithsAfter, Dictionary<string, int> smeltedCountByHero,
            Dictionary<string, int> refinedCountByHero)
        {
            Dictionary<string, int> staminaBeforeByHero = BuildStaminaBeforeByHero(smithsBefore);

            var lines = new List<string>();
            foreach (SmithSnapshot smith in smithsAfter)
            {
                int smeltedCount;
                smeltedCountByHero.TryGetValue(smith.HeroId, out smeltedCount);
                int refinedCount;
                refinedCountByHero.TryGetValue(smith.HeroId, out refinedCount);
                if (smeltedCount == 0 && refinedCount == 0)
                {
                    continue;
                }

                int staminaBefore;
                staminaBeforeByHero.TryGetValue(smith.HeroId, out staminaBefore);
                int staminaSpent = staminaBefore - smith.Stamina;

                lines.Add(new TextObject(
                        "{=SSSmithSummaryLine}{HERO}: {SMELTED} smelted, {REFINED} refined, {STAMINA} stamina spent",
                        null)
                    .SetTextVariable("HERO", smith.Name)
                    .SetTextVariable("SMELTED", smeltedCount)
                    .SetTextVariable("REFINED", refinedCount)
                    .SetTextVariable("STAMINA", staminaSpent)
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
    }
}
