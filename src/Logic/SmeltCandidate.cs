namespace SmithingSuite.Logic
{
    /// <summary>
    /// One stack of smeltable items in the party inventory, captured at planning time.
    /// Engine-free: the connector is responsible for excluding items that aren't unlocked
    /// smeltable weapons before this type is ever constructed.
    /// </summary>
    public sealed class SmeltCandidate
    {
        public string ItemId { get; }
        public string Name { get; }
        public int Count { get; }

        /// <summary>
        /// True if the player marked this stack as "keep this" via the inventory lock UI
        /// (the same lock source AutoTrader uses). The planner must never assign smelts
        /// against a locked candidate - this is the player's explicit protection signal.
        /// </summary>
        public bool IsLocked { get; }

        public SmeltCandidate(string itemId, string name, int count, bool isLocked)
        {
            ItemId = itemId;
            Name = name;
            Count = count;
            IsLocked = isLocked;
        }
    }
}
