using System.Collections.Generic;

namespace SmithingSuite.Logic
{
    /// <summary>
    /// The outcome of one <see cref="SmithingRun.Execute"/> call: per-hero smelted/refined counts
    /// and stamina spent, plus whether the run did nothing at all. Engine-free and TextObject-free
    /// by design (Refactor A) - SmithingBehavior turns this into a player-facing message
    /// (BuildSummary) and a verbose log section (ISmithingTrace.RunSummary); this type only
    /// carries the data both of those need, once.
    /// </summary>
    public sealed class RunReport
    {
        /// <summary>
        /// True exactly when both the smelt and refine phases produced zero actions for every
        /// hero - i.e. the run had nothing to do. When true, <see cref="Entries"/> is always empty.
        /// </summary>
        public bool NothingHappened { get; }

        /// <summary>
        /// One entry per hero who smelted or refined at least once this run, in the same order as
        /// the "after" snapshot the run observed. Heroes with zero smelted AND zero refined are
        /// omitted, matching the pre-refactor summary/log behaviour exactly.
        /// </summary>
        public IReadOnlyList<RunReportEntry> Entries { get; }

        private RunReport(bool nothingHappened, IReadOnlyList<RunReportEntry> entries)
        {
            NothingHappened = nothingHappened;
            Entries = entries;
        }

        /// <summary>
        /// Builds a report from a before/after snapshot pair spanning both phases and each
        /// phase's per-hero action counts. <paramref name="smithsAfter"/> should be empty when the
        /// run never took an "after" snapshot (the nothing-happened path) - an empty list combined
        /// with two empty count dictionaries naturally yields <see cref="NothingHappened"/> true
        /// and no entries, without requiring a separate factory method.
        ///
        /// Stamina spent is derived from the snapshots, never from planned cost, because the
        /// engine (not the plan) is the source of truth for what stamina was actually deducted.
        /// </summary>
        public static RunReport From(
            IReadOnlyList<SmithSnapshot> smithsBefore,
            IReadOnlyList<SmithSnapshot> smithsAfter,
            Dictionary<string, int> smeltedCountByHero,
            Dictionary<string, int> refinedCountByHero)
        {
            bool nothingHappened = smeltedCountByHero.Count == 0 && refinedCountByHero.Count == 0;

            var staminaBeforeByHero = new Dictionary<string, int>();
            foreach (SmithSnapshot smith in smithsBefore)
            {
                staminaBeforeByHero[smith.HeroId] = smith.Stamina;
            }

            var entries = new List<RunReportEntry>();
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

                entries.Add(new RunReportEntry(smith.HeroId, smith.Name, smeltedCount, refinedCount, staminaSpent));
            }

            return new RunReport(nothingHappened, entries);
        }
    }

    /// <summary>One hero's outcome within a <see cref="RunReport"/>.</summary>
    public sealed class RunReportEntry
    {
        public string HeroId { get; }
        public string HeroName { get; }
        public int Smelted { get; }
        public int Refined { get; }
        public int StaminaSpent { get; }

        public RunReportEntry(string heroId, string heroName, int smelted, int refined, int staminaSpent)
        {
            HeroId = heroId;
            HeroName = heroName;
            Smelted = smelted;
            Refined = refined;
            StaminaSpent = staminaSpent;
        }
    }
}
