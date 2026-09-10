using System.Collections.Generic;

namespace SmithingSuite.Logic
{
    /// <summary>
    /// The two-phase run orchestration (Refactor A): budget and plan a SMELT phase, execute it,
    /// then plan a REFINE phase against a FRESH post-smelt snapshot, execute it, and report what
    /// happened. Moved here (out of SmithingBehavior, which the test project cannot compile) so
    /// the one bug this mod actually shipped in-game - smelting spending all stamina and leaving
    /// refining nothing, because the composition of two individually-correct planners was wrong -
    /// can never regress silently again; see SmithingRunTests for the tests this now makes possible.
    ///
    /// Pure orchestration over <see cref="ISmithingConnector"/> and the engine-free planners - no
    /// engine types, no TextObject, no SmithingLog. Every observation is reported through
    /// <see cref="ISmithingTrace"/> instead, so the caller decides how (or whether) to log it.
    /// </summary>
    public static class SmithingRun
    {
        /// <param name="connector">The engine boundary (see <see cref="ISmithingConnector"/>).</param>
        /// <param name="smeltSharePercent">
        /// Percentage of each smith's stamina the SMELT phase may plan against; read from
        /// SmithingSuiteConfig by the caller and passed in, since this layer cannot reference
        /// that config type. See <see cref="SmithSnapshot.ForSmeltShare"/> for the clamp.
        /// </param>
        /// <param name="trace">Diagnostics sink - see <see cref="ISmithingTrace"/>.</param>
        public static RunReport Execute(ISmithingConnector connector, int smeltSharePercent, ISmithingTrace trace)
        {
            IReadOnlyList<SmithSnapshot> smithsBefore = connector.GetSmiths();
            Dictionary<string, int> smeltedCountByHero = RunSmeltPhase(connector, smithsBefore, smeltSharePercent, trace);

            // Smelting produces the raw materials refining consumes (ore, wood -> iron chain,
            // charcoal), so it must run first, and refining must plan against a FRESH post-smelt
            // snapshot of both stamina and material stock rather than smithsBefore - both have
            // changed by the time refining plans. RunRefinePhase takes its own connector.GetSmiths()
            // snapshot for exactly this reason - see SmithingRunTests for the regression this
            // guards against.
            Dictionary<string, int> refinedCountByHero = RunRefinePhase(connector, trace);

            if (smeltedCountByHero.Count == 0 && refinedCountByHero.Count == 0)
            {
                trace.Line("Nothing to do this run.");
                // No "after" snapshot is taken on this path (matches pre-refactor behaviour
                // exactly) - RunReport.From still yields NothingHappened=true and no entries from
                // two empty count dictionaries and an empty smithsAfter list.
                return RunReport.From(smithsBefore, new SmithSnapshot[0], smeltedCountByHero, refinedCountByHero);
            }

            IReadOnlyList<SmithSnapshot> smithsAfter = connector.GetSmiths();
            RunReport report = RunReport.From(smithsBefore, smithsAfter, smeltedCountByHero, refinedCountByHero);
            trace.RunSummary(report);
            return report;
        }

        private static Dictionary<string, int> RunSmeltPhase(
            ISmithingConnector connector, IReadOnlyList<SmithSnapshot> smiths, int smeltSharePercent, ISmithingTrace trace)
        {
            trace.Section("SMELT phase");
            trace.SmithPool(smiths);

            // Slice 6 fix: smelting used to plan against the smith's FULL stamina and consume all
            // of it, leaving refining only the integer-division remainder. Budget smelting down to
            // its configured share; the REFINE phase keeps taking its own fresh, unbudgeted
            // snapshot afterwards, so it naturally sees the untouched remainder.
            IReadOnlyList<SmithSnapshot> budgetedSmiths = ApplySmeltStaminaBudget(smiths, smeltSharePercent, trace);

            IReadOnlyList<SmeltCandidate> candidates = connector.GetSmeltCandidates();
            trace.SmeltCandidates(candidates);

            SmeltPlan plan = SmeltPlanner.Plan(budgetedSmiths, candidates);
            trace.SmeltPlanResult(plan, budgetedSmiths, candidates);

            Dictionary<string, int> smeltedCountByHero = ExecuteSmeltPlan(connector, plan, trace);
            trace.SmeltedTotals(smeltedCountByHero, budgetedSmiths);

            int smeltedCount = SumValues(smeltedCountByHero);
            trace.SmeltedToScreen(smeltedCount);

            return smeltedCountByHero;
        }

        // Maps each smith to a copy whose stamina is scaled down to the configured smelt share, so
        // SmeltPlanner never sees - and therefore can never spend - the stamina reserved for
        // refining. The clamp-and-floor arithmetic itself lives in SmithSnapshot.ForSmeltShare;
        // this is just the loop-and-trace wrapper around it.
        private static IReadOnlyList<SmithSnapshot> ApplySmeltStaminaBudget(
            IReadOnlyList<SmithSnapshot> smiths, int sharePercent, ISmithingTrace trace)
        {
            trace.StaminaBudgetHeader(sharePercent);

            var budgeted = new List<SmithSnapshot>(smiths.Count);
            foreach (SmithSnapshot smith in smiths)
            {
                SmithSnapshot budgetedSmith = SmithSnapshot.ForSmeltShare(smith, sharePercent);
                trace.StaminaBudgetEntry(smith, budgetedSmith);
                budgeted.Add(budgetedSmith);
            }

            return budgeted;
        }

        private static Dictionary<string, int> RunRefinePhase(ISmithingConnector connector, ISmithingTrace trace)
        {
            trace.Section("REFINE phase");

            MaterialStock stockBefore = connector.GetMaterialStock();
            trace.MaterialStockSnapshot("Material stock BEFORE", stockBefore);

            IReadOnlyList<SmithSnapshot> smiths = connector.GetSmiths();
            trace.RefineSmithPool(smiths);

            // No floors in this slice (null) - fully aggressive refining per the approved policy.
            RefinePlan plan = RefinePlanner.Plan(smiths, stockBefore, null);
            trace.RefinePlanResult(plan, smiths);

            Dictionary<string, int> refinedCountByHero = ExecuteRefinePlan(connector, plan, trace);

            MaterialStock stockAfter = connector.GetMaterialStock();
            trace.MaterialStockSnapshot("Material stock AFTER", stockAfter);

            int refinedCount = SumValues(refinedCountByHero);
            trace.RefinedToScreen(refinedCount);

            return refinedCountByHero;
        }

        private static Dictionary<string, int> ExecuteSmeltPlan(ISmithingConnector connector, SmeltPlan plan, ISmithingTrace trace)
        {
            trace.Line("Execution:");
            var smeltedCountByHero = new Dictionary<string, int>();
            foreach (SmeltAction action in plan.Actions)
            {
                if (!connector.ExecuteSmelt(action.HeroId, action.ItemId))
                {
                    continue;
                }

                int countSoFar;
                smeltedCountByHero.TryGetValue(action.HeroId, out countSoFar);
                smeltedCountByHero[action.HeroId] = countSoFar + 1;
            }

            return smeltedCountByHero;
        }

        private static Dictionary<string, int> ExecuteRefinePlan(ISmithingConnector connector, RefinePlan plan, ISmithingTrace trace)
        {
            trace.Line("Execution:");
            var refinedCountByHero = new Dictionary<string, int>();
            foreach (RefineAction action in plan.Actions)
            {
                if (!connector.ExecuteRefine(action.HeroId, action.FormulaIndex))
                {
                    continue;
                }

                int countSoFar;
                refinedCountByHero.TryGetValue(action.HeroId, out countSoFar);
                refinedCountByHero[action.HeroId] = countSoFar + 1;
            }

            return refinedCountByHero;
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
    }
}
