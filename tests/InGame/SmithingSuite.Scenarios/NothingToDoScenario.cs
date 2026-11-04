using System;
using System.Collections.Generic;
using System.Linq;
using Helpers;
using SmithingSuite.Integration;
using SmithingSuite.Logic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.ComponentInterfaces;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.Core;
using TestRig.Harness.Fixtures;
using TestRig.Harness.Logic;

namespace SmithingSuite.Scenarios
{
    /// <summary>
    /// SM-2: with nothing smeltable and no crafting materials in the party inventory, but full
    /// stamina, one smithing run reports NothingHappened and changes neither the inventory, nor any
    /// smith's stamina, nor the main hero's gold (SmithingRun.Execute's early-return path).
    /// </summary>
    /// <remarks>
    /// Setup removes every crafted weapon and every crafting material from the party roster and
    /// does not put them back, so it does not depend on what earlier scenarios (e.g. SM-1) left
    /// behind and leaves a clean slate for later ones.
    /// </remarks>
    public sealed class NothingToDoScenario : IScenario
    {
        // Same slot range SmithingConnector.GetMaterialStock reads: CraftingMaterials IronOre(0) .. Charcoal(8).
        private const int CraftingMaterialCount = 9;

        public string Id => "smithing.nothing-to-do";

        public IEnumerable<ScenarioStep> Run(IScenarioContext ctx)
        {
            bool mcmReady = SmithingSuiteMcmSettings.Instance != null;
            ctx.Check("setup.mcm", mcmReady, "SmithingSuiteMcmSettings.Instance != null", mcmReady ? "ok" : "null");
            if (!mcmReady)
            {
                yield break;
            }

            ICraftingCampaignBehavior crafting = Campaign.Current.GetCampaignBehavior<ICraftingCampaignBehavior>();
            ctx.Check("setup.crafting", crafting != null, "ICraftingCampaignBehavior registered", crafting != null ? "ok" : "null");
            if (crafting == null)
            {
                yield break;
            }

            SmithingModel smithingModel = Campaign.Current.Models.SmithingModel;
            ctx.Check("setup.smithing-model", smithingModel != null, "Models.SmithingModel != null", smithingModel != null ? "ok" : "null");
            if (smithingModel == null)
            {
                yield break;
            }

            List<Hero> smiths = CraftingHelper.GetAvailableHeroesForCrafting().Where(h => h != null).ToList();
            ctx.Check("setup.smiths", smiths.Count > 0, "> 0 heroes in the main party", smiths.Count.ToString());
            if (smiths.Count == 0)
            {
                yield break;
            }

            ItemRoster roster = MobileParty.MainParty.ItemRoster;
            HashSet<ItemObject> materials = GetCraftingMaterials(smithingModel);
            int removed = RemoveSmithingInputs(roster, materials);
            ctx.Log("removed " + removed + " crafted weapons and crafting materials from the party roster");

            int leftover = CountSmithingInputs(roster, materials);
            ctx.Check("setup.empty", leftover == 0, "0 crafted weapons and crafting materials", leftover.ToString());
            if (leftover != 0)
            {
                yield break;
            }

            // Full stamina, so "nothing to do" can only come from the empty inventory.
            foreach (Hero smith in smiths)
            {
                crafting.SetHeroCraftingStamina(smith, crafting.GetMaxHeroCraftingStamina(smith));
            }

            SmithingSuiteMcmSettings.Apply();

            // NothingHappened must come from the empty inventory, not from an empty smith pool.
            int connectorSmiths = new SmithingConnector().GetSmiths().Count;
            ctx.Check("setup.connector-smiths", connectorSmiths == smiths.Count, smiths.Count.ToString(), connectorSmiths.ToString());
            if (connectorSmiths != smiths.Count)
            {
                yield break;
            }

            yield return ScenarioStep.Ticks(1);

            Dictionary<string, int> itemsBefore = CountItems(roster);
            Dictionary<string, int> staminaBefore = CaptureStamina(crafting, smiths);
            int goldBefore = Hero.MainHero.Gold;

            // Mirrors SmithingBehavior.SmithingTownConsequence, minus the menu/message plumbing.
            RunReport report;
            SmithingLog.BeginRun("TestRig");
            try
            {
                report = SmithingRun.Execute(new SmithingConnector(), SmithingSuiteConfig.SmeltStaminaSharePercent, new SmithingLogTrace());
            }
            finally
            {
                SmithingLog.EndRun();
            }

            ctx.Check("report.nothing-happened", report.NothingHappened, "True", report.NothingHappened.ToString());
            ctx.Check("report.no-entries", report.Entries.Count == 0, "0", report.Entries.Count.ToString());

            string itemDiff = Diff(itemsBefore, CountItems(roster));
            ctx.Check("inventory.unchanged", itemDiff.Length == 0, "no item count changed", itemDiff.Length == 0 ? "unchanged" : itemDiff);

            string staminaDiff = Diff(staminaBefore, CaptureStamina(crafting, smiths));
            ctx.Check("stamina.unchanged", staminaDiff.Length == 0, "no smith's stamina changed", staminaDiff.Length == 0 ? "unchanged" : staminaDiff);

            int goldAfter = Hero.MainHero.Gold;
            ctx.Check("gold.unchanged", goldAfter == goldBefore, goldBefore.ToString(), goldAfter.ToString());
        }

        private static HashSet<ItemObject> GetCraftingMaterials(SmithingModel smithingModel)
        {
            var materials = new HashSet<ItemObject>();
            for (int ordinal = 0; ordinal < CraftingMaterialCount; ordinal++)
            {
                ItemObject material = smithingModel.GetCraftingMaterialItem((CraftingMaterials)ordinal);
                if (material != null)
                {
                    materials.Add(material);
                }
            }

            return materials;
        }

        private static bool IsSmithingInput(ItemObject item, HashSet<ItemObject> materials)
        {
            return item != null && (item.WeaponDesign != null || materials.Contains(item));
        }

        private static int RemoveSmithingInputs(ItemRoster roster, HashSet<ItemObject> materials)
        {
            int removed = 0;
            // Backwards: AddToCounts fills the slot of a stack that drops to 0 with the roster's last
            // element, which a forward loop would then skip. Going backwards, that element was
            // already visited.
            for (int i = roster.Count - 1; i >= 0; i--)
            {
                ItemRosterElement element = roster[i];
                if (IsSmithingInput(element.EquipmentElement.Item, materials) && element.Amount > 0)
                {
                    removed += element.Amount;
                    roster.AddToCounts(element.EquipmentElement, -element.Amount);
                }
            }

            return removed;
        }

        private static int CountSmithingInputs(ItemRoster roster, HashSet<ItemObject> materials)
        {
            int count = 0;
            for (int i = 0; i < roster.Count; i++)
            {
                ItemRosterElement element = roster[i];
                if (IsSmithingInput(element.EquipmentElement.Item, materials))
                {
                    count += element.Amount;
                }
            }

            return count;
        }

        private static Dictionary<string, int> CountItems(ItemRoster roster)
        {
            var counts = new Dictionary<string, int>();
            for (int i = 0; i < roster.Count; i++)
            {
                ItemRosterElement element = roster[i];
                string key = InventorySnapshot.ItemKey(element.EquipmentElement);
                int sum;
                counts.TryGetValue(key, out sum);
                counts[key] = sum + element.Amount;
            }

            return counts;
        }

        private static Dictionary<string, int> CaptureStamina(ICraftingCampaignBehavior crafting, List<Hero> smiths)
        {
            var stamina = new Dictionary<string, int>();
            foreach (Hero smith in smiths)
            {
                stamina[smith.StringId] = crafting.GetHeroCraftingStamina(smith);
            }

            return stamina;
        }

        // "key +delta" for every key whose value changed (see SnapshotDiff.CountDelta); empty when equal.
        private static string Diff(Dictionary<string, int> before, Dictionary<string, int> after)
        {
            IReadOnlyDictionary<string, int> delta = SnapshotDiff.CountDelta(before, after);
            return string.Join(", ", delta
                .OrderBy(entry => entry.Key, StringComparer.Ordinal)
                .Select(entry => entry.Key + " " + (entry.Value > 0 ? "+" : "") + entry.Value));
        }
    }
}
