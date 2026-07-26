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

        /// <summary>
        /// The item's gold value. Vanilla's crafting research gain scales with the value of the
        /// item consumed, so this is what lets the planner prefer high-value items when it wants
        /// to maximize research progress per smelt.
        /// </summary>
        public int Value { get; }

        /// <summary>
        /// Id of the crafting template this item's research points feed into. Never null -
        /// defaults to "" when unknown, so callers can compare/group without a null check.
        /// </summary>
        public string TemplateId { get; }

        /// <summary>
        /// True if <see cref="TemplateId"/> still has unopened parts. Research points accrue per
        /// template, so this is what lets the planner prefer items that advance a template still
        /// being explored over one that is already fully unlocked.
        /// </summary>
        public bool TemplateHasUnopenedParts { get; }

        public SmeltCandidate(string itemId, string name, int count, bool isLocked)
            : this(itemId, name, count, isLocked, value: 0, templateId: "", templateHasUnopenedParts: false)
        {
        }

        public SmeltCandidate(
            string itemId,
            string name,
            int count,
            bool isLocked,
            int value,
            string templateId,
            bool templateHasUnopenedParts)
        {
            ItemId = itemId;
            Name = name;
            Count = count;
            IsLocked = isLocked;
            Value = value;
            // TemplateId must never surface as null - callers (planner routing) compare and
            // group by it without a null check.
            TemplateId = templateId ?? "";
            TemplateHasUnopenedParts = templateHasUnopenedParts;
        }
    }
}
