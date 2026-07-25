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
    /// </summary>
    public class SmithingBehavior : CampaignBehaviorBase
    {
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
            IReadOnlyList<SmithSnapshot> smithsBefore = _connector.GetSmiths();
            IReadOnlyList<SmeltCandidate> candidates = _connector.GetSmeltCandidates();
            SmeltPlan plan = SmeltPlanner.Plan(smithsBefore, candidates);

            if (plan.Actions.Count == 0)
            {
                _connector.Report(new TextObject(
                    "{=SSNothingToSmelt}Nothing to smelt: every smith is out of crafting stamina, or there are no unlocked smeltable items.",
                    null).ToString());
                return;
            }

            Dictionary<string, int> smeltedCountByHero = ExecutePlan(plan);
            IReadOnlyList<SmithSnapshot> smithsAfter = _connector.GetSmiths();

            _connector.Report(BuildSummary(smithsBefore, smithsAfter, smeltedCountByHero));
        }

        private Dictionary<string, int> ExecutePlan(SmeltPlan plan)
        {
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

        // Stamina spent is derived from a before/after snapshot pair, not from the planned smelt
        // cost, because the engine (not the plan) is the source of truth for what stamina was
        // actually deducted.
        private static string BuildSummary(IReadOnlyList<SmithSnapshot> smithsBefore,
            IReadOnlyList<SmithSnapshot> smithsAfter, Dictionary<string, int> smeltedCountByHero)
        {
            Dictionary<string, int> staminaBeforeByHero = new Dictionary<string, int>();
            foreach (SmithSnapshot smith in smithsBefore)
            {
                staminaBeforeByHero[smith.HeroId] = smith.Stamina;
            }

            var lines = new List<string>();
            foreach (SmithSnapshot smith in smithsAfter)
            {
                int smeltedCount;
                smeltedCountByHero.TryGetValue(smith.HeroId, out smeltedCount);
                if (smeltedCount == 0)
                {
                    continue;
                }

                int staminaBefore;
                staminaBeforeByHero.TryGetValue(smith.HeroId, out staminaBefore);
                int staminaSpent = staminaBefore - smith.Stamina;

                lines.Add(new TextObject("{=SSSmeltSummaryLine}{HERO}: {COUNT} smelted, {STAMINA} stamina spent", null)
                    .SetTextVariable("HERO", smith.Name)
                    .SetTextVariable("COUNT", smeltedCount)
                    .SetTextVariable("STAMINA", staminaSpent)
                    .ToString());
            }

            if (lines.Count == 0)
            {
                return new TextObject(
                    "{=SSNothingToSmelt}Nothing to smelt: every smith is out of crafting stamina, or there are no unlocked smeltable items.",
                    null).ToString();
            }

            string header = new TextObject("{=SSSmeltSummaryHeader}Automatic smithing:", null).ToString();
            return header + "\n" + string.Join("\n", lines);
        }
    }
}
