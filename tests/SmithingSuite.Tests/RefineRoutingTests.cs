using SmithingSuite.Logic;
using Xunit;

namespace SmithingSuite.Tests
{
    /// <summary>
    /// Red-state test for the not-yet-implemented Slice 3 refine routing policy
    /// (SPEC-SLICE3.md TODO 5, ahead of the TODO 6 implementation). Specifies AC8: smiths with
    /// <c>HasPracticalRefiner</c> are processed before others, so their actions appear first in
    /// the plan even when they are listed later in the input. The refine policy itself
    /// (charcoal-first, then highest tier - AC9) is unchanged and stays covered by the untouched
    /// Slice 2 tests in <c>RefinePlannerTests</c>.
    /// </summary>
    public class RefineRoutingTests
    {
        private const int Wood = 7;
        private const int Charcoal = 8;

        // Single-input formula (dummy Iron1, 0 second input) so a lone smith with a small
        // amount of wood can produce one observable action without needing charcoal in stock.
        private static RefineFormulaSnapshot WoodToCharcoal(int index = 0)
            => new RefineFormulaSnapshot(
                formulaIndex: index,
                input1: Wood, input1Count: 2,
                input2: 1, input2Count: 0,
                output: Charcoal, outputCount: 3,
                output2: 0, output2Count: 0);

        private static SmithSnapshot Smith(string heroId, int stamina, int refineEnergyCost, bool hasPracticalRefiner, params RefineFormulaSnapshot[] formulas)
            => new SmithSnapshot(
                heroId,
                name: heroId,
                stamina: stamina,
                maxStamina: 100,
                smeltEnergyCost: 10,
                refineEnergyCost: refineEnergyCost,
                refineFormulas: formulas,
                hasCuriousSmelter: false,
                hasPracticalSmelter: false,
                hasPracticalRefiner: hasPracticalRefiner);

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
        public void Plan_ProcessesPracticalRefinerSmithsBeforeOthers()
        {
            // hero-plain has no perk and is listed FIRST; hero-perked has PracticalRefiner and is
            // listed SECOND. Both can afford exactly one action with abundant wood, so the only
            // way to tell who went first is the order of actions in the resulting plan - the
            // perked smith must appear first despite being listed second.
            var formula = WoodToCharcoal();
            var plainSmith = Smith("hero-plain", stamina: 6, refineEnergyCost: 6, hasPracticalRefiner: false, formula);
            var perkedSmith = Smith("hero-perked", stamina: 6, refineEnergyCost: 6, hasPracticalRefiner: true, formula);
            var stock = Stock((Wood, 10));

            var plan = RefinePlanner.Plan(new[] { plainSmith, perkedSmith }, stock, new MaterialStock());

            Assert.Equal(2, plan.Actions.Count);
            Assert.Equal("hero-perked", plan.Actions[0].HeroId);
            Assert.Equal("hero-plain", plan.Actions[1].HeroId);
        }
    }
}
