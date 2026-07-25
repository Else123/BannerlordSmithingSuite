using System.Collections.Generic;

namespace SmithingSuite.Logic
{
    /// <summary>
    /// One planned refine: a specific smith applying a specific formula (by index into that
    /// smith's own <see cref="SmithSnapshot.RefineFormulas"/>). Engine-free - the connector
    /// resolves the hero and re-indexes the hero's live formula list to execute it.
    /// </summary>
    public sealed class RefineAction
    {
        public string HeroId { get; }
        public int FormulaIndex { get; }

        public RefineAction(string heroId, int formulaIndex)
        {
            HeroId = heroId;
            FormulaIndex = formulaIndex;
        }
    }

    /// <summary>
    /// The ordered result of <see cref="RefinePlanner.Plan"/>: every refine action the connector
    /// should execute, in the order it should execute them.
    /// </summary>
    public sealed class RefinePlan
    {
        public IReadOnlyList<RefineAction> Actions { get; }

        public RefinePlan(IReadOnlyList<RefineAction> actions)
        {
            Actions = actions;
        }
    }
}
