using System.Collections.Generic;

namespace SmithingSuite.Logic
{
    /// <summary>
    /// The engine boundary for smelting automation. Its sole purpose is to keep the rest of
    /// <see cref="SmithingSuite.Logic"/> free of TaleWorlds types: this interface references
    /// only Logic value types and BCL types, never anything engine-specific. Lives in Logic
    /// (moved from Integration by Refactor A) because <see cref="SmithingRun"/> - the two-phase
    /// orchestration - depends on it directly, and orchestration must stay in the engine-free,
    /// test-compiled layer.
    /// </summary>
    public interface ISmithingConnector
    {
        /// <summary>
        /// Every smith (player hero and companions) currently available for smelting, with
        /// their stamina and per-smith smelt cost.
        /// </summary>
        IReadOnlyList<SmithSnapshot> GetSmiths();

        /// <summary>
        /// Every smeltable item stack in the party inventory, including locked ones (the
        /// planner is the single place that decides what to do with a lock).
        /// </summary>
        IReadOnlyList<SmeltCandidate> GetSmeltCandidates();

        /// <summary>
        /// Executes one planned smelt action against the live game state. Returns false
        /// (without throwing) if the hero or item can no longer be resolved, or if the hero's
        /// current stamina no longer covers the smelt cost.
        /// </summary>
        bool ExecuteSmelt(string heroId, string itemId);

        /// <summary>
        /// The party's current count of every refining material (ordinals 0..8, matching
        /// vanilla <c>CraftingMaterials</c>), read from the party inventory.
        /// </summary>
        MaterialStock GetMaterialStock();

        /// <summary>
        /// Executes one planned refine action against the live game state. <paramref name="formulaIndex"/>
        /// is the position within the hero's OWN <c>GetRefiningFormulas</c> list, as captured in
        /// <see cref="SmithSnapshot.RefineFormulas"/> - see the connector for the stability contract.
        /// Returns false (without throwing) if the hero or formula can no longer be resolved, or if
        /// the hero's current stamina no longer covers the refine cost.
        /// </summary>
        bool ExecuteRefine(string heroId, int formulaIndex);

        /// <summary>Surfaces a message to the player (e.g. the post-run summary).</summary>
        void Report(string message);
    }
}
