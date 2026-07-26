using System;
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
            var smithList = (smiths ?? new SmithSnapshot[0]).ToList();
            var freeCapacity = smithList.Select(smith => smith.Capacity).ToArray();

            // Research points accrue per CraftingTemplate and scale with the smelted item's
            // value, so smelting an item that still advances an unfinished template is strictly
            // more useful than smelting one that cannot - that beats both roster order and
            // value. Within a class, descending value wins (more research / better materials),
            // and ItemId is the final tie-break so the resulting plan is reproducible.
            var orderedCandidates = (candidates ?? new SmeltCandidate[0])
                .Where(candidate => !candidate.IsLocked && candidate.Count > 0)
                .OrderByDescending(candidate => candidate.TemplateHasUnopenedParts)
                .ThenByDescending(candidate => candidate.Value)
                .ThenBy(candidate => candidate.ItemId, StringComparer.Ordinal)
                .ToList();

            var actions = new List<SmeltAction>();

            foreach (var candidate in orderedCandidates)
            {
                var remaining = candidate.Count;

                // Research-relevant work prefers a CuriousSmelter (multiplies research gain);
                // bulk work prefers a PracticalSmelter (cheaper smelts -> more actions from the
                // same stamina). Neither preference may starve a smith or drop a candidate: once
                // the preferred smiths are out of capacity, assignment falls through to any
                // smith that still has some, in roster order.
                foreach (var smithIndex in AssignmentOrder(smithList, candidate.TemplateHasUnopenedParts))
                {
                    while (remaining > 0 && freeCapacity[smithIndex] > 0)
                    {
                        actions.Add(new SmeltAction(smithList[smithIndex].HeroId, candidate.ItemId));
                        freeCapacity[smithIndex]--;
                        remaining--;
                    }

                    if (remaining == 0)
                    {
                        break;
                    }
                }
            }

            return new SmeltPlan(actions);
        }

        /// <summary>
        /// Smith indices in the order a candidate of the given class should be offered to them:
        /// perk-matched smiths first (stable, roster order), then every other smith (stable,
        /// roster order) as the fallback - so capacity is never left idle and no smith is
        /// starved just because it lacks the preferred perk.
        /// </summary>
        private static IEnumerable<int> AssignmentOrder(IReadOnlyList<SmithSnapshot> smiths, bool researchRelevant)
        {
            var preferred = new List<int>();
            var fallback = new List<int>();

            for (var i = 0; i < smiths.Count; i++)
            {
                var hasPreferredPerk = researchRelevant ? smiths[i].HasCuriousSmelter : smiths[i].HasPracticalSmelter;
                if (hasPreferredPerk)
                {
                    preferred.Add(i);
                }
                else
                {
                    fallback.Add(i);
                }
            }

            return preferred.Concat(fallback);
        }
    }
}
