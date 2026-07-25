using System.Collections.Generic;
using System.Linq;

namespace SmithingSuite.Logic
{
    /// <summary>
    /// Turns a snapshot of smiths and smeltable candidates into an ordered <see cref="SmeltPlan"/>.
    /// Pure arithmetic over the snapshot - no engine types, no I/O. Mirrors what the connector
    /// will later execute one <c>DoSmelting</c> call at a time.
    /// </summary>
    public static class SmeltPlanner
    {
        public static SmeltPlan Plan(IReadOnlyList<SmithSnapshot> smiths, IReadOnlyList<SmeltCandidate> candidates)
        {
            var remainingByItem = (candidates ?? new SmeltCandidate[0])
                .Where(candidate => !candidate.IsLocked)
                .Select(candidate => new RemainingCandidate(candidate.ItemId, candidate.Count))
                .Where(remaining => remaining.Count > 0)
                .ToList();

            var actions = new List<SmeltAction>();

            foreach (var smith in smiths ?? new SmithSnapshot[0])
            {
                var stillFreeCapacity = smith.Capacity;

                while (stillFreeCapacity > 0 && remainingByItem.Count > 0)
                {
                    var next = remainingByItem[0];
                    actions.Add(new SmeltAction(smith.HeroId, next.ItemId));

                    next.Count--;
                    if (next.Count == 0)
                    {
                        remainingByItem.RemoveAt(0);
                    }

                    stillFreeCapacity--;
                }

                if (remainingByItem.Count == 0)
                {
                    break;
                }
            }

            return new SmeltPlan(actions);
        }

        /// <summary>
        /// Mutable per-candidate counter used only while planning; kept separate from the
        /// immutable <see cref="SmeltCandidate"/> snapshot so the input is never mutated.
        /// </summary>
        private sealed class RemainingCandidate
        {
            public string ItemId { get; }
            public int Count { get; set; }

            public RemainingCandidate(string itemId, int count)
            {
                ItemId = itemId;
                Count = count;
            }
        }
    }
}
