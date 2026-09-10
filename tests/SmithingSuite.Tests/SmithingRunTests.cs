using System;
using System.Collections.Generic;
using System.Linq;
using SmithingSuite.Logic;
using Xunit;

namespace SmithingSuite.Tests
{
    /// <summary>
    /// Tests for <c>SmithingSuite.Logic.SmithingRun</c> (Refactor A). Before this refactor, the
    /// two-phase run orchestration lived in SmithingBehavior, which the test project cannot
    /// compile (it references TaleWorlds.*) - so none of these were possible, and the one
    /// composition bug this mod actually shipped in-game (smelting spent all stamina, leaving
    /// refining nothing) went undetected by 87 otherwise-green tests. Each test here targets
    /// exactly the class of bug that one was.
    /// </summary>
    public class SmithingRunTests
    {
        private static SmithSnapshot Smith(
            string heroId,
            int stamina,
            int smeltEnergyCost,
            int refineEnergyCost = 0,
            IReadOnlyList<RefineFormulaSnapshot> refineFormulas = null)
            => new SmithSnapshot(
                heroId, name: heroId, stamina: stamina, maxStamina: 100,
                smeltEnergyCost: smeltEnergyCost, refineEnergyCost: refineEnergyCost, refineFormulas: refineFormulas);

        private static SmeltCandidate Candidate(string itemId, int count)
            => new SmeltCandidate(itemId, name: itemId, count: count, isLocked: false);

        // Ore (0) -> Iron1 (1), 1-for-1, no second input/output - just enough for RefinePlanner to
        // keep finding an applicable formula for as many actions as the smith has capacity for.
        private static RefineFormulaSnapshot OreToIron1()
            => new RefineFormulaSnapshot(formulaIndex: 0, input1: 0, input1Count: 1, input2: 0, input2Count: 0,
                output: 1, outputCount: 1, output2: 0, output2Count: 0);

        [Fact]
        public void Execute_SmeltShareIsRespected_AndRefiningConsequentlyGetsNonZeroCapacity()
        {
            // This is the Slice 6 regression, reproduced directly: a smith with abundant smelt
            // supply and abundant refine capability. At smeltSharePercent=50, smelting must spend
            // at most half of the smith's stamina - leaving the other half for refining to plan
            // (and execute) against, instead of the old bug where smelting spent everything and
            // refining got the integer-division remainder (usually 0).
            var smith = Smith("hero-1", stamina: 100, smeltEnergyCost: 10, refineEnergyCost: 10, refineFormulas: new[] { OreToIron1() });
            var stock = new MaterialStock();
            stock.Set(0, 1000); // ample ore - refining is never blocked by material supply here

            var connector = new FakeSmithingConnector(new[] { smith }, new[] { Candidate("item-sword", count: 100) }, stock);

            var report = SmithingRun.Execute(connector, smeltSharePercent: 50, new NoOpTrace());

            var entry = Assert.Single(report.Entries);
            Assert.True(entry.Smelted <= 5, "smelting must never exceed floor(100 * 50% / 10) = 5 actions");
            Assert.True(entry.Refined > 0, "refining must get non-zero capacity once smelting is budgeted to its share");
        }

        [Fact]
        public void Execute_RefinePhasePlansAgainstThePostSmeltSnapshot_NotThePreSmeltOne()
        {
            // The connector's smiths snapshot is queried three times by one run: once for the
            // smelt phase's budget (pre-smelt), once for the refine phase's own planning snapshot,
            // once for the final "after" report. Here the SECOND snapshot deliberately reports far
            // less stamina than the first (as if smelting had spent it, though no smelt candidates
            // are offered so the smelt phase itself performs zero actions) - isolating that the
            // refine plan is built from that fresh snapshot, not reused from the smelt phase's.
            var formulas = new[] { OreToIron1() };
            var fullStamina = Smith("hero-1", stamina: 100, smeltEnergyCost: 10, refineEnergyCost: 10, refineFormulas: formulas);
            var reducedStamina = Smith("hero-1", stamina: 20, smeltEnergyCost: 10, refineEnergyCost: 10, refineFormulas: formulas);
            var stock = new MaterialStock();
            stock.Set(0, 1000);

            var connector = new SequencedSmithsConnector(
                new IReadOnlyList<SmithSnapshot>[]
                {
                    new[] { fullStamina },    // 1: smelt-phase budget snapshot
                    new[] { reducedStamina }, // 2: refine-phase planning snapshot
                    new[] { reducedStamina }, // 3: final "after" snapshot
                },
                candidates: new SmeltCandidate[0],
                stock: stock);

            var report = SmithingRun.Execute(connector, smeltSharePercent: 50, new NoOpTrace());

            // RefineCapacity at stamina=100/cost=10 would be 10; at stamina=20/cost=10 it is 2. If
            // the refine phase had (incorrectly) reused the first snapshot, this would be 10.
            var entry = Assert.Single(report.Entries);
            Assert.Equal(2, entry.Refined);
        }

        [Fact]
        public void Execute_AFailedExecuteSmeltIsNotCounted_AndDoesNotAbortRemainingActions()
        {
            var smith = Smith("hero-1", stamina: 100, smeltEnergyCost: 10);
            var connector = new FakeSmithingConnector(
                new[] { smith },
                new[] { Candidate("item-sword", count: 3) },
                stock: null,
                smeltShouldSucceed: (heroId, itemId, callIndex) => callIndex != 2);

            var report = SmithingRun.Execute(connector, smeltSharePercent: 100, new NoOpTrace());

            // 3 planned actions, all 3 attempted (not aborted after the failure), only 2 counted.
            Assert.Equal(3, connector.ExecuteSmeltCallCount);
            var entry = Assert.Single(report.Entries);
            Assert.Equal(2, entry.Smelted);
        }

        [Fact]
        public void Execute_NothingHappenedIsTrueExactlyWhenBothPhasesProducedZeroActions()
        {
            var smith = Smith("hero-1", stamina: 100, smeltEnergyCost: 10);
            var connectorWithNothingToDo = new FakeSmithingConnector(new[] { smith }, new SmeltCandidate[0]);

            var emptyReport = SmithingRun.Execute(connectorWithNothingToDo, smeltSharePercent: 50, new NoOpTrace());

            Assert.True(emptyReport.NothingHappened);
            Assert.Empty(emptyReport.Entries);

            var connectorWithWork = new FakeSmithingConnector(new[] { smith }, new[] { Candidate("item-sword", count: 1) });

            var nonEmptyReport = SmithingRun.Execute(connectorWithWork, smeltSharePercent: 100, new NoOpTrace());

            Assert.False(nonEmptyReport.NothingHappened);
            Assert.NotEmpty(nonEmptyReport.Entries);
        }

        [Fact]
        public void Execute_StaminaSpentComesFromBeforeAfterSnapshots_NotFromPlannedCost()
        {
            // The smith's declared smeltEnergyCost (10) is what the PLAN is built against, but the
            // connector's actual execution deducts a different amount (7) per successful smelt -
            // simulating any real-world discrepancy between planned and actually-spent stamina.
            // staminaSpent on the report must reflect the ACTUAL delta, not planned cost * count.
            var smith = Smith("hero-1", stamina: 100, smeltEnergyCost: 10);
            var connector = new FakeSmithingConnector(
                new[] { smith },
                new[] { Candidate("item-sword", count: 2) },
                stock: null,
                actualSmeltStaminaCost: 7);

            var report = SmithingRun.Execute(connector, smeltSharePercent: 100, new NoOpTrace());

            var entry = Assert.Single(report.Entries);
            Assert.Equal(2, entry.Smelted);
            Assert.Equal(14, entry.StaminaSpent); // actual: 2 * 7, NOT the planned 2 * 10 = 20
        }

        /// <summary>No-op <see cref="ISmithingTrace"/> - these tests assert on <see cref="RunReport"/>, not on logging.</summary>
        private sealed class NoOpTrace : ISmithingTrace
        {
            public void Section(string title) { }
            public void Line(string message) { }
            public void SmeltedToScreen(int count) { }
            public void RefinedToScreen(int count) { }
            public void SmithPool(IReadOnlyList<SmithSnapshot> smiths) { }
            public void StaminaBudgetHeader(int sharePercent) { }
            public void StaminaBudgetEntry(SmithSnapshot fullSmith, SmithSnapshot budgetedSmith) { }
            public void SmeltCandidates(IReadOnlyList<SmeltCandidate> candidates) { }
            public void SmeltPlanResult(SmeltPlan plan, IReadOnlyList<SmithSnapshot> smiths, IReadOnlyList<SmeltCandidate> candidates) { }
            public void SmeltedTotals(Dictionary<string, int> smeltedCountByHero, IReadOnlyList<SmithSnapshot> smiths) { }
            public void RefineSmithPool(IReadOnlyList<SmithSnapshot> smiths) { }
            public void MaterialStockSnapshot(string label, MaterialStock stock) { }
            public void RefinePlanResult(RefinePlan plan, IReadOnlyList<SmithSnapshot> smiths) { }
            public void RunSummary(RunReport report) { }
        }

        /// <summary>
        /// A stateful fake connector: tracks per-hero stamina and per-candidate remaining count,
        /// mutating them as smelt/refine actions execute - so a second GetSmiths()/GetSmeltCandidates()
        /// call naturally reflects everything executed so far, exactly like the real connector would.
        /// </summary>
        private sealed class FakeSmithingConnector : ISmithingConnector
        {
            private readonly Dictionary<string, SmithSnapshot> _templates = new Dictionary<string, SmithSnapshot>();
            private readonly Dictionary<string, int> _stamina = new Dictionary<string, int>();
            private readonly Dictionary<string, int> _candidateRemaining = new Dictionary<string, int>();
            private readonly List<SmeltCandidate> _candidateTemplates;
            private readonly MaterialStock _stock;
            private readonly int? _actualSmeltStaminaCost;
            private readonly Func<string, string, int, bool> _smeltShouldSucceed;
            private int _smeltCallIndex;

            public int ExecuteSmeltCallCount;

            public FakeSmithingConnector(
                IEnumerable<SmithSnapshot> smiths,
                IEnumerable<SmeltCandidate> candidates,
                MaterialStock stock = null,
                int? actualSmeltStaminaCost = null,
                Func<string, string, int, bool> smeltShouldSucceed = null)
            {
                foreach (var smith in smiths)
                {
                    _templates[smith.HeroId] = smith;
                    _stamina[smith.HeroId] = smith.Stamina;
                }

                _candidateTemplates = candidates.ToList();
                foreach (var candidate in _candidateTemplates)
                {
                    _candidateRemaining[candidate.ItemId] = candidate.Count;
                }

                _stock = stock ?? new MaterialStock();
                _actualSmeltStaminaCost = actualSmeltStaminaCost;
                _smeltShouldSucceed = smeltShouldSucceed ?? ((heroId, itemId, callIndex) => true);
            }

            public IReadOnlyList<SmithSnapshot> GetSmiths()
            {
                var result = new List<SmithSnapshot>();
                foreach (var template in _templates.Values)
                {
                    result.Add(new SmithSnapshot(
                        template.HeroId, template.Name, _stamina[template.HeroId], template.MaxStamina,
                        template.SmeltEnergyCost, template.RefineEnergyCost, template.RefineFormulas,
                        template.HasCuriousSmelter, template.HasPracticalSmelter, template.HasPracticalRefiner));
                }

                return result;
            }

            public IReadOnlyList<SmeltCandidate> GetSmeltCandidates()
            {
                var result = new List<SmeltCandidate>();
                foreach (var candidate in _candidateTemplates)
                {
                    int remaining = _candidateRemaining[candidate.ItemId];
                    if (remaining > 0)
                    {
                        result.Add(new SmeltCandidate(candidate.ItemId, candidate.Name, remaining, candidate.IsLocked,
                            candidate.Value, candidate.TemplateId, candidate.TemplateHasUnopenedParts));
                    }
                }

                return result;
            }

            public bool ExecuteSmelt(string heroId, string itemId)
            {
                ExecuteSmeltCallCount++;
                _smeltCallIndex++;

                if (!_smeltShouldSucceed(heroId, itemId, _smeltCallIndex))
                {
                    return false;
                }

                int cost = _actualSmeltStaminaCost ?? _templates[heroId].SmeltEnergyCost;
                if (_stamina[heroId] < cost)
                {
                    return false;
                }

                _stamina[heroId] -= cost;
                _candidateRemaining[itemId]--;
                return true;
            }

            public MaterialStock GetMaterialStock() => _stock;

            public bool ExecuteRefine(string heroId, int formulaIndex)
            {
                var smith = _templates[heroId];
                if (formulaIndex < 0 || formulaIndex >= smith.RefineFormulas.Count)
                {
                    return false;
                }

                var formula = smith.RefineFormulas[formulaIndex];
                if (_stamina[heroId] < smith.RefineEnergyCost || !_stock.CanApply(formula, null))
                {
                    return false;
                }

                _stamina[heroId] -= smith.RefineEnergyCost;
                _stock.Apply(formula);
                return true;
            }

            public void Report(string message, ReportTone tone = ReportTone.Neutral)
            {
            }
        }

        /// <summary>
        /// A connector whose GetSmiths() returns pre-set snapshots in call order, ignoring
        /// whatever ExecuteSmelt/ExecuteRefine actually did - used only to isolate "which snapshot
        /// does the refine phase plan against" from execution mechanics.
        /// </summary>
        private sealed class SequencedSmithsConnector : ISmithingConnector
        {
            private readonly Queue<IReadOnlyList<SmithSnapshot>> _smithsSequence;
            private readonly IReadOnlyList<SmeltCandidate> _candidates;
            private readonly MaterialStock _stock;

            public SequencedSmithsConnector(IReadOnlyList<SmithSnapshot>[] smithsSequence, IReadOnlyList<SmeltCandidate> candidates, MaterialStock stock)
            {
                _smithsSequence = new Queue<IReadOnlyList<SmithSnapshot>>(smithsSequence);
                _candidates = candidates;
                _stock = stock;
            }

            public IReadOnlyList<SmithSnapshot> GetSmiths() => _smithsSequence.Dequeue();
            public IReadOnlyList<SmeltCandidate> GetSmeltCandidates() => _candidates;
            public bool ExecuteSmelt(string heroId, string itemId) => false;
            public MaterialStock GetMaterialStock() => _stock;
            public bool ExecuteRefine(string heroId, int formulaIndex) => true;
            public void Report(string message, ReportTone tone = ReportTone.Neutral) { }
        }
    }
}
