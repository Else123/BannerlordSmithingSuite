using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Helpers;
using SmithingSuite.Integration;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Library;
using TestRig.Harness.Logic;

namespace SmithingSuite.Scenarios
{
    /// <summary>
    /// SM-3: with the player party parked outside any settlement (the harness parks it), every hero
    /// in the party recovers crafting stamina on each in-game hour by exactly
    /// <c>Round(vanillaHourlyRate * TravelStaminaMultiplier)</c>, where vanillaHourlyRate is what
    /// vanilla's private <c>CraftingCampaignBehavior.GetStaminaHourlyRecoveryRate</c> returns
    /// (TravelStaminaBehavior.RecoverHeroStamina). Vanilla itself recovers nothing outside a
    /// settlement, so without the mod the stamina stays at the start value.
    /// </summary>
    /// <remarks>
    /// Days(1) runs 0 to 24 in-game hours, so the scenario counts the hourly ticks itself. Start
    /// stamina 0 and a multiplier below 1 keep up to 24 hours of the expected gain below the max
    /// stamina, so the mod's clamp to max cannot mask a wrong gain; the setup checks the headroom
    /// and <c>hours.max</c> checks the 24-hour assumption. The MCM values and every hero's stamina
    /// are restored afterwards.
    /// </remarks>
    public sealed class TravelStaminaScenario : IScenario
    {
        private const int StartStamina = 0;
        private const int MaxHoursInOneDay = 24;

        // Not the default 1.0. The scenario only sets it on the MCM instance and does not call
        // Apply() before the wait, so the check also proves that TravelStaminaBehavior.OnHourlyTick
        // bridges the MCM values itself. 0.6 x an integer never ends in .5, so the rounding mode
        // does not matter.
        private const float Multiplier = 0.6f;

        private const string VanillaRateMethodName = "GetStaminaHourlyRecoveryRate";

        public string Id => "smithing.travel-stamina";

        public IEnumerable<ScenarioStep> Run(IScenarioContext ctx)
        {
            SmithingSuiteMcmSettings settings = SmithingSuiteMcmSettings.Instance;
            ctx.Check("setup.mcm", settings != null, "SmithingSuiteMcmSettings.Instance != null", settings != null ? "ok" : "null");
            if (settings == null)
            {
                yield break;
            }

            TravelStaminaBehavior travel = Campaign.Current.GetCampaignBehavior<TravelStaminaBehavior>();
            ctx.Check("setup.travel-behavior", travel != null, "TravelStaminaBehavior registered", travel != null ? "ok" : "null");
            if (travel == null)
            {
                yield break;
            }

            ICraftingCampaignBehavior crafting = Campaign.Current.GetCampaignBehavior<ICraftingCampaignBehavior>();
            ctx.Check("setup.crafting", crafting != null, "ICraftingCampaignBehavior registered", crafting != null ? "ok" : "null");
            if (crafting == null)
            {
                yield break;
            }

            MethodInfo vanillaRate = typeof(CraftingCampaignBehavior).GetMethod(
                VanillaRateMethodName, BindingFlags.NonPublic | BindingFlags.Static, null, new[] { typeof(Hero) }, null);
            ctx.Check("setup.vanilla-rate", vanillaRate != null,
                "CraftingCampaignBehavior." + VanillaRateMethodName + "(Hero) exists", vanillaRate != null ? "ok" : "not found");
            if (vanillaRate == null)
            {
                yield break;
            }

            MobileParty party = MobileParty.MainParty;
            string settlement = party.CurrentSettlement == null ? "none" : party.CurrentSettlement.StringId;
            ctx.Check("setup.outside-settlement", party.CurrentSettlement == null, "none", settlement);
            if (party.CurrentSettlement != null)
            {
                yield break;
            }

            List<Hero> heroes = CraftingHelper.GetAvailableHeroesForCrafting().Where(h => h != null).ToList();
            ctx.Check("setup.heroes", heroes.Count > 0, "> 0 heroes in the main party", heroes.Count.ToString());
            if (heroes.Count == 0)
            {
                yield break;
            }

            bool enabledBefore = settings.TravelStaminaEnabled;
            float multiplierBefore = settings.TravelStaminaMultiplier;
            var staminaBefore = heroes.ToDictionary(h => h, h => crafting.GetHeroCraftingStamina(h));
            var hourlyGain = new Dictionary<Hero, int>();
            var hours = new HourCounter(heroes);
            bool sampled = false;
            try
            {
                // Only the instance; the mod's hourly tick bridges it into SmithingSuiteConfig.
                settings.TravelStaminaEnabled = true;
                settings.TravelStaminaMultiplier = Multiplier;

                bool setupOk = true;
                foreach (Hero hero in heroes)
                {
                    int rate = (int)vanillaRate.Invoke(null, new object[] { hero });
                    int gain = MathF.Round(rate * Multiplier);
                    int max = crafting.GetMaxHeroCraftingStamina(hero);
                    hourlyGain[hero] = gain;

                    ctx.Check("setup.gain." + hero.StringId, gain > 0,
                        "> 0 (vanilla rate " + rate + " x " + Multiplier + ")", gain.ToString());

                    int worstCase = StartStamina + MaxHoursInOneDay * gain;
                    ctx.Check("setup.headroom." + hero.StringId, worstCase < max,
                        "start + " + MaxHoursInOneDay + " h x gain < max (" + max + ")", worstCase.ToString());

                    setupOk &= gain > 0 && worstCase < max;
                    crafting.SetHeroCraftingStamina(hero, StartStamina);
                }

                if (!setupOk)
                {
                    yield break;
                }

                CampaignEvents.HourlyTickEvent.AddNonSerializedListener(hours, hours.OnHourlyTick);

                yield return ScenarioStep.Days(1);

                CampaignEvents.HourlyTickEvent.ClearListeners(hours);

                ctx.Log("hourly ticks during Days(1): " + hours.Count);
                ctx.Check("hours", hours.Count >= 1, ">= 1 hourly tick", hours.Count.ToString());
                ctx.Check("hours.max", hours.Count <= MaxHoursInOneDay, "<= " + MaxHoursInOneDay, hours.Count.ToString());
                ctx.Check("stayed-outside", hours.SettlementSeen == null,
                    "no hero in a settlement on any hourly tick", hours.SettlementSeen ?? "none");

                foreach (Hero hero in heroes)
                {
                    int expected = StartStamina + hours.Count * hourlyGain[hero];
                    int actual = crafting.GetHeroCraftingStamina(hero);
                    ctx.Check("stamina." + hero.StringId, actual == expected,
                        expected + " (" + StartStamina + " + " + hours.Count + " h x " + hourlyGain[hero] + ")", actual.ToString());
                }

                sampled = true;
                RestoreStamina(crafting, staminaBefore);
            }
            finally
            {
                // Idempotent: ClearListeners does nothing for an owner that is not registered.
                CampaignEvents.HourlyTickEvent.ClearListeners(hours);

                // Only on an early exit; after sampling the stamina was already restored above.
                // Restoring here before the checks would make them pass on the old values.
                if (!sampled)
                {
                    RestoreStamina(crafting, staminaBefore);
                }

                settings.TravelStaminaEnabled = enabledBefore;
                settings.TravelStaminaMultiplier = multiplierBefore;
                SmithingSuiteMcmSettings.Apply();
            }
        }

        private static void RestoreStamina(ICraftingCampaignBehavior crafting, Dictionary<Hero, int> stamina)
        {
            foreach (KeyValuePair<Hero, int> entry in stamina)
            {
                crafting.SetHeroCraftingStamina(entry.Key, entry.Value);
            }
        }

        // Counts HourlyTickEvent firings and notes a hero inside a settlement. The count does not
        // depend on whether it runs before or after TravelStaminaBehavior in the same dispatch:
        // both listen on the same event, so each firing is exactly one recovery step.
        private sealed class HourCounter
        {
            private readonly List<Hero> _heroes;

            public HourCounter(List<Hero> heroes)
            {
                _heroes = heroes;
            }

            public int Count { get; private set; }

            /// <summary>"heroId in settlementId" of the first sighting; null when none.</summary>
            public string SettlementSeen { get; private set; }

            public void OnHourlyTick()
            {
                Count++;
                if (SettlementSeen != null)
                {
                    return;
                }

                foreach (Hero hero in _heroes)
                {
                    if (hero.CurrentSettlement != null)
                    {
                        SettlementSeen = hero.StringId + " in " + hero.CurrentSettlement.StringId;
                        return;
                    }
                }
            }
        }
    }
}
