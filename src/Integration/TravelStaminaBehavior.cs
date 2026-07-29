using System;
using System.Collections.Generic;
using Helpers;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace SmithingSuite.Integration
{
    /// <summary>
    /// The mod's first deliberate balance change (SPEC-SLICE6.md section C). Vanilla only
    /// regenerates crafting stamina for a hero standing inside a settlement -
    /// <c>CraftingCampaignBehavior.HourlyTick</c> is gated on <c>CurrentSettlement != null</c>.
    /// The user found this unmotivated (travelling does not tire a smith) and asked for the same
    /// recovery while the player's party travels, on the explicit condition that it stays
    /// switchable - see <see cref="SmithingSuiteConfig.TravelStaminaEnabled"/>.
    ///
    /// Deliberately narrow: only the player party's own heroes (never AI lords - the blast radius
    /// stays inside the player's clan), and it mirrors vanilla's own recovery-rate formula rather
    /// than inventing one, so it scales with the Crafting skill and the Athletics.Stamina perk
    /// exactly as resting does.
    /// </summary>
    public class TravelStaminaBehavior : CampaignBehaviorBase
    {
        public override void RegisterEvents()
        {
            CampaignEvents.HourlyTickEvent.AddNonSerializedListener(this, OnHourlyTick);
        }

        public override void SyncData(IDataStore dataStore)
        {
            // Nothing to persist: recovery is derived fresh from live hero/engine state on every
            // tick, exactly like SmithingBehavior's plan-on-demand approach.
        }

        private void OnHourlyTick()
        {
            // Bridged on every tick (code review Slice 6 finding A): this behavior reads
            // SmithingSuiteConfig.TravelStaminaEnabled/TravelStaminaMultiplier directly, never
            // through the town menu, so without this call a player who flips the toggle off in
            // MCM mid-session would keep recovering stamina all session - the exact bug the
            // finding described. Hourly is cheap enough (a handful of field copies) that this is
            // not a per-frame cost. Safe no-op if MCM has not registered the page yet.
            SmithingSuiteMcmSettings.Apply();

            if (!SmithingSuiteConfig.TravelStaminaEnabled)
            {
                return;
            }

            if (Campaign.Current == null)
            {
                return;
            }

            var behavior = Campaign.Current.GetCampaignBehavior<ICraftingCampaignBehavior>();
            if (behavior == null)
            {
                return;
            }

            // Same precondition SmithingConnector.GetSmiths() checks: GetAvailableHeroesForCrafting()
            // evaluates PartyBase.MainParty.MemberRoster.GetTroopRoster() EAGERLY as its LINQ query
            // source, so a null MainParty/MemberRoster throws from the call itself rather than
            // yielding an empty/null result a post-call check could catch.
            if (PartyBase.MainParty?.MemberRoster == null)
            {
                return;
            }

            try
            {
                RecoverPartyStamina(behavior);
            }
            catch
            {
                // This handler fires every in-game hour for the lifetime of the campaign - a
                // diagnostics or balance feature must never be able to break the hourly tick.
            }
        }

        private void RecoverPartyStamina(ICraftingCampaignBehavior behavior)
        {
            IEnumerable<Hero> heroes = CraftingHelper.GetAvailableHeroesForCrafting();

            int heroesRecovered = 0;
            int totalGain = 0;

            foreach (Hero hero in heroes)
            {
                if (hero == null)
                {
                    continue;
                }

                // THE single most important rule in this file: vanilla's own HourlyTick already
                // regenerates crafting stamina for any hero standing in a settlement. Recovering
                // here too would silently double that rate - see SPEC-SLICE6.md section C, "Only
                // when hero.CurrentSettlement == null".
                if (hero.CurrentSettlement != null)
                {
                    continue;
                }

                int gain = RecoverHeroStamina(behavior, hero);
                if (gain <= 0)
                {
                    continue;
                }

                heroesRecovered++;
                totalGain += gain;
            }

            LogTick(heroesRecovered, totalGain);
        }

        // Mirrors CraftingCampaignBehavior.GetStaminaHourlyRecoveryRate exactly (see
        // SPEC-SLICE6.md "Kontext") so travel recovery scales with the Crafting skill and the
        // Athletics.Stamina perk the same way resting in a settlement does, then applies the
        // configurable multiplier and clamps to the hero's max - never exceeding it, never going
        // negative.
        private static int RecoverHeroStamina(ICraftingCampaignBehavior behavior, Hero hero)
        {
            int vanillaRate = 5 + MathF.Round(hero.GetSkillValue(DefaultSkills.Crafting) * 0.025f);
            if (hero.GetPerkValue(DefaultPerks.Athletics.Stamina))
            {
                vanillaRate += MathF.Round(vanillaRate * DefaultPerks.Athletics.Stamina.PrimaryBonus);
            }

            int gain = MathF.Round(vanillaRate * SmithingSuiteConfig.TravelStaminaMultiplier);
            if (gain <= 0)
            {
                return 0;
            }

            int current = behavior.GetHeroCraftingStamina(hero);
            int max = behavior.GetMaxHeroCraftingStamina(hero);
            int newValue = Math.Min(max, current + gain);
            if (newValue <= current)
            {
                return 0;
            }

            behavior.SetHeroCraftingStamina(hero, newValue);
            return newValue - current;
        }

        // Fires every in-game hour for as long as the campaign runs, so this must stay a single
        // compact summary line - never one line per hero - and only when verbose logging is on.
        // The heroesRecovered == 0 guard also skips string building on an idle tick (every
        // hero already at max, or all in a settlement), which is the common case.
        private static void LogTick(int heroesRecovered, int totalGain)
        {
            if (!SmithingLog.IsVerbose || heroesRecovered == 0)
            {
                return;
            }

            SmithingLog.Line("Travel stamina recovery: " + heroesRecovered + " hero(es), +" + totalGain + " total.");
            // This tick is never inside a SmithingBehavior run, so the batching optimisation does
            // not apply here - flush immediately or the line only reaches disk if/when a
            // town-menu run happens to occur later in the session (see SmithingLog.Flush()).
            SmithingLog.Flush();
        }
    }
}
