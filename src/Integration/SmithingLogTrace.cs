using System.Collections.Generic;
using SmithingSuite.Logic;

namespace SmithingSuite.Integration
{
    /// <summary>
    /// The <see cref="SmithingLog"/>-backed implementation of <see cref="ISmithingTrace"/>
    /// (Refactor A). Every method here is the exact code that used to live as a private method on
    /// <see cref="SmithingBehavior"/>, just moved - the log it produces is byte-identical to what
    /// SmithingBehavior wrote directly before this refactor, for the same inputs. Formatting and
    /// the <see cref="SmithingLog.IsVerbose"/> gate both live here (not in
    /// <see cref="SmithingSuite.Logic.SmithingRun"/>) so the orchestration stays engine-free and
    /// so the "skip the expensive string building when verbose is off" property survives the move.
    /// </summary>
    public sealed class SmithingLogTrace : ISmithingTrace
    {
        // Matches MaterialStock's own slot count (IronOre=0 .. Charcoal=8) - duplicated here only
        // for the diagnostics log, which reads material counts through the public Get() accessor
        // rather than reaching into MaterialStock internals.
        private const int MaterialOrdinalCount = 9;

        public void Section(string title)
        {
            SmithingLog.Section(title);
        }

        public void Line(string message)
        {
            SmithingLog.Line(message);
        }

        public void Screen(string message)
        {
            SmithingLog.Screen(message);
        }

        public void SmithPool(IReadOnlyList<SmithSnapshot> smiths)
        {
            if (!SmithingLog.IsVerbose)
            {
                return;
            }

            SmithingLog.Line("Smith pool (" + smiths.Count + "):");
            foreach (SmithSnapshot smith in smiths)
            {
                SmithingLog.Line("  " + smith.Name + ": stamina=" + smith.Stamina + "/" + smith.MaxStamina
                    + ", smeltEnergyCost=" + smith.SmeltEnergyCost + ", capacity=" + smith.Capacity
                    + ", " + DescribePerks(smith));
            }
        }

        public void StaminaBudgetHeader(int sharePercent)
        {
            if (!SmithingLog.IsVerbose)
            {
                return;
            }

            // The configured value is logged as-is (not re-clamped here) - the clamp is Logic's
            // job (SmithSnapshot.ForSmeltShare), and each per-smith line below already shows the
            // resulting budgetedStamina/smeltCapacity, so a reader still sees the effective share.
            SmithingLog.Line("Stamina budget (smelt share=" + sharePercent + "%):");
        }

        public void StaminaBudgetEntry(SmithSnapshot fullSmith, SmithSnapshot budgetedSmith)
        {
            if (!SmithingLog.IsVerbose)
            {
                return;
            }

            SmithingLog.Line("  " + fullSmith.Name + ": fullStamina=" + fullSmith.Stamina
                + ", budgetedStamina=" + budgetedSmith.Stamina + ", smeltCapacity=" + budgetedSmith.Capacity);
        }

        // Class/value/template per candidate is what proves AC1/AC2 in the log: research
        // candidates (unopened template parts) must appear ordered before bulk ones, and within a
        // class, descending by value - see SmeltPlanner for the policy this log line verifies.
        public void SmeltCandidates(IReadOnlyList<SmeltCandidate> candidates)
        {
            if (!SmithingLog.IsVerbose)
            {
                return;
            }

            SmithingLog.Line("Smelt candidates (" + candidates.Count + "):");
            foreach (SmeltCandidate candidate in candidates)
            {
                string lockNote = candidate.IsLocked ? " [LOCKED]" : "";
                SmithingLog.Line("  " + candidate.Name + " x" + candidate.Count + lockNote
                    + " [" + CandidateClass(candidate) + ", value=" + candidate.Value
                    + ", template=" + candidate.TemplateId + "]");
            }
        }

        public void SmeltPlanResult(SmeltPlan plan, IReadOnlyList<SmithSnapshot> smiths, IReadOnlyList<SmeltCandidate> candidates)
        {
            if (!SmithingLog.IsVerbose)
            {
                return;
            }

            SmithingLog.Line("Smelt plan: " + plan.Actions.Count + " action(s).");
            foreach (SmeltAction action in plan.Actions)
            {
                SmeltCandidate candidate = FindCandidate(candidates, action.ItemId);
                SmithingLog.Line("  " + HeroDisplayName(smiths, action.HeroId) + " -> "
                    + ItemDisplayName(candidates, action.ItemId)
                    + " (" + DescribeReason(action.Reason, candidate) + ")");
            }
        }

        public void SmeltedTotals(Dictionary<string, int> smeltedCountByHero, IReadOnlyList<SmithSnapshot> smiths)
        {
            if (!SmithingLog.IsVerbose)
            {
                return;
            }

            SmithingLog.Line("Smelted totals:");
            foreach (KeyValuePair<string, int> entry in smeltedCountByHero)
            {
                SmithingLog.Line("  " + HeroDisplayName(smiths, entry.Key) + ": " + entry.Value);
            }
        }

        public void RefineSmithPool(IReadOnlyList<SmithSnapshot> smiths)
        {
            if (!SmithingLog.IsVerbose)
            {
                return;
            }

            SmithingLog.Line("Smith pool (" + smiths.Count + "):");
            foreach (SmithSnapshot smith in smiths)
            {
                SmithingLog.Line("  " + smith.Name + ": stamina=" + smith.Stamina
                    + ", refineEnergyCost=" + smith.RefineEnergyCost
                    + ", refineCapacity=" + smith.RefineCapacity
                    + ", formulas=" + smith.RefineFormulas.Count
                    + ", " + DescribePerks(smith));
                LogSmithFormulas(smith);
            }
        }

        // Listing each smith's available formulas (indexed, described) is what proves perk
        // gating in the log: a smith without the SteelMaker perk simply will not have an
        // Iron3->Iron4 entry in this list.
        private static void LogSmithFormulas(SmithSnapshot smith)
        {
            if (!SmithingLog.IsVerbose)
            {
                return;
            }

            for (int i = 0; i < smith.RefineFormulas.Count; i++)
            {
                SmithingLog.Line("    [" + i + "] " + SmithingLog.Describe(smith.RefineFormulas[i]));
            }
        }

        public void MaterialStockSnapshot(string label, MaterialStock stock)
        {
            if (!SmithingLog.IsVerbose)
            {
                return;
            }

            var parts = new List<string>(MaterialOrdinalCount);
            for (int ordinal = 0; ordinal < MaterialOrdinalCount; ordinal++)
            {
                parts.Add(SmithingLog.MaterialName(ordinal) + "=" + stock.Get(ordinal));
            }

            SmithingLog.Line(label + ": " + string.Join(", ", parts));
        }

        public void RefinePlanResult(RefinePlan plan, IReadOnlyList<SmithSnapshot> smiths)
        {
            if (!SmithingLog.IsVerbose)
            {
                return;
            }

            SmithingLog.Line("Refine plan: " + plan.Actions.Count + " action(s).");
            foreach (RefineAction action in plan.Actions)
            {
                SmithingLog.Line("  " + HeroDisplayName(smiths, action.HeroId) + " -> ["
                    + action.FormulaIndex + "] " + DescribeFormula(smiths, action.HeroId, action.FormulaIndex));
            }
        }

        public void RunSummary(RunReport report)
        {
            if (!SmithingLog.IsVerbose)
            {
                return;
            }

            SmithingLog.Section("RUN SUMMARY");
            foreach (RunReportEntry entry in report.Entries)
            {
                SmithingLog.Line("  " + entry.HeroName + ": smelted=" + entry.Smelted + ", refined=" + entry.Refined
                    + ", staminaSpent=" + entry.StaminaSpent);
            }
        }

        // Shared by both the smelt- and refine-phase smith-pool log lines so a tester can see all
        // three perk flags together next to stamina/capacity, without cross-referencing.
        private static string DescribePerks(SmithSnapshot smith)
        {
            return "curiousSmelter=" + smith.HasCuriousSmelter + ", practicalSmelter=" + smith.HasPracticalSmelter
                + ", practicalRefiner=" + smith.HasPracticalRefiner;
        }

        private static string CandidateClass(SmeltCandidate candidate)
        {
            return candidate.TemplateHasUnopenedParts ? "research" : "bulk";
        }

        // Renders SmeltPlanner's own routing decision (Refactor B: the reason is data on the
        // action, not reconstructed here). The candidate's class still decides which perk name
        // ("CuriousSmelter" vs "PracticalSmelter") the fallback wording uses - that is just
        // display detail already carried by the candidate, not a re-derivation of which branch
        // the planner took.
        private static string DescribeReason(SmeltRoutingReason reason, SmeltCandidate candidate)
        {
            bool researchRelevant = candidate != null && candidate.TemplateHasUnopenedParts;

            switch (reason)
            {
                case SmeltRoutingReason.CuriousSmelterPreferred:
                    return "CuriousSmelter preferred";
                case SmeltRoutingReason.PracticalSmelterPreferred:
                    return "PracticalSmelter preferred";
                case SmeltRoutingReason.FallbackNoPerkedSmith:
                    return researchRelevant ? "fallback - no CuriousSmelter in party" : "fallback - no PracticalSmelter in party";
                case SmeltRoutingReason.FallbackPerkedSmithAtCapacity:
                    return researchRelevant ? "fallback - CuriousSmelter at capacity" : "fallback - PracticalSmelter at capacity";
                default:
                    return "?";
            }
        }

        private static SmeltCandidate FindCandidate(IReadOnlyList<SmeltCandidate> candidates, string itemId)
        {
            foreach (SmeltCandidate candidate in candidates)
            {
                if (candidate.ItemId == itemId)
                {
                    return candidate;
                }
            }

            return null;
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
    }
}
