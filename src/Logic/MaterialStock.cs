namespace SmithingSuite.Logic
{
    /// <summary>
    /// A mutable per-material counter used while simulating a refine plan. Backed by one slot per
    /// vanilla <c>CraftingMaterials</c> ordinal (IronOre=0 .. Charcoal=8). Kept separate from
    /// <see cref="RefineFormulaSnapshot"/> so planning can simulate stock changes without mutating
    /// anything the caller owns.
    /// </summary>
    public sealed class MaterialStock
    {
        private const int MaterialCount = 9;

        private readonly int[] _counts;

        public MaterialStock()
        {
            _counts = new int[MaterialCount];
        }

        private MaterialStock(int[] counts)
        {
            _counts = counts;
        }

        private static bool IsInRange(int material) => material >= 0 && material < MaterialCount;

        /// <summary>
        /// Out-of-range ordinals are treated as 0 rather than throwing - the connector projects
        /// live game data, and a defensive read is cheaper than a crashed planning pass over a
        /// single malformed formula.
        /// </summary>
        public int Get(int material) => IsInRange(material) ? _counts[material] : 0;

        public void Set(int material, int count)
        {
            if (IsInRange(material))
            {
                _counts[material] = count;
            }
        }

        /// <summary>
        /// True when every input this formula needs is currently in stock AND applying it would
        /// not push any consumed material below its floor. A null <paramref name="floors"/> means
        /// no floors are configured (fully aggressive, per Slice 2's default policy).
        /// </summary>
        public bool CanApply(RefineFormulaSnapshot formula, MaterialStock floors)
        {
            if (formula == null)
            {
                return false;
            }

            if (!HasEnough(formula.Input1, formula.Input1Count, floors))
            {
                return false;
            }

            if (formula.HasSecondInput && !HasEnough(formula.Input2, formula.Input2Count, floors))
            {
                return false;
            }

            return true;
        }

        private bool HasEnough(int material, int requiredCount, MaterialStock floors)
        {
            var floor = floors == null ? 0 : floors.Get(material);
            return Get(material) - requiredCount >= floor;
        }

        /// <summary>
        /// Subtracts inputs and adds outputs. Skips the second input when the formula has none,
        /// and skips <c>Output2</c> when its count is 0 - both mirror vanilla's dummy-slot
        /// convention rather than treating them as real material movements.
        /// </summary>
        public void Apply(RefineFormulaSnapshot formula)
        {
            if (formula == null)
            {
                return;
            }

            Subtract(formula.Input1, formula.Input1Count);
            if (formula.HasSecondInput)
            {
                Subtract(formula.Input2, formula.Input2Count);
            }

            Add(formula.Output, formula.OutputCount);
            if (formula.Output2Count > 0)
            {
                Add(formula.Output2, formula.Output2Count);
            }
        }

        private void Subtract(int material, int count)
        {
            if (IsInRange(material))
            {
                _counts[material] -= count;
            }
        }

        private void Add(int material, int count)
        {
            if (IsInRange(material))
            {
                _counts[material] += count;
            }
        }

        /// <summary>
        /// Deep copy so the planner can simulate ahead without mutating the caller's stock.
        /// </summary>
        public MaterialStock Clone()
        {
            var copy = new int[MaterialCount];
            _counts.CopyTo(copy, 0);
            return new MaterialStock(copy);
        }
    }
}
