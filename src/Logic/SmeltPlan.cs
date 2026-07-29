using System.Collections.Generic;

namespace SmithingSuite.Logic
{
    /// <summary>
    /// One planned smelt: a specific smith performing DoSmelting on a specific item.
    /// Engine-free - the connector resolves the ids to real Hero/EquipmentElement instances.
    /// </summary>
    public sealed class SmeltAction
    {
        public string HeroId { get; }
        public string ItemId { get; }

        /// <summary>
        /// Why <see cref="SmeltPlanner"/> routed this action to this smith (Refactor B) - set at
        /// the moment of assignment, so it is always exactly the branch the planner took, never a
        /// reconstruction from outside.
        /// </summary>
        public SmeltRoutingReason Reason { get; }

        public SmeltAction(string heroId, string itemId, SmeltRoutingReason reason)
        {
            HeroId = heroId;
            ItemId = itemId;
            Reason = reason;
        }
    }

    /// <summary>
    /// The ordered result of <see cref="SmeltPlanner.Plan"/>: every action the connector
    /// should execute, in the order it should execute them.
    /// </summary>
    public sealed class SmeltPlan
    {
        public IReadOnlyList<SmeltAction> Actions { get; }

        public SmeltPlan(IReadOnlyList<SmeltAction> actions)
        {
            Actions = actions;
        }
    }
}
