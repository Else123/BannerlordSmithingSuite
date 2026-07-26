using System.Collections.Generic;
using System.Linq;
using SmithingSuite.Logic;
using Xunit;

namespace SmithingSuite.Tests
{
    /// <summary>
    /// Red-state tests for the not-yet-implemented Slice 3 smelt routing policy
    /// (SPEC-SLICE3.md TODO 3, ahead of the TODO 4 implementation). Specifies the NEW behaviour
    /// of <c>SmithingSuite.Logic.SmeltPlanner</c> per SPEC-SLICE3.md acceptance criteria
    /// AC1-AC7: two priority classes (research-relevant before bulk, each ordered by descending
    /// value) and perk-preferring assignment (CuriousSmelter for research work, PracticalSmelter
    /// for bulk work), while the Slice 1 capacity/lock/starvation guarantees survive untouched.
    /// </summary>
    public class SmeltRoutingTests
    {
        private static SmithSnapshot Smith(
            string heroId,
            int stamina,
            int smeltEnergyCost,
            bool hasCuriousSmelter = false,
            bool hasPracticalSmelter = false)
            => new SmithSnapshot(
                heroId,
                name: heroId,
                stamina: stamina,
                maxStamina: 100,
                smeltEnergyCost: smeltEnergyCost,
                refineEnergyCost: 0,
                refineFormulas: null,
                hasCuriousSmelter: hasCuriousSmelter,
                hasPracticalSmelter: hasPracticalSmelter,
                hasPracticalRefiner: false);

        private static SmeltCandidate Candidate(
            string itemId,
            int count,
            bool isLocked = false,
            int value = 0,
            string templateId = "",
            bool templateHasUnopenedParts = false)
            => new SmeltCandidate(itemId, name: itemId, count: count, isLocked: isLocked, value: value, templateId: templateId, templateHasUnopenedParts: templateHasUnopenedParts);

        [Fact]
        public void Plan_ResearchRelevantCandidatesArePlannedBeforeBulkCandidates()
        {
            // Capacity is 1, so only one action fits. The bulk item is far more valuable (1000
            // vs 1) and appears first in the input list, but the research-relevant item must
            // still win - class beats both list order and value.
            var smith = Smith("hero-1", stamina: 10, smeltEnergyCost: 10);
            var bulkCandidate = Candidate("item-bulk", count: 5, value: 1000, templateId: "tpl-bulk", templateHasUnopenedParts: false);
            var researchCandidate = Candidate("item-research", count: 5, value: 1, templateId: "tpl-research", templateHasUnopenedParts: true);

            var plan = SmeltPlanner.Plan(new[] { smith }, new[] { bulkCandidate, researchCandidate });

            Assert.Single(plan.Actions);
            Assert.Equal("item-research", plan.Actions[0].ItemId);
        }

        [Fact]
        public void Plan_WithinAPriorityClassHigherValueCandidatesArePlannedFirst()
        {
            // Both candidates are research-relevant, so class is tied. Capacity is 1 and the
            // lower-value item is listed first, but the higher-value item must still win.
            var smith = Smith("hero-1", stamina: 10, smeltEnergyCost: 10);
            var lowValue = Candidate("item-low", count: 5, value: 10, templateId: "tpl-a", templateHasUnopenedParts: true);
            var highValue = Candidate("item-high", count: 5, value: 500, templateId: "tpl-b", templateHasUnopenedParts: true);

            var plan = SmeltPlanner.Plan(new[] { smith }, new[] { lowValue, highValue });

            Assert.Single(plan.Actions);
            Assert.Equal("item-high", plan.Actions[0].ItemId);
        }

        [Fact]
        public void Plan_ResearchRelevantCandidatesGoToACuriousSmelterSmithWhenItHasCapacity()
        {
            // hero-plain has no perk and is listed FIRST; hero-curious has CuriousSmelter and is
            // listed SECOND. Both have identical capacity, but the single research-relevant
            // candidate must still be routed to the CuriousSmelter smith.
            var plainSmith = Smith("hero-plain", stamina: 10, smeltEnergyCost: 10);
            var curiousSmith = Smith("hero-curious", stamina: 10, smeltEnergyCost: 10, hasCuriousSmelter: true);
            var candidate = Candidate("item-research", count: 1, value: 100, templateId: "tpl", templateHasUnopenedParts: true);

            var plan = SmeltPlanner.Plan(new[] { plainSmith, curiousSmith }, new[] { candidate });

            Assert.Single(plan.Actions);
            Assert.Equal("hero-curious", plan.Actions[0].HeroId);
        }

        [Fact]
        public void Plan_BulkCandidatesPreferAPracticalSmelterSmithWhenItHasCapacity()
        {
            // hero-plain has no perk and is listed FIRST; hero-practical has PracticalSmelter and
            // is listed SECOND. Both have identical capacity, but the single bulk candidate must
            // still be routed to the PracticalSmelter smith.
            var plainSmith = Smith("hero-plain", stamina: 10, smeltEnergyCost: 10);
            var practicalSmith = Smith("hero-practical", stamina: 10, smeltEnergyCost: 10, hasPracticalSmelter: true);
            var candidate = Candidate("item-bulk", count: 1, value: 50, templateId: "tpl", templateHasUnopenedParts: false);

            var plan = SmeltPlanner.Plan(new[] { plainSmith, practicalSmith }, new[] { candidate });

            Assert.Single(plan.Actions);
            Assert.Equal("hero-practical", plan.Actions[0].HeroId);
        }

        [Fact]
        public void Plan_TotalActionsNeverExceedSumOfAllSmithsCapacities()
        {
            // Perked smiths and a mix of research/bulk candidates, all in abundant supply - the
            // Slice 1 capacity bound must survive the Slice 3 reordering exactly.
            var smith1 = Smith("hero-1", stamina: 13, smeltEnergyCost: 10, hasCuriousSmelter: true);
            var smith2 = Smith("hero-2", stamina: 27, smeltEnergyCost: 10, hasPracticalSmelter: true);
            var smiths = new[] { smith1, smith2 };
            var candidates = new[]
            {
                Candidate("item-research", count: 100, value: 50, templateId: "tpl-a", templateHasUnopenedParts: true),
                Candidate("item-bulk", count: 100, value: 20, templateId: "tpl-b", templateHasUnopenedParts: false),
            };

            var plan = SmeltPlanner.Plan(smiths, candidates);

            var sumOfCapacities = smiths.Sum(s => s.Capacity);
            Assert.True(plan.Actions.Count <= sumOfCapacities);
            Assert.Equal(sumOfCapacities, plan.Actions.Count);
        }

        [Fact]
        public void Plan_LockedCandidatesAreNeverPlannedRegardlessOfClassOrValue()
        {
            // The locked candidate is research-relevant and far more valuable than the unlocked
            // one - neither property may override the lock.
            var smith = Smith("hero-1", stamina: 100, smeltEnergyCost: 10);
            var lockedResearch = Candidate("item-locked", count: 5, isLocked: true, value: 1000, templateId: "tpl", templateHasUnopenedParts: true);
            var unlockedBulk = Candidate("item-free", count: 3, isLocked: false, value: 1, templateId: "tpl2", templateHasUnopenedParts: false);

            var plan = SmeltPlanner.Plan(new[] { smith }, new[] { lockedResearch, unlockedBulk });

            Assert.DoesNotContain(plan.Actions, a => a.ItemId == "item-locked");
            Assert.Equal(3, plan.Actions.Count(a => a.ItemId == "item-free"));
        }

        [Fact]
        public void Plan_WithNoPerkedSmithsThePlanIsStillValidWithNoStarvationAndNoCapacityOverrun()
        {
            // No perks anywhere and a single item type, so class/value routing cannot change
            // anything - this must degrade to exactly the Slice 1 capacity-bound distribution.
            var smith1 = Smith("hero-1", stamina: 13, smeltEnergyCost: 10);
            var smith2 = Smith("hero-2", stamina: 27, smeltEnergyCost: 10);
            var smiths = new[] { smith1, smith2 };
            var candidates = new[] { Candidate("item-sword", count: 100, value: 10, templateId: "tpl", templateHasUnopenedParts: false) };

            var plan = SmeltPlanner.Plan(smiths, candidates);

            Assert.Equal(1, plan.Actions.Count(a => a.HeroId == "hero-1"));
            Assert.Equal(2, plan.Actions.Count(a => a.HeroId == "hero-2"));
            Assert.Equal(3, plan.Actions.Count);
        }
    }
}
