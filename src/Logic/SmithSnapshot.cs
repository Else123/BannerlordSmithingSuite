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
    }
}
