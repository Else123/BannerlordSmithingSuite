using System.Collections.Generic;
using SmithingSuite.Integration;
using SmithingSuite.Logic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.ComponentInterfaces;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.Core;
using TaleWorlds.ObjectSystem;
using TestRig.Harness.Logic;

namespace SmithingSuite.Scenarios
{
    /// <summary>
    /// SM-1: with surplus crafted weapons and wood/ore in the party inventory, one smithing run
    /// smelts and refines, removes exactly the smelted weapons and keeps smelting inside the
    /// configured stamina share.
    /// </summary>
    public sealed class SmeltThenRefineScenario : IScenario
    {
        private const string WeaponItemId = "peasant_maul_t1";
        private const int WeaponCount = 5;
        private const int HardWoodCount = 4;
        private const int IronOreCount = 2;
        private const int SmeltSharePercent = 50;

        public string Id => "smithing.smelt-then-refine";

        public IEnumerable<ScenarioStep> Run(IScenarioContext ctx)
        {
            Hero hero = Hero.MainHero;
            ItemObject item = MBObjectManager.Instance.GetObject<ItemObject>(WeaponItemId);
            bool hasDesign = item != null && item.WeaponDesign != null;
            ctx.Check("setup.weapon", hasDesign, WeaponItemId + " exists with a WeaponDesign",
                item == null ? "item not found" : (hasDesign ? "ok" : "no WeaponDesign"));
            if (!hasDesign)
            {
                yield break;
            }

            ItemRoster roster = MobileParty.MainParty.ItemRoster;

            // Other smeltables in the roster would make the before/after counts depend on the
            // planner's choice between them, so require a clean slate.
            int otherSmeltables = CountSmeltables(roster);
            ctx.Check("setup.no-other-smeltables", otherSmeltables == 0, "0", otherSmeltables.ToString());
            if (otherSmeltables != 0)
            {
                yield break;
            }

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

            roster.AddToCounts(item, WeaponCount);
            roster.AddToCounts(DefaultItems.HardWood, HardWoodCount);
            roster.AddToCounts(DefaultItems.IronOre, IronOreCount);

            crafting.SetHeroCraftingStamina(hero, crafting.GetMaxHeroCraftingStamina(hero));
            int startStamina = crafting.GetHeroCraftingStamina(hero);

            SmithingSuiteMcmSettings.Instance.SmeltStaminaSharePercent = SmeltSharePercent;
            SmithingSuiteMcmSettings.Apply();

            int smeltablesBefore = CountSmeltables(roster);

            yield return ScenarioStep.Ticks(1);

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

            int smelted = 0;
            int refined = 0;
            foreach (RunReportEntry entry in report.Entries)
            {
                smelted += entry.Smelted;
                refined += entry.Refined;
                ctx.Log(entry.HeroName + ": smelted " + entry.Smelted + ", refined " + entry.Refined
                    + ", stamina spent " + entry.StaminaSpent);
            }

            ctx.Check("smelted", smelted > 0, "> 0", smelted.ToString());
            ctx.Check("refined", refined > 0, "> 0", refined.ToString());

            int smeltablesAfter = CountSmeltables(roster);
            ctx.Check("weapons.drop", smeltablesBefore - smeltablesAfter == smelted,
                "before - after == smelted (" + smelted + ")",
                smeltablesBefore + " - " + smeltablesAfter + " = " + (smeltablesBefore - smeltablesAfter));

            int smeltCost = smelted * smithingModel.GetEnergyCostForSmelting(item, hero);
            double allowed = startStamina * SmeltSharePercent / 100.0;
            ctx.Check("smelt.stamina", smeltCost <= allowed,
                "<= " + allowed + " (" + SmeltSharePercent + "% of " + startStamina + ")", smeltCost.ToString());
        }

        private static int CountSmeltables(ItemRoster roster)
        {
            int count = 0;
            for (int i = 0; i < roster.Count; i++)
            {
                ItemRosterElement element = roster[i];
                ItemObject rosterItem = element.EquipmentElement.Item;
                if (rosterItem != null && rosterItem.WeaponDesign != null)
                {
                    count += element.Amount;
                }
            }

            return count;
        }
    }
}
