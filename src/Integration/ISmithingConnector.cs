using System.Collections.Generic;
using SmithingSuite.Logic;

namespace SmithingSuite.Integration
{
    /// <summary>
    /// The engine boundary for smelting automation. Its sole purpose is to keep
    /// <see cref="SmithingSuite.Logic"/> free of TaleWorlds types: this interface references
    /// only Logic value types and BCL types, never anything engine-specific.
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

        /// <summary>Surfaces a message to the player (e.g. the post-run summary).</summary>
        void Report(string message);
    }
}
