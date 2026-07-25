using System.Collections.Generic;
using System.Linq;
using Helpers;
using SmithingSuite.Logic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace SmithingSuite.Integration
{
    /// <summary>
    /// The single class touching TaleWorlds smithing APIs. Everything else in the mod (planner,
    /// behavior) only ever sees <see cref="ISmithingConnector"/> and the engine-free Logic types.
    /// </summary>
    public sealed class SmithingConnector : ISmithingConnector
    {
        // Matches MaterialStock's internal slot count (IronOre=0 .. Charcoal=8).
        private const int MaterialOrdinalCount = 9;

        public IReadOnlyList<SmithSnapshot> GetSmiths()
        {
            var behavior = Campaign.Current?.GetCampaignBehavior<ICraftingCampaignBehavior>();
            if (behavior == null)
            {
                return new List<SmithSnapshot>();
            }

            // CraftingHelper.GetAvailableHeroesForCrafting() evaluates
            // PartyBase.MainParty.MemberRoster.GetTroopRoster() EAGERLY as the LINQ query
            // source (not lazily on enumeration), so a null MainParty/MemberRoster throws a
            // NullReferenceException from the call itself - before a post-call "heroes == null"
            // check could ever catch it. Check the precondition explicitly instead.
            if (PartyBase.MainParty?.MemberRoster == null)
            {
                return new List<SmithSnapshot>();
            }

            var heroes = CraftingHelper.GetAvailableHeroesForCrafting();

            // GetEnergyCostForSmelting is flat per-smith and item-independent (see
            // SmithSnapshot.SmeltEnergyCost), so any smeltable item works as the probe. If the
            // party carries none right now, the cost is unknowable - fall back to 0 rather than
            // inventing a number; SmithSnapshot.Capacity already treats a non-positive cost as
            // "this smith can't smelt", which is the correct behaviour here too.
            ItemObject representativeItem = GetFirstSmeltableItem();

            var snapshots = new List<SmithSnapshot>();
            foreach (Hero hero in heroes)
            {
                if (hero == null)
                {
                    continue;
                }

                int stamina = behavior.GetHeroCraftingStamina(hero);
                int maxStamina = behavior.GetMaxHeroCraftingStamina(hero);
                int smeltEnergyCost = representativeItem == null
                    ? 0
                    : Campaign.Current.Models.SmithingModel.GetEnergyCostForSmelting(representativeItem, hero);

                // CONTRACT: materialised ONCE here via .ToList() and never re-sorted or filtered.
                // GetRefiningFormulas is per-hero and perk-gated, so there is no stable global id
                // for a formula - the planner only ever carries a FormulaIndex (this list's
                // position), and ExecuteRefine later re-materialises the SAME hero's formula list
                // the same way to map that index back to a real RefiningFormula. If this
                // enumeration and ExecuteRefine's ever disagreed on order, the planner and
                // connector would silently execute the wrong formula.
                List<Crafting.RefiningFormula> refineFormulas =
                    Campaign.Current.Models.SmithingModel.GetRefiningFormulas(hero).ToList();
                int refineEnergyCost = GetRefineEnergyCost(refineFormulas, hero);

                snapshots.Add(new SmithSnapshot(hero.StringId, hero.Name?.ToString() ?? hero.StringId,
                    stamina, maxStamina, smeltEnergyCost, refineEnergyCost, ProjectRefineFormulas(refineFormulas)));
            }

            return snapshots;
        }

        public MaterialStock GetMaterialStock()
        {
            var stock = new MaterialStock();
            ItemRoster roster = PartyBase.MainParty?.MobileParty?.ItemRoster;
            if (roster == null)
            {
                return stock;
            }

            for (int ordinal = 0; ordinal < MaterialOrdinalCount; ordinal++)
            {
                ItemObject materialItem =
                    Campaign.Current.Models.SmithingModel.GetCraftingMaterialItem((CraftingMaterials)ordinal);
                if (materialItem == null)
                {
                    continue;
                }

                stock.Set(ordinal, CountItemInRoster(roster, materialItem));
            }

            return stock;
        }

        public IReadOnlyList<SmeltCandidate> GetSmeltCandidates()
        {
            ItemRoster roster = PartyBase.MainParty?.MobileParty?.ItemRoster;
            if (roster == null)
            {
                return new List<SmeltCandidate>();
            }

            HashSet<string> lockedKeys = GetLockedKeys();

            // KNOWN SIMPLIFICATION (accepted for this slice): candidates are keyed by item
            // StringId, so two roster stacks of the same item differing only by ItemModifier
            // become two candidates with the same ItemId. ExecuteSmelt resolves the first
            // matching unlocked element, so this never blocks planning - it just means the
            // planner cannot distinguish which specific modifier variant gets smelted first.
            var candidates = new List<SmeltCandidate>();
            for (int i = 0; i < roster.Count; i++)
            {
                ItemRosterElement element = roster[i];
                ItemObject item = element.EquipmentElement.Item;
                if (item == null || item.WeaponDesign == null)
                {
                    continue;
                }

                // Locked stacks are still reported (never silently dropped) so the planner is
                // the single place that decides what to do with the lock.
                bool isLocked = lockedKeys.Contains(BuildLockKey(element.EquipmentElement));
                candidates.Add(new SmeltCandidate(item.StringId, item.Name?.ToString() ?? item.StringId,
                    element.Amount, isLocked));
            }

            return candidates;
        }

        public bool ExecuteSmelt(string heroId, string itemId)
        {
            var behavior = Campaign.Current?.GetCampaignBehavior<ICraftingCampaignBehavior>();
            ItemRoster roster = PartyBase.MainParty?.MobileParty?.ItemRoster;
            if (behavior == null || roster == null)
            {
                return false;
            }

            Hero hero = FindSmith(heroId);
            if (hero == null)
            {
                return false;
            }

            EquipmentElement matchedElement;
            if (!TryFindUnlockedElement(roster, itemId, out matchedElement))
            {
                return false;
            }

            // The plan was computed against a snapshot; the world may have moved since (another
            // action already spent this hero's stamina), so re-check against live state rather
            // than trusting the plan blindly.
            int smeltEnergyCost = Campaign.Current.Models.SmithingModel.GetEnergyCostForSmelting(matchedElement.Item, hero);
            int stamina = behavior.GetHeroCraftingStamina(hero);
            if (stamina < smeltEnergyCost)
            {
                return false;
            }

            behavior.DoSmelting(hero, matchedElement);
            return true;
        }

        public bool ExecuteRefine(string heroId, int formulaIndex)
        {
            var behavior = Campaign.Current?.GetCampaignBehavior<ICraftingCampaignBehavior>();
            if (behavior == null)
            {
                return false;
            }

            Hero hero = FindSmith(heroId);
            if (hero == null)
            {
                return false;
            }

            // Re-resolve the formula fresh rather than trusting the planner's snapshot: perks or
            // game state may have moved since planning, and formulaIndex is only meaningful
            // against a re-materialisation of THIS hero's own GetRefiningFormulas list (see the
            // stability contract documented in GetSmiths()).
            List<Crafting.RefiningFormula> formulas =
                Campaign.Current.Models.SmithingModel.GetRefiningFormulas(hero).ToList();
            if (formulaIndex < 0 || formulaIndex >= formulas.Count)
            {
                return false;
            }

            Crafting.RefiningFormula formula = formulas[formulaIndex];

            // The plan was computed against a snapshot; the world may have moved since (another
            // action already spent this hero's stamina), so re-check against live state rather
            // than trusting the plan blindly - same defensive pattern as ExecuteSmelt.
            int refineEnergyCost = Campaign.Current.Models.SmithingModel.GetEnergyCostForRefining(ref formula, hero);
            int stamina = behavior.GetHeroCraftingStamina(hero);
            if (stamina < refineEnergyCost)
            {
                return false;
            }

            behavior.DoRefinement(hero, formula);
            return true;
        }

        public void Report(string message)
        {
            InformationManager.DisplayMessage(new InformationMessage(message));
        }

        private static Hero FindSmith(string heroId)
        {
            // See GetSmiths() for why this precondition check is required:
            // CraftingHelper.GetAvailableHeroesForCrafting() evaluates
            // PartyBase.MainParty.MemberRoster.GetTroopRoster() EAGERLY as the LINQ query
            // source (not lazily on enumeration), so a null MainParty/MemberRoster throws a
            // NullReferenceException from the call itself - before a post-call "heroes == null"
            // check could ever catch it. Check the precondition explicitly instead.
            if (PartyBase.MainParty?.MemberRoster == null)
            {
                return null;
            }

            var heroes = CraftingHelper.GetAvailableHeroesForCrafting();

            foreach (Hero hero in heroes)
            {
                if (hero != null && hero.StringId == heroId)
                {
                    return hero;
                }
            }

            return null;
        }

        private static bool TryFindUnlockedElement(ItemRoster roster, string itemId, out EquipmentElement matchedElement)
        {
            HashSet<string> lockedKeys = GetLockedKeys();

            for (int i = 0; i < roster.Count; i++)
            {
                ItemRosterElement element = roster[i];
                ItemObject item = element.EquipmentElement.Item;
                if (item == null || item.StringId != itemId)
                {
                    continue;
                }

                if (lockedKeys.Contains(BuildLockKey(element.EquipmentElement)))
                {
                    continue;
                }

                matchedElement = element.EquipmentElement;
                return true;
            }

            matchedElement = default(EquipmentElement);
            return false;
        }

        // GetEnergyCostForRefining is flat per-smith and formula-independent (see
        // SmithSnapshot.RefineEnergyCost), so any one of the hero's own formulas works as the
        // probe - the ref parameter forces a local variable, it cannot be passed a property or
        // loop variable directly. Falls back to 0 (== "can't refine", per RefineCapacity) when
        // the hero currently has no unlocked formula to probe with.
        private static int GetRefineEnergyCost(List<Crafting.RefiningFormula> formulas, Hero hero)
        {
            if (formulas.Count == 0)
            {
                return 0;
            }

            Crafting.RefiningFormula representative = formulas[0];
            return Campaign.Current.Models.SmithingModel.GetEnergyCostForRefining(ref representative, hero);
        }

        private static IReadOnlyList<RefineFormulaSnapshot> ProjectRefineFormulas(List<Crafting.RefiningFormula> formulas)
        {
            var snapshots = new List<RefineFormulaSnapshot>(formulas.Count);
            for (int i = 0; i < formulas.Count; i++)
            {
                Crafting.RefiningFormula formula = formulas[i];
                snapshots.Add(new RefineFormulaSnapshot(
                    i,
                    (int)formula.Input1, formula.Input1Count,
                    (int)formula.Input2, formula.Input2Count,
                    (int)formula.Output, formula.OutputCount,
                    (int)formula.Output2, formula.Output2Count));
            }

            return snapshots;
        }

        private static int CountItemInRoster(ItemRoster roster, ItemObject item)
        {
            int count = 0;
            for (int i = 0; i < roster.Count; i++)
            {
                if (roster[i].EquipmentElement.Item == item)
                {
                    count += roster[i].Amount;
                }
            }

            return count;
        }

        private static ItemObject GetFirstSmeltableItem()
        {
            ItemRoster roster = PartyBase.MainParty?.MobileParty?.ItemRoster;
            if (roster == null)
            {
                return null;
            }

            for (int i = 0; i < roster.Count; i++)
            {
                ItemObject item = roster[i].EquipmentElement.Item;
                if (item != null && item.WeaponDesign != null)
                {
                    return item;
                }
            }

            return null;
        }

        // Lock key convention proven by the shipping AutoTrader: item StringId, with
        // ItemModifier.StringId appended when the element carries a modifier.
        private static string BuildLockKey(EquipmentElement equipmentElement)
        {
            string key = equipmentElement.Item.StringId;
            if (equipmentElement.ItemModifier != null)
            {
                key += equipmentElement.ItemModifier.StringId;
            }

            return key;
        }

        private static HashSet<string> GetLockedKeys()
        {
            var locks = Campaign.Current?.GetCampaignBehavior<IViewDataTracker>()?.GetInventoryLocks();
            return locks == null ? new HashSet<string>() : new HashSet<string>(locks);
        }
    }
}
