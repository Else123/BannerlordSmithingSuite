namespace SmithingSuite.Logic
{
    /// <summary>
    /// An engine-free projection of one vanilla <c>Crafting.RefiningFormula</c>, captured at
    /// planning time. Materials are <c>int</c> ordinals matching vanilla <c>CraftingMaterials</c>:
    /// IronOre=0, Iron1=1, Iron2=2, Iron3=3, Iron4=4, Iron5=5, Iron6=6, Wood=7, Charcoal=8.
    /// </summary>
    public sealed class RefineFormulaSnapshot
    {
        /// <summary>
        /// Position of this formula within the owning hero's formula list (as returned by
        /// <c>GetRefiningFormulas</c>). Formulas are per-hero and perk-gated, so there is no
        /// stable global id - the connector maps a planned action back to the real formula by
        /// re-resolving the hero's list and indexing into it.
        /// </summary>
        public int FormulaIndex { get; }

        public int Input1 { get; }
        public int Input1Count { get; }
        public int Input2 { get; }
        public int Input2Count { get; }
        public int Output { get; }
        public int OutputCount { get; }
        public int Output2 { get; }
        public int Output2Count { get; }

        /// <summary>
        /// Vanilla declares a dummy <c>Iron1, 0</c> as the second input on the wood-&gt;charcoal
        /// formula. A zero count means "no second input", not "requires zero" - model that
        /// explicitly here instead of leaving every caller to remember the convention.
        /// </summary>
        public bool HasSecondInput => Input2Count > 0;

        /// <summary>
        /// Highest iron ordinal produced by this formula (1..6), or 0 if it produces no iron at
        /// all (e.g. wood -> charcoal). Used by the "prefer the highest tier" planning rule.
        ///
        /// This is deliberately NOT <c>Math.Max(Output, Output2)</c>: Wood (7) and Charcoal (8)
        /// have higher ordinals than Iron6 (6) but are not higher tiers than steel - a naive max
        /// over the raw ordinals would rank a wood/charcoal formula above the top iron formulas
        /// and wreck the preference order. Restricting the max to the iron range (1..6) fixes that.
        /// </summary>
        public int HighestOutputTier
        {
            get
            {
                var tier = 0;
                if (Output >= 1 && Output <= 6 && Output > tier)
                {
                    tier = Output;
                }

                if (Output2 >= 1 && Output2 <= 6 && Output2 > tier)
                {
                    tier = Output2;
                }

                return tier;
            }
        }

        public RefineFormulaSnapshot(
            int formulaIndex,
            int input1,
            int input1Count,
            int input2,
            int input2Count,
            int output,
            int outputCount,
            int output2,
            int output2Count)
        {
            FormulaIndex = formulaIndex;
            Input1 = input1;
            Input1Count = input1Count;
            Input2 = input2;
            Input2Count = input2Count;
            Output = output;
            OutputCount = outputCount;
            Output2 = output2;
            Output2Count = output2Count;
        }
    }
}
