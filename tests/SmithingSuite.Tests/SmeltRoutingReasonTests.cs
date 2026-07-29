using System.Linq;
using SmithingSuite.Logic;
using Xunit;

namespace SmithingSuite.Tests
{
    /// <summary>
    /// Tests for <c>SmeltAction.Reason</c> (Refactor B): pins that <c>SmeltPlanner</c> states its
    /// own routing decision at the moment of assignment, for every situation the previous
    /// (deleted) <c>SmithingBehavior.DescribeSmeltRoutingReason</c> used to reconstruct from the
    /// outside - including the distinction between "no perked smith exists in the party" and "the
    /// perked smith was at capacity", which a prior review found stated wrongly when it was
    /// reconstructed rather than recorded by construction.
    /// </summary>
    public class SmeltRoutingReasonTests
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

        private static SmeltCandidate ResearchCandidate(string itemId, int count)
            => new SmeltCandidate(itemId, name: itemId, count: count, isLocked: false, value: 10, templateId: "tpl", templateHasUnopenedParts: true);

        private static SmeltCandidate BulkCandidate(string itemId, int count)
            => new SmeltCandidate(itemId, name: itemId, count: count, isLocked: false, value: 10, templateId: "tpl", templateHasUnopenedParts: false);

        [Fact]
        public void ResearchRelevantCandidateAssignedToCuriousSmelterSmith_IsCuriousSmelterPreferred()
        {
            var curiousSmith = Smith("hero-1", stamina: 10, smeltEnergyCost: 10, hasCuriousSmelter: true);

            var plan = SmeltPlanner.Plan(new[] { curiousSmith }, new[] { ResearchCandidate("item", 1) });

            Assert.Equal(SmeltRoutingReason.CuriousSmelterPreferred, plan.Actions.Single().Reason);
        }

        [Fact]
        public void BulkCandidateAssignedToPracticalSmelterSmith_IsPracticalSmelterPreferred()
        {
            var practicalSmith = Smith("hero-1", stamina: 10, smeltEnergyCost: 10, hasPracticalSmelter: true);

            var plan = SmeltPlanner.Plan(new[] { practicalSmith }, new[] { BulkCandidate("item", 1) });

            Assert.Equal(SmeltRoutingReason.PracticalSmelterPreferred, plan.Actions.Single().Reason);
        }

        [Fact]
        public void ResearchRelevantCandidate_WithNoCuriousSmelterAnywhereInParty_IsFallbackNoPerkedSmith()
        {
            // Neither smith has CuriousSmelter - the perk does not exist in this roster at all.
            var plainSmith1 = Smith("hero-1", stamina: 10, smeltEnergyCost: 10);
            var plainSmith2 = Smith("hero-2", stamina: 10, smeltEnergyCost: 10);

            var plan = SmeltPlanner.Plan(new[] { plainSmith1, plainSmith2 }, new[] { ResearchCandidate("item", 1) });

            Assert.Equal(SmeltRoutingReason.FallbackNoPerkedSmith, plan.Actions.Single().Reason);
        }

        [Fact]
        public void ResearchRelevantCandidate_WithCuriousSmelterSmithAtCapacity_IsFallbackPerkedSmithAtCapacity()
        {
            // hero-curious HAS CuriousSmelter but has zero capacity (stamina below its own smelt
            // cost); hero-plain has no perk but does have capacity. The candidate must fall
            // through to hero-plain, and the reason must say "at capacity", not "no perked smith" -
            // the perk DOES exist in the party, it is simply busy/out of capacity.
            var curiousAtCapacity = Smith("hero-curious", stamina: 5, smeltEnergyCost: 10, hasCuriousSmelter: true);
            var plainWithCapacity = Smith("hero-plain", stamina: 10, smeltEnergyCost: 10);

            var plan = SmeltPlanner.Plan(new[] { curiousAtCapacity, plainWithCapacity }, new[] { ResearchCandidate("item", 1) });

            var action = Assert.Single(plan.Actions);
            Assert.Equal("hero-plain", action.HeroId);
            Assert.Equal(SmeltRoutingReason.FallbackPerkedSmithAtCapacity, action.Reason);
        }

        [Fact]
        public void BulkCandidate_WithNoPracticalSmelterAnywhereInParty_IsFallbackNoPerkedSmith()
        {
            var plainSmith = Smith("hero-1", stamina: 10, smeltEnergyCost: 10);

            var plan = SmeltPlanner.Plan(new[] { plainSmith }, new[] { BulkCandidate("item", 1) });

            Assert.Equal(SmeltRoutingReason.FallbackNoPerkedSmith, plan.Actions.Single().Reason);
        }

        [Fact]
        public void BulkCandidate_WithPracticalSmelterSmithAtCapacity_IsFallbackPerkedSmithAtCapacity()
        {
            var practicalAtCapacity = Smith("hero-practical", stamina: 5, smeltEnergyCost: 10, hasPracticalSmelter: true);
            var plainWithCapacity = Smith("hero-plain", stamina: 10, smeltEnergyCost: 10);

            var plan = SmeltPlanner.Plan(new[] { practicalAtCapacity, plainWithCapacity }, new[] { BulkCandidate("item", 1) });

            var action = Assert.Single(plan.Actions);
            Assert.Equal("hero-plain", action.HeroId);
            Assert.Equal(SmeltRoutingReason.FallbackPerkedSmithAtCapacity, action.Reason);
        }
    }
}
