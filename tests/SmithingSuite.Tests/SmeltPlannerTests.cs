using System.Collections.Generic;
using System.Linq;
using SmithingSuite.Logic;
using Xunit;

namespace SmithingSuite.Tests
{
    /// <summary>
    /// Red-state tests for the not-yet-implemented <c>SmithingSuite.Logic.SmeltPlanner</c>
    /// (SPEC.md TODO 2). Written first per TDD: these must fail to compile until
    /// <c>SmeltPlanner</c>, <c>SmeltPlan</c> and <c>SmeltAction</c> exist, and then must pass
    /// once the planner is implemented per SPEC.md acceptance criteria AC1-AC7.
    /// </summary>
    public class SmeltPlannerTests
    {
        private static SmithSnapshot Smith(string heroId, int stamina, int smeltEnergyCost)
            => new SmithSnapshot(heroId, name: heroId, stamina: stamina, maxStamina: 100, smeltEnergyCost: smeltEnergyCost);

        private static SmeltCandidate Candidate(string itemId, int count, bool isLocked = false)
            => new SmeltCandidate(itemId, name: itemId, count: count, isLocked: isLocked);

        [Fact]
        public void Plan_NeverAssignsMoreSmeltsThanStaminaAllows()
        {
            // Capacity = floor(25 / 10) = 2, even though supply and appetite both exceed that.
            var smith = Smith("hero-1", stamina: 25, smeltEnergyCost: 10);
            var candidates = new[] { Candidate("item-sword", count: 10) };

            var plan = SmeltPlanner.Plan(new[] { smith }, candidates);

            Assert.Equal(2, plan.Actions.Count);
            Assert.All(plan.Actions, a => Assert.Equal("hero-1", a.HeroId));
        }

        [Fact]
        public void Plan_ContinuesWithNextSmithWhenCapacityExhausted()
        {
            // hero-1 can afford exactly 1 smelt, hero-2 can afford 2. Supply is abundant, so the
            // planner must not stop after hero-1 runs dry - it must move on to hero-2.
            var smith1 = Smith("hero-1", stamina: 10, smeltEnergyCost: 10);
            var smith2 = Smith("hero-2", stamina: 20, smeltEnergyCost: 10);
            var candidates = new[] { Candidate("item-sword", count: 10) };

            var plan = SmeltPlanner.Plan(new[] { smith1, smith2 }, candidates);

            Assert.Equal(1, plan.Actions.Count(a => a.HeroId == "hero-1"));
            Assert.Equal(2, plan.Actions.Count(a => a.HeroId == "hero-2"));
            Assert.Equal(3, plan.Actions.Count);
        }

        [Fact]
        public void Plan_SmithBelowSmeltCostGetsZeroActionsAndDoesNotBlockLaterSmiths()
        {
            // hero-1's stamina (5) is below its own smelt cost (10) -> capacity 0. hero-2 must
            // still receive its full share; hero-1 must not block or consume anything.
            var smith1 = Smith("hero-1", stamina: 5, smeltEnergyCost: 10);
            var smith2 = Smith("hero-2", stamina: 20, smeltEnergyCost: 10);
            var candidates = new[] { Candidate("item-sword", count: 10) };

            var plan = SmeltPlanner.Plan(new[] { smith1, smith2 }, candidates);

            Assert.Empty(plan.Actions.Where(a => a.HeroId == "hero-1"));
            Assert.Equal(2, plan.Actions.Count(a => a.HeroId == "hero-2"));
        }

        [Fact]
        public void Plan_LockedCandidatesNeverAppearInPlan()
        {
            // Ample capacity and ample locked supply - the locked stack must never be touched,
            // while the unlocked stack is still planned normally.
            var smith = Smith("hero-1", stamina: 100, smeltEnergyCost: 10);
            var lockedCandidate = Candidate("item-locked", count: 5, isLocked: true);
            var unlockedCandidate = Candidate("item-free", count: 3, isLocked: false);

            var plan = SmeltPlanner.Plan(new[] { smith }, new[] { lockedCandidate, unlockedCandidate });

            Assert.DoesNotContain(plan.Actions, a => a.ItemId == "item-locked");
            Assert.Equal(3, plan.Actions.Count(a => a.ItemId == "item-free"));
        }

        [Fact]
        public void Plan_ItemPlannedAtMostAsOftenAsItExistsInRoster()
        {
            // Capacity (100) vastly exceeds the roster count (3) - the roster count is the
            // binding constraint, not the smith's stamina.
            var smith = Smith("hero-1", stamina: 1000, smeltEnergyCost: 10);
            var candidate = Candidate("item-sword", count: 3);

            var plan = SmeltPlanner.Plan(new[] { smith }, new[] { candidate });

            Assert.Equal(3, plan.Actions.Count);
            Assert.All(plan.Actions, a => Assert.Equal("item-sword", a.ItemId));
        }

        [Fact]
        public void Plan_NoSmithsYieldsEmptyPlanWithoutThrowing()
        {
            var candidates = new[] { Candidate("item-sword", count: 5) };

            var plan = SmeltPlanner.Plan(new SmithSnapshot[0], candidates);

            Assert.Empty(plan.Actions);
        }

        [Fact]
        public void Plan_NoCandidatesYieldsEmptyPlanWithoutThrowing()
        {
            var smiths = new[] { Smith("hero-1", stamina: 100, smeltEnergyCost: 10) };

            var plan = SmeltPlanner.Plan(smiths, new SmeltCandidate[0]);

            Assert.Empty(plan.Actions);
        }

        [Fact]
        public void Plan_TotalActionsNeverExceedSumOfAllSmithsCapacities()
        {
            // hero-1 capacity = floor(13/10) = 1, hero-2 capacity = floor(27/10) = 2 -> sum = 3.
            // Supply is abundant, so total actions must be bounded by, and here exactly equal
            // to, the sum of capacities.
            var smith1 = Smith("hero-1", stamina: 13, smeltEnergyCost: 10);
            var smith2 = Smith("hero-2", stamina: 27, smeltEnergyCost: 10);
            var smiths = new[] { smith1, smith2 };
            var candidates = new[] { Candidate("item-sword", count: 100) };

            var plan = SmeltPlanner.Plan(smiths, candidates);

            var sumOfCapacities = smiths.Sum(s => s.Capacity);
            Assert.True(plan.Actions.Count <= sumOfCapacities);
            Assert.Equal(sumOfCapacities, plan.Actions.Count);
        }
    }
}
