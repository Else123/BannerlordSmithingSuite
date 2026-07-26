using SmithingSuite.Logic;
using Xunit;

namespace SmithingSuite.Tests
{
    /// <summary>
    /// Direct edge-case tests for <see cref="SmithSnapshot"/>. <see cref="SmithSnapshot.Capacity"/>
    /// is the guard that stops infinite smelting (see its XML doc) but is only ever exercised
    /// indirectly through <c>SmeltPlannerTests</c>; these tests pin its own edges explicitly.
    /// </summary>
    public class SmithSnapshotTests
    {
        private static SmithSnapshot Smith(int stamina, int smeltEnergyCost)
            => new SmithSnapshot("hero-1", name: "hero-1", stamina: stamina, maxStamina: 100, smeltEnergyCost: smeltEnergyCost);

        private static SmithSnapshot SmithForRefine(int stamina, int refineEnergyCost)
            => new SmithSnapshot("hero-1", name: "hero-1", stamina: stamina, maxStamina: 100,
                smeltEnergyCost: 10, refineEnergyCost: refineEnergyCost, refineFormulas: null);

        [Fact]
        public void Capacity_IsZero_WhenSmeltEnergyCostIsZero()
        {
            // The anti-"infinite smelts via division by zero" guard - the single most
            // safety-critical line in the Logic layer.
            var smith = Smith(stamina: 100, smeltEnergyCost: 0);

            Assert.Equal(0, smith.Capacity);
        }

        [Fact]
        public void Capacity_IsZero_WhenSmeltEnergyCostIsNegative()
        {
            var smith = Smith(stamina: 100, smeltEnergyCost: -10);

            Assert.Equal(0, smith.Capacity);
        }

        [Fact]
        public void Stamina_IsClampedToZero_WhenConstructedWithNegativeStamina()
        {
            var smith = Smith(stamina: -5, smeltEnergyCost: 10);

            Assert.Equal(0, smith.Stamina);
            Assert.Equal(0, smith.Capacity);
        }

        [Fact]
        public void Capacity_UsesIntegerDivision_TwentyFiveStaminaAtCostTenGivesTwoNotTwoPointFive()
        {
            var smith = Smith(stamina: 25, smeltEnergyCost: 10);

            Assert.Equal(2, smith.Capacity);
        }

        [Fact]
        public void RefineCapacity_IsZero_WhenRefineEnergyCostIsZero()
        {
            // Same anti-"infinite refines via division by zero" guard as Capacity, applied to
            // the refine cost.
            var smith = SmithForRefine(stamina: 100, refineEnergyCost: 0);

            Assert.Equal(0, smith.RefineCapacity);
        }

        [Fact]
        public void RefineCapacity_IsZero_WhenRefineEnergyCostIsNegative()
        {
            var smith = SmithForRefine(stamina: 100, refineEnergyCost: -6);

            Assert.Equal(0, smith.RefineCapacity);
        }

        [Fact]
        public void RefineCapacity_UsesIntegerDivision_TwentyFiveStaminaAtCostSixGivesFourNotFourPointOhSixSeven()
        {
            var smith = SmithForRefine(stamina: 25, refineEnergyCost: 6);

            Assert.Equal(4, smith.RefineCapacity);
        }
    }
}
