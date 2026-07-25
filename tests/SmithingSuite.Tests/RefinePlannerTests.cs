using System.Linq;
using SmithingSuite.Logic;
using Xunit;

namespace SmithingSuite.Tests
{
    /// <summary>
    /// Red-state tests for the not-yet-implemented <c>SmithingSuite.Logic.RefinePlanner</c>
    /// (SPEC-SLICE2.md TODO 3). Written first per TDD: these must fail to compile until
    /// <c>RefinePlanner</c>, <c>RefinePlan</c> and <c>RefineAction</c> exist, and then must pass
    /// once the planner is implemented per SPEC-SLICE2.md acceptance criteria AC1-AC9.
    ///
    /// Fixtures mirror the real vanilla refining chain (SPEC-SLICE2.md Kontext), decompiled from
    /// <c>DefaultSmithingModel.GetRefiningFormulas</c>. Material ordinals match
    /// <c>CraftingMaterials</c>: IronOre=0, Iron1=1 .. Iron6=6, Wood=7, Charcoal=8.
    /// </summary>
    public class RefinePlannerTests
    {
        private const int IronOre = 0;
        private const int Iron1 = 1;
        private const int Iron2 = 2;
        private const int Iron3 = 3;
        private const int Wood = 7;
        private const int Charcoal = 8;

        // index 0 (default): Wood 2 -> Charcoal 3. Single-input: Input2Count == 0 (dummy Iron1, 0).
        private static RefineFormulaSnapshot WoodToCharcoal(int index = 0)
            => new RefineFormulaSnapshot(
                formulaIndex: index,
                input1: Wood, input1Count: 2,
                input2: Iron1, input2Count: 0,
                output: Charcoal, outputCount: 3,
                output2: 0, output2Count: 0);

        // index 1 (default): IronOre 1 + Charcoal 1 -> Iron1 2.
        private static RefineFormulaSnapshot OreToIron1(int index = 1)
            => new RefineFormulaSnapshot(
                formulaIndex: index,
                input1: IronOre, input1Count: 1,
                input2: Charcoal, input2Count: 1,
                output: Iron1, outputCount: 2,
                output2: 0, output2Count: 0);

        // index 2 (default): Iron1 1 + Charcoal 1 -> Iron2 1.
        private static RefineFormulaSnapshot Iron1ToIron2(int index = 2)
            => new RefineFormulaSnapshot(
                formulaIndex: index,
                input1: Iron1, input1Count: 1,
                input2: Charcoal, input2Count: 1,
                output: Iron2, outputCount: 1,
                output2: 0, output2Count: 0);

        // index 3 (default): Iron2 2 + Charcoal 1 -> Iron3 1 + Iron1 1.
        private static RefineFormulaSnapshot Iron2ToIron3(int index = 3)
            => new RefineFormulaSnapshot(
                formulaIndex: index,
                input1: Iron2, input1Count: 2,
                input2: Charcoal, input2Count: 1,
                output: Iron3, outputCount: 1,
                output2: Iron1, output2Count: 1);

        private static SmithSnapshot Smith(string heroId, int stamina, int refineEnergyCost, params RefineFormulaSnapshot[] formulas)
            => new SmithSnapshot(
                heroId, name: heroId, stamina: stamina, maxStamina: 100,
                smeltEnergyCost: 10, refineEnergyCost: refineEnergyCost, refineFormulas: formulas);

        private static MaterialStock Stock(params (int material, int count)[] entries)
        {
            var stock = new MaterialStock();
            foreach (var entry in entries)
            {
                stock.Set(entry.material, entry.count);
            }

            return stock;
        }

        [Fact]
        public void Plan_NeverPlansAnActionWhoseInputsAreNotInStock()
        {
            // Only formula available needs IronOre + Charcoal; stock has neither.
            var smith = Smith("hero-1", stamina: 100, refineEnergyCost: 6, OreToIron1());
            var stock = Stock();

            var plan = RefinePlanner.Plan(new[] { smith }, stock, new MaterialStock());

            Assert.Empty(plan.Actions);
        }

        [Fact]
        public void Plan_PerSmithActionCountNeverExceedsFloorOfStaminaOverRefineEnergyCost()
        {
            // Capacity = floor(25 / 10) = 2. Only a single-input formula and abundant wood, so
            // stamina - not supply - is the binding constraint.
            var smith = Smith("hero-1", stamina: 25, refineEnergyCost: 10, WoodToCharcoal());
            var stock = Stock((Wood, 1000));

            var plan = RefinePlanner.Plan(new[] { smith }, stock, new MaterialStock());

            Assert.Equal(2, plan.Actions.Count);
            Assert.All(plan.Actions, a => Assert.Equal("hero-1", a.HeroId));
        }

        [Fact]
        public void Plan_LaterActionSeesStockChangesFromEarlierAction()
        {
            // Charcoal starts at 0, so ore->iron1 is only applicable AFTER wood->charcoal has run
            // and produced charcoal. Proves stock is threaded between simulated actions.
            var woodToCharcoal = WoodToCharcoal(index: 0);
            var oreToIron1 = OreToIron1(index: 1);
            var smith = Smith("hero-1", stamina: 12, refineEnergyCost: 6, woodToCharcoal, oreToIron1);
            var stock = Stock((Wood, 2), (IronOre, 1));

            var plan = RefinePlanner.Plan(new[] { smith }, stock, new MaterialStock());

            Assert.Equal(2, plan.Actions.Count);
            Assert.Equal(0, plan.Actions[0].FormulaIndex);
            Assert.Equal(1, plan.Actions[1].FormulaIndex);
        }

        [Fact]
        public void Plan_PlansWoodToCharcoalBeforeIronFormulaWhenCharcoalIsZero()
        {
            // Formulas are deliberately listed iron-first on the smith to prove the ordering
            // comes from the charcoal-aware policy, not from list order.
            var oreToIron1 = OreToIron1(index: 0);
            var woodToCharcoal = WoodToCharcoal(index: 1);
            var smith = Smith("hero-1", stamina: 12, refineEnergyCost: 6, oreToIron1, woodToCharcoal);
            var stock = Stock((IronOre, 1), (Wood, 2));

            var plan = RefinePlanner.Plan(new[] { smith }, stock, new MaterialStock());

            Assert.NotEmpty(plan.Actions);
            Assert.Equal(1, plan.Actions[0].FormulaIndex);
        }

        [Fact]
        public void Plan_NeverBreachesAMinimumStockFloor()
        {
            // Charcoal is at exactly the floor; consuming it for ore->iron1 would push it below
            // the floor, so the action must not be planned even though stock nominally "has" it.
            var oreToIron1 = OreToIron1();
            var smith = Smith("hero-1", stamina: 100, refineEnergyCost: 6, oreToIron1);
            var stock = Stock((IronOre, 5), (Charcoal, 1));
            var floors = Stock((Charcoal, 1));

            var plan = RefinePlanner.Plan(new[] { smith }, stock, floors);

            Assert.Empty(plan.Actions);
        }

        [Fact]
        public void Plan_PrefersHigherTierFormulaOverLowerTierWhenBothApplicable()
        {
            // Both ore->iron1 (tier 1) and iron1->iron2 (tier 2) are applicable but stamina only
            // affords one action; the higher tier must win.
            var oreToIron1 = OreToIron1(index: 0);
            var iron1ToIron2 = Iron1ToIron2(index: 1);
            var smith = Smith("hero-1", stamina: 6, refineEnergyCost: 6, oreToIron1, iron1ToIron2);
            var stock = Stock((IronOre, 1), (Iron1, 1), (Charcoal, 2));

            var plan = RefinePlanner.Plan(new[] { smith }, stock, new MaterialStock());

            Assert.Single(plan.Actions);
            Assert.Equal(1, plan.Actions[0].FormulaIndex);
        }

        [Fact]
        public void Plan_EmptySmithsOrEmptyStockOrEmptyFormulaListYieldsEmptyPlanWithoutThrowing()
        {
            var formula = OreToIron1();
            var abundantStock = Stock((IronOre, 10), (Charcoal, 10));
            var floors = new MaterialStock();

            var noSmithsPlan = RefinePlanner.Plan(new SmithSnapshot[0], abundantStock, floors);
            Assert.Empty(noSmithsPlan.Actions);

            var smithWithFormula = Smith("hero-1", stamina: 100, refineEnergyCost: 6, formula);
            var emptyStockPlan = RefinePlanner.Plan(new[] { smithWithFormula }, new MaterialStock(), floors);
            Assert.Empty(emptyStockPlan.Actions);

            var smithWithoutFormulas = Smith("hero-1", stamina: 100, refineEnergyCost: 6);
            var noFormulasPlan = RefinePlanner.Plan(new[] { smithWithoutFormulas }, abundantStock, floors);
            Assert.Empty(noFormulasPlan.Actions);
        }

        [Fact]
        public void Plan_TreatsFormulaWithZeroInput2CountAsSingleInputNotRequiringIron1()
        {
            // Wood->charcoal declares a dummy Iron1 second input with count 0. Iron1 is
            // deliberately absent from stock to prove it is not treated as a requirement.
            var woodToCharcoal = WoodToCharcoal();
            var smith = Smith("hero-1", stamina: 6, refineEnergyCost: 6, woodToCharcoal);
            var stock = Stock((Wood, 2));

            var plan = RefinePlanner.Plan(new[] { smith }, stock, new MaterialStock());

            Assert.Single(plan.Actions);
            Assert.Equal(0, plan.Actions[0].FormulaIndex);
        }

        [Fact]
        public void Plan_TerminatesWhenFormulasFormAMaterialCycleAndIsBoundedByStaminaCapacity()
        {
            // Iron1->Iron2->Iron3(+Iron1) is a material cycle: Iron2->Iron3 regenerates the very
            // Iron1 that Iron1->Iron2 consumes. With abundant charcoal and iron1, a stock-only
            // loop guard would spin forever - only the stamina bound may stop it.
            var iron1ToIron2 = Iron1ToIron2(index: 0);
            var iron2ToIron3 = Iron2ToIron3(index: 1);
            var smith = Smith("hero-1", stamina: 60, refineEnergyCost: 6, iron1ToIron2, iron2ToIron3);
            var stock = Stock((Iron1, 100), (Charcoal, 1000));
            var smiths = new[] { smith };

            var plan = RefinePlanner.Plan(smiths, stock, new MaterialStock());

            var totalCapacity = smiths.Sum(s => s.RefineCapacity);
            Assert.True(plan.Actions.Count <= totalCapacity);
        }
    }
}
