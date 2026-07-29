namespace SmithingSuite.Logic
{
    /// <summary>
    /// Why <see cref="SmeltPlanner"/> routed a <see cref="SmeltAction"/> to the smith it picked
    /// (Refactor B). Set by the planner at the moment of assignment - see
    /// <see cref="SmeltPlanner.Plan"/> - so the stated reason IS the branch taken, by
    /// construction. Nothing outside the planner reconstructs this from smith/candidate state
    /// anymore; the trace layer only renders it.
    /// </summary>
    public enum SmeltRoutingReason
    {
        /// <summary>Research-relevant candidate assigned to a smith with CuriousSmelter.</summary>
        CuriousSmelterPreferred,

        /// <summary>Bulk candidate assigned to a smith with PracticalSmelter.</summary>
        PracticalSmelterPreferred,

        /// <summary>
        /// Fell through to a non-preferred smith because no smith in the party holds the perk
        /// relevant to this candidate's class at all.
        /// </summary>
        FallbackNoPerkedSmith,

        /// <summary>
        /// Fell through to a non-preferred smith because the party's perked smith(s) for this
        /// candidate's class were already out of capacity - as opposed to not existing at all.
        /// </summary>
        FallbackPerkedSmithAtCapacity
    }
}
