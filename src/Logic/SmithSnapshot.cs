using System;
using System.Collections.Generic;

namespace SmithingSuite.Logic
{
    /// <summary>
    /// A snapshot of one smith (player hero or companion) available for smelting,
    /// captured at planning time. Engine-free: the connector reads live game state
    /// into this shape before the planner touches it.
    /// </summary>
    public sealed class SmithSnapshot
    {
        public string HeroId { get; }
        public string Name { get; }
        public int Stamina { get; }
        public int MaxStamina { get; }

        /// <summary>
        /// Crafting stamina cost of a single smelt action for THIS smith. Per vanilla
        /// (GetEnergyCostForSmelting), the cost is flat per-smith (base 10, reduced by the
        /// PracticalSmelter perk) and does NOT depend on which item is being smelted - so it
        /// is captured once here rather than per candidate.
        /// </summary>
        public int SmeltEnergyCost { get; }

        /// <summary>
        /// Crafting stamina cost of a single refine action for THIS smith (GetEnergyCostForRefining,
        /// base 6, reduced by the PracticalRefiner perk). Like <see cref="SmeltEnergyCost"/>, flat
        /// per-smith and independent of which formula is applied.
        /// </summary>
        public int RefineEnergyCost { get; }

        /// <summary>
        /// The refining formulas this smith's perks unlock (per-hero, perk-gated - see
        /// DefaultSmithingModel.GetRefiningFormulas). Never null; empty when unknown or when the
        /// snapshot was built via the Slice 1 constructor.
        /// </summary>
        public IReadOnlyList<RefineFormulaSnapshot> RefineFormulas { get; }

        /// <summary>
        /// From hero.GetPerkValue(DefaultPerks.Crafting.CuriousSmelter). Multiplies part-research
        /// gain when smelting. Not for display - it exists so the planner can route items whose
        /// crafting template still has unopened parts to a smith who has this perk, maximizing
        /// research progress instead of wasting it on an already-explored template.
        /// </summary>
        public bool HasCuriousSmelter { get; }

        /// <summary>
        /// From hero.GetPerkValue(DefaultPerks.Crafting.PracticalSmelter). Reduces smelt energy
        /// cost. Not for display - it exists so the planner can route smelt volume to whichever
        /// smith turns stamina into the most smelts, i.e. prefer this smith when capacity is the
        /// bottleneck.
        /// </summary>
        public bool HasPracticalSmelter { get; }

        /// <summary>
        /// From hero.GetPerkValue(DefaultPerks.Crafting.PracticalRefiner). Reduces refine energy
        /// cost, same routing rationale as <see cref="HasPracticalSmelter"/> but for refining.
        /// Not for display.
        /// </summary>
        public bool HasPracticalRefiner { get; }

        public SmithSnapshot(string heroId, string name, int stamina, int maxStamina, int smeltEnergyCost)
            : this(heroId, name, stamina, maxStamina, smeltEnergyCost, refineEnergyCost: 0, refineFormulas: null)
        {
        }

        public SmithSnapshot(
            string heroId,
            string name,
            int stamina,
            int maxStamina,
            int smeltEnergyCost,
            int refineEnergyCost,
            IReadOnlyList<RefineFormulaSnapshot> refineFormulas)
            : this(
                heroId,
                name,
                stamina,
                maxStamina,
                smeltEnergyCost,
                refineEnergyCost,
                refineFormulas,
                hasCuriousSmelter: false,
                hasPracticalSmelter: false,
                hasPracticalRefiner: false)
        {
        }

        public SmithSnapshot(
            string heroId,
            string name,
            int stamina,
            int maxStamina,
            int smeltEnergyCost,
            int refineEnergyCost,
            IReadOnlyList<RefineFormulaSnapshot> refineFormulas,
            bool hasCuriousSmelter,
            bool hasPracticalSmelter,
            bool hasPracticalRefiner)
        {
            HeroId = heroId;
            Name = name;
            // Vanilla's SetHeroCraftingStamina clamps to Max(0, value); mirror that here so a
            // snapshot built from a stale or adversarial source can never carry negative stamina
            // into the planner's arithmetic.
            Stamina = Math.Max(0, stamina);
            MaxStamina = maxStamina;
            SmeltEnergyCost = smeltEnergyCost;
            RefineEnergyCost = refineEnergyCost;
            RefineFormulas = refineFormulas ?? new RefineFormulaSnapshot[0];
            HasCuriousSmelter = hasCuriousSmelter;
            HasPracticalSmelter = hasPracticalSmelter;
            HasPracticalRefiner = hasPracticalRefiner;
        }

        /// <summary>
        /// How many smelts this smith can afford right now. Guarding against a non-positive
        /// cost matters: a zero (or negative) cost would otherwise mean infinite smelts via
        /// integer division by zero or a sign flip, instead of correctly meaning "this smith
        /// can't smelt" - so capacity is defined as 0 in that case, never negative.
        /// </summary>
        public int Capacity => SmeltEnergyCost <= 0 ? 0 : Stamina / SmeltEnergyCost;

        /// <summary>
        /// How many refines this smith can afford right now. Same non-positive-cost guard as
        /// <see cref="Capacity"/>, applied to the refine cost instead of the smelt cost.
        /// </summary>
        public int RefineCapacity => RefineEnergyCost <= 0 ? 0 : Stamina / RefineEnergyCost;

        /// <summary>
        /// Returns a copy of this snapshot with <see cref="Stamina"/> replaced by a phase's
        /// share of it, every other field preserved unchanged. Exists so the smelt phase can plan
        /// against a fraction of the smith's stamina (Slice 6 "phase stamina budget") instead of
        /// consuming all of it before refining ever gets a turn - the refine phase then takes its
        /// own fresh, unbudgeted snapshot and naturally sees the untouched remainder.
        ///
        /// Safety property: a budget can never GRANT more stamina than the smith actually has -
        /// <paramref name="stamina"/> is clamped to [0, this.Stamina], so an out-of-range share
        /// (negative, or a caller passing more than the smith's current total) cannot inflate
        /// capacity beyond what the unbudgeted snapshot itself would allow.
        /// </summary>
        public SmithSnapshot WithStaminaBudget(int stamina)
        {
            int clampedStamina = Math.Max(0, Math.Min(stamina, Stamina));
            return new SmithSnapshot(
                HeroId,
                Name,
                clampedStamina,
                MaxStamina,
                SmeltEnergyCost,
                RefineEnergyCost,
                RefineFormulas,
                HasCuriousSmelter,
                HasPracticalSmelter,
                HasPracticalRefiner);
        }

        /// <summary>
        /// The AC5 share arithmetic that actually satisfies "smelting may never spend more than
        /// its configured share of a smith's stamina": clamps <paramref name="sharePercent"/> to
        /// 0..100 - a bad value (manual edit, future MCM input) must never be able to hand
        /// smelting a negative or more-than-100% share - then floors <c>Stamina * share / 100</c>
        /// so refining is never starved by a smelt share that rounds in its own favor. Moved here
        /// (code review Slice 6 finding C) so it lives in the engine-free layer the test project
        /// actually compiles, instead of the Integration layer where a regression would stay
        /// green.
        /// </summary>
        public static SmithSnapshot ForSmeltShare(SmithSnapshot smith, int sharePercent)
        {
            if (smith == null)
            {
                throw new ArgumentNullException(nameof(smith));
            }

            int clampedShare = Math.Max(0, Math.Min(100, sharePercent));
            int budgetedStamina = smith.Stamina * clampedShare / 100;
            return smith.WithStaminaBudget(budgetedStamina);
        }
    }
}
