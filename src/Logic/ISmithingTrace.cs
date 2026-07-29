using System.Collections.Generic;

namespace SmithingSuite.Logic
{
    /// <summary>
    /// Diagnostics sink for one <see cref="SmithingRun.Execute"/> call. Engine-free by design, so
    /// the orchestration in <see cref="SmithingRun"/> never needs to know about SmithingLog,
    /// TextObject, or SmithingSuiteConfig's verbosity flags - it just reports what happened, in
    /// the same order it happened. SmithingBehavior supplies the real, SmithingLog-backed
    /// implementation (see Integration/SmithingLogTrace.cs); tests supply a no-op or recording one.
    ///
    /// Each method mirrors, 1:1, a log line (or block of lines) SmithingBehavior used to emit
    /// directly - see SmithingLogTrace for the exact formatting and verbosity gating, which must
    /// stay byte-identical to what it replaced.
    /// </summary>
    public interface ISmithingTrace
    {
        /// <summary>A visually separated section header, e.g. "SMELT phase".</summary>
        void Section(string title);

        /// <summary>One free-form timestamped line.</summary>
        void Line(string message);

        /// <summary>A short message posted to the in-game message log (not just the file).</summary>
        void Screen(string message);

        void SmithPool(IReadOnlyList<SmithSnapshot> smiths);
        void StaminaBudgetHeader(int sharePercent);
        void StaminaBudgetEntry(SmithSnapshot fullSmith, SmithSnapshot budgetedSmith);
        void SmeltCandidates(IReadOnlyList<SmeltCandidate> candidates);
        void SmeltPlanResult(SmeltPlan plan, IReadOnlyList<SmithSnapshot> smiths, IReadOnlyList<SmeltCandidate> candidates);
        void SmeltedTotals(Dictionary<string, int> smeltedCountByHero, IReadOnlyList<SmithSnapshot> smiths);

        void RefineSmithPool(IReadOnlyList<SmithSnapshot> smiths);
        void MaterialStockSnapshot(string label, MaterialStock stock);
        void RefinePlanResult(RefinePlan plan, IReadOnlyList<SmithSnapshot> smiths);

        /// <summary>The verbose "RUN SUMMARY" section, one line per hero in <paramref name="report"/>.</summary>
        void RunSummary(RunReport report);
    }
}
