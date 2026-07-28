using System.Collections.Generic;
using SmithingSuite.Logic;
using Xunit;

namespace SmithingSuite.Tests
{
    /// <summary>
    /// Red tests for Slice 6 "A. Phase stamina budget" (<see cref="SmithSnapshot.WithStaminaBudget"/>).
    /// Covers AC1-AC5 from SPEC-SLICE6.md: the smelt phase must plan against a snapshot whose
    /// stamina has been scaled down to its share, while every other field - and the safety
    /// property that a budget can never GRANT more stamina than the smith actually has - stays
    /// intact.
    /// </summary>
    public class StaminaBudgetTests
    {
        private static SmithSnapshot FullSmith(int stamina)
        {
            var formulas = new List<RefineFormulaSnapshot>
            {
                new RefineFormulaSnapshot(
                    formulaIndex: 0,
                    input1: 7, input1Count: 2,
                    input2: 1, input2Count: 0,
                    output: 8, outputCount: 3,
                    output2: 0, output2Count: 0),
            };

            return new SmithSnapshot(
                heroId: "hero-1",
                name: "Ferrin",
                stamina: stamina,
                maxStamina: 200,
                smeltEnergyCost: 10,
                refineEnergyCost: 6,
                refineFormulas: formulas,
                hasCuriousSmelter: true,
                hasPracticalSmelter: true,
                hasPracticalRefiner: true);
        }

        [Fact]
        public void WithStaminaBudget_ReturnsCopy_WithReducedStamina_AndEveryOtherFieldIntact()
        {
            // AC1
            SmithSnapshot smith = FullSmith(stamina: 134);

            SmithSnapshot budgeted = smith.WithStaminaBudget(67);

            Assert.Equal(67, budgeted.Stamina);
            Assert.Equal(smith.HeroId, budgeted.HeroId);
            Assert.Equal(smith.Name, budgeted.Name);
            Assert.Equal(smith.MaxStamina, budgeted.MaxStamina);
            Assert.Equal(smith.SmeltEnergyCost, budgeted.SmeltEnergyCost);
            Assert.Equal(smith.RefineEnergyCost, budgeted.RefineEnergyCost);
            Assert.Same(smith.RefineFormulas, budgeted.RefineFormulas);
            Assert.Equal(smith.HasCuriousSmelter, budgeted.HasCuriousSmelter);
            Assert.Equal(smith.HasPracticalSmelter, budgeted.HasPracticalSmelter);
            Assert.Equal(smith.HasPracticalRefiner, budgeted.HasPracticalRefiner);
        }

        [Fact]
        public void WithStaminaBudget_FiftyPercentShare_HalvesSmeltCapacity_UsingFloor()
        {
            // AC2 - stamina 134, cost 10: full capacity 13, budgeted-to-67 capacity 6 (floor of 6.7).
            SmithSnapshot smith = FullSmith(stamina: 134);
            Assert.Equal(13, smith.Capacity);

            int smeltShareStamina = smith.Stamina * 50 / 100;
            Assert.Equal(67, smeltShareStamina);

            SmithSnapshot budgeted = smith.WithStaminaBudget(smeltShareStamina);

            Assert.Equal(67, budgeted.Stamina);
            Assert.Equal(6, budgeted.Capacity);
        }

        [Fact]
        public void WithStaminaBudget_ZeroPercentShare_YieldsZeroCapacity_AndDoesNotThrow()
        {
            // AC3
            SmithSnapshot smith = FullSmith(stamina: 134);

            int smeltShareStamina = smith.Stamina * 0 / 100;
            SmithSnapshot budgeted = smith.WithStaminaBudget(smeltShareStamina);

            Assert.Equal(0, budgeted.Stamina);
            Assert.Equal(0, budgeted.Capacity);
        }

        [Fact]
        public void WithStaminaBudget_HundredPercentShare_ReproducesUnbudgetedCapacityExactly()
        {
            // AC4 - regression guard: a 100% share must be indistinguishable from no budgeting.
            SmithSnapshot smith = FullSmith(stamina: 134);

            int smeltShareStamina = smith.Stamina * 100 / 100;
            SmithSnapshot budgeted = smith.WithStaminaBudget(smeltShareStamina);

            Assert.Equal(smith.Stamina, budgeted.Stamina);
            Assert.Equal(smith.Capacity, budgeted.Capacity);
        }

        [Fact]
        public void WithStaminaBudget_NegativeInput_ClampsToZero_NeverNegativeStamina()
        {
            // AC5 - an out-of-range share (clamped to a negative stamina figure upstream, or a
            // caller bug) can never leave the snapshot with negative stamina.
            SmithSnapshot smith = FullSmith(stamina: 134);

            SmithSnapshot budgeted = smith.WithStaminaBudget(-50);

            Assert.Equal(0, budgeted.Stamina);
            Assert.Equal(0, budgeted.Capacity);
        }

        [Fact]
        public void WithStaminaBudget_InputAboveOriginalStamina_ClampsToOriginalStamina_NeverGrantsMore()
        {
            // AC5 - a budget can never GRANT more stamina than the smith actually has, e.g. a
            // >100% share or a stale/adversarial caller value.
            SmithSnapshot smith = FullSmith(stamina: 134);

            SmithSnapshot budgeted = smith.WithStaminaBudget(999);

            Assert.Equal(134, budgeted.Stamina);
            Assert.Equal(smith.Capacity, budgeted.Capacity);
        }

        // The tests below pin SmithSnapshot.ForSmeltShare - the clamp-and-floor share arithmetic
        // moved from SmithingBehavior into this engine-free layer (code review Slice 6 finding
        // C), so a regression in it now fails a test the project actually compiles and runs.

        [Fact]
        public void ForSmeltShare_NegativeShare_ClampsToZero_YieldsZeroCapacity()
        {
            SmithSnapshot smith = FullSmith(stamina: 134);

            SmithSnapshot budgeted = SmithSnapshot.ForSmeltShare(smith, -50);

            Assert.Equal(0, budgeted.Stamina);
            Assert.Equal(0, budgeted.Capacity);
        }

        [Fact]
        public void ForSmeltShare_ShareAboveHundred_ClampsToHundred_NeverGrantsMoreThanFullStamina()
        {
            // A >100% share must never hand smelting more stamina than the smith actually has -
            // same effective result as an unbudgeted (100%) snapshot.
            SmithSnapshot smith = FullSmith(stamina: 134);

            SmithSnapshot budgeted = SmithSnapshot.ForSmeltShare(smith, 150);

            Assert.Equal(smith.Stamina, budgeted.Stamina);
            Assert.Equal(smith.Capacity, budgeted.Capacity);
        }

        [Fact]
        public void ForSmeltShare_FiftyPercent_FloorsTheBudgetedStamina()
        {
            // stamina 134, cost 10: 50% share is 67 (floor of 67.0 - exact here), capacity floor(6.7) = 6.
            SmithSnapshot smith = FullSmith(stamina: 134);
            Assert.Equal(13, smith.Capacity);

            SmithSnapshot budgeted = SmithSnapshot.ForSmeltShare(smith, 50);

            Assert.Equal(67, budgeted.Stamina);
            Assert.Equal(6, budgeted.Capacity);
        }
    }
}
