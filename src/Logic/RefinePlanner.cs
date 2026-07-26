using System.Collections.Generic;
using System.Linq;

namespace SmithingSuite.Logic
{
    /// <summary>
    /// Turns a snapshot of smiths and a starting material stock into an ordered
    /// <see cref="RefinePlan"/>. Pure arithmetic over the snapshot - no engine types, no I/O.
    /// Mirrors what the connector will later execute one <c>DoRefinement</c> call at a time.
    /// </summary>
    public static class RefinePlanner
    {
        /// <summary>Ordinal of <c>CraftingMaterials.Charcoal</c> - see RefineFormulaSnapshot.</summary>
        private const int Charcoal = 8;

        public static RefinePlan Plan(IReadOnlyList<SmithSnapshot> smiths, MaterialStock stock, MaterialStock floors)
        {
            var actions = new List<RefineAction>();

            // Simulate on a clone - the caller's stock must never be mutated by planning.
            var workingStock = (stock ?? new MaterialStock()).Clone();

            // A cheaper refine cost turns the same stamina into more actions, so smiths with
            // PracticalRefiner get first crack at the shared stock. OrderByDescending is a
            // stable sort, so smiths without the perk keep their original relative order among
            // themselves - the refine policy body below is untouched by this reordering.
            var orderedSmiths = (smiths ?? new SmithSnapshot[0])
                .OrderByDescending(smith => smith.HasPracticalRefiner);

            foreach (var smith in orderedSmiths)
            {
                // The per-smith loop is bounded by RefineCapacity (itself Stamina / RefineEnergyCost,
                // a finite value), and the smith list itself is finite. That bound - not a stock-based
                // guard - is what makes this safe: the iron tier chain (Iron1->Iron2->Iron3+Iron1) is a
                // material cycle, so a loop that only stopped when no formula applied could otherwise
                // spin forever feeding itself back a fresh Iron1 every pass.
                for (var performed = 0; performed < smith.RefineCapacity; performed++)
                {
                    var remainingCapacity = smith.RefineCapacity - performed;
                    var formula = SelectFormula(smith, workingStock, floors, remainingCapacity);
                    if (formula == null)
                    {
                        // Nothing this smith can currently apply - move on to the next smith
                        // instead of aborting the whole plan.
                        break;
                    }

                    actions.Add(new RefineAction(smith.HeroId, formula.FormulaIndex));
                    workingStock.Apply(formula);
                }
            }

            return new RefinePlan(actions);
        }

        /// <summary>
        /// Picks the next formula for this smith per the Slice 2 policy: feed the charcoal
        /// pipeline first while charcoal is low enough to run this smith dry before it can
        /// finish its remaining refines, otherwise prefer the highest tier, breaking ties by the
        /// lower formula index for a reproducible plan.
        /// </summary>
        private static RefineFormulaSnapshot SelectFormula(SmithSnapshot smith, MaterialStock stock, MaterialStock floors, int remainingCapacity)
        {
            var applicable = smith.RefineFormulas.Where(f => stock.CanApply(f, floors)).ToList();
            if (applicable.Count == 0)
            {
                return null;
            }

            // Every iron formula burns exactly one charcoal per action, so if the current stock
            // cannot cover every refine this smith still has capacity for, it will run dry before
            // finishing - top the charcoal pipeline up first. Once charcoal covers the remaining
            // capacity, fall back to the normal highest-tier preference.
            var charcoalProducers = applicable.Where(ProducesCharcoal).ToList();
            var candidates = stock.Get(Charcoal) < remainingCapacity && charcoalProducers.Count > 0
                ? charcoalProducers
                : applicable;

            return candidates
                .OrderByDescending(f => f.HighestOutputTier)
                .ThenBy(f => f.FormulaIndex)
                .First();
        }

        private static bool ProducesCharcoal(RefineFormulaSnapshot formula)
        {
            return (formula.Output == Charcoal && formula.OutputCount > 0)
                || (formula.Output2 == Charcoal && formula.Output2Count > 0);
        }
    }
}
