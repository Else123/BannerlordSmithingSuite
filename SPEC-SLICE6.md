# Smithing Suite - Slice 6: Phase budget, travel recovery, diagnostics and MCM

Follow-up spec to `SPEC.md`, `SPEC-SLICE2.md`, `SPEC-SLICE3.md`, `SPEC-SLICE4-5.md`.
Driven by findings from the first real in-game run (2026-07-26, 8 runs across 5 towns).

## Problem

Three things, all surfaced by the first in-game test:

1. **Refining is effectively dead.** Across 70 observed smith-phases, `refineCapacity` was 0 sixty
   times and 1 ten times - never more. Smelting runs first against an essentially unlimited
   candidate list (141 items) and consumes every point of crafting stamina, so refining only ever
   gets the integer-division remainder. Material evidence over eight runs: `IronOre` never moved
   (949 -> 949), `Charcoal` oscillated between 0 and 2, `Wood` grew to 1793 unused, while `Iron1`
   sat at ~38,000 and was never refined upward. Every unit test is green - each planner is correct
   in isolation, the **composition** is wrong.
2. **The log cannot answer its own key question.** It records only wall-clock timestamps. When two
   runs 13 seconds apart both started from full stamina, the log could not distinguish "stamina
   legitimately regenerated over in-game hours" from "stamina was never consumed" - that had to be
   inferred from other lines. Diagnostics must not require inference.
3. **Vanilla only regenerates crafting stamina inside a settlement** (`HourlyTick` is gated on
   `hero.CurrentSettlement != null`). The user finds this unmotivated - travelling does not tire a
   smith - and wants recovery while travelling too.

## Kontext

Verified vanilla behaviour [V-BIN], from `CraftingCampaignBehavior`:

```csharp
private void HourlyTick()   // only for heroes with CurrentSettlement != null
    CraftingStamina = Min(max, current + GetStaminaHourlyRecoveryRate(hero));

private static int GetStaminaHourlyRecoveryRate(Hero hero)
{
    int num = 5 + MathF.Round(hero.GetSkillValue(DefaultSkills.Crafting) * 0.025f);
    if (hero.GetPerkValue(DefaultPerks.Athletics.Stamina))
        num += MathF.Round(num * DefaultPerks.Athletics.Stamina.PrimaryBonus);
    return num;
}

public int  GetHeroCraftingStamina(Hero hero);
public void SetHeroCraftingStamina(Hero hero, int value);   // clamps to Max(0, value)
public int  GetMaxHeroCraftingStamina(Hero hero);           // 100 + Round(Crafting * 0.5)
```

Campaign clock for the log: `CampaignTime.Now` [A - confirm the formatting member before use].

Existing files: `src/Logic/SmithSnapshot.cs`, `src/Integration/SmithingBehavior.cs`,
`src/Integration/SmithingLog.cs`, `src/SmithingSuiteConfig.cs`, `src/SmithingSuiteSubModule.cs`.

## Ansatz + Architektur

### A. Phase stamina budget (the fix for finding 1)

Give each phase a share of the smith's stamina instead of letting smelting drain it.

**No planner change is needed.** Smelting still runs first (its output feeds refining), but the
smelt phase plans against snapshots whose `Stamina` has been scaled to the smelt share. The refine
phase already takes a **fresh** snapshot afterwards, so it naturally sees the untouched remainder.

- `SmithSnapshot.WithStaminaBudget(int stamina)` - engine-free copy with a reduced stamina,
  preserving every other field. Keeps the budgeting testable without a game.
- Default share: **50 % to smelting, remainder to refining** (user decision, 2026-07-26).
- Rounding: floor the smelt share, so refining is never starved by rounding.

### B. Campaign time in the log (finding 2)

`SmithingLog.BeginRun` additionally records the in-game date/time, so a reader can tell how much
campaign time passed between runs and judge stamina recovery without inference.

### C. Recovery while travelling (finding 3) - **the first deliberate balance change**

Own `CampaignBehaviorBase` on `CampaignEvents.HourlyTickEvent`. For each smith in the player's
party **that is not in a settlement**, add the vanilla hourly rate times a configurable multiplier.

Deliberately narrow, because this is a balance change:
- **Only when `hero.CurrentSettlement == null`** - otherwise we would double vanilla's recovery.
- **Only heroes of the player's party** - never AI lords; the blast radius stays inside the player's clan.
- Never exceed `GetMaxHeroCraftingStamina`.
- Mirrors vanilla's own rate formula rather than inventing one, so it scales with skill and the
  `Athletics.Stamina` perk exactly as resting does.
- **Off is a first-class state**: `TravelStaminaEnabled` (default on, per the user's request) and
  `TravelStaminaMultiplier` (default 1.0 = same rate as resting). Both MCM-exposed.

This is recorded explicitly as a departure from the mod's founding constraint ("automation clicks
faster, never cheaper"). It is the user's informed decision; the switch exists so it stays one.

### D. MCM settings

`AttributeGlobalSettings`, id `SmithingSuite_v1`, bridged into `SmithingSuiteConfig` on each use -
the AutoTrader pattern. Groups and settings:

- **General**: `VerboseLogging` (default on), `LogToScreen` (default on).
- **Production**: `SmeltStaminaSharePercent` (0-100, default 50).
- **Travel**: `TravelStaminaEnabled` (default on), `TravelStaminaMultiplier` (0.0-2.0, default 1.0).
- **Advisor**: the `ScoreWeights` for on-foot and mounted, plus `EvaluationBudget`. These are
  **opinion, not engine truth** - the setting hints must say so.

Adds `Bannerlord.MBOptionScreen` to `SubModule.xml` and the `MCMv5` reference via `McmBinPath`.

## Scope

**In:** A, B, C, D as above, their unit tests, and the log lines that make each visible.

**Out of scope:** the advisor's inline UI (Slice 5 TODOs 10-12 - separate), any further balance
change, touching AI lords' stamina, and any `SmithingModel` override.

## Akzeptanzkriterien

Unit-testable:
- [ ] AC1 `WithStaminaBudget` returns a copy with the reduced stamina and every other field intact.
- [ ] AC2 A 50 % share halves a smith's smelt capacity (floor), leaving the remainder for refining.
- [ ] AC3 A 0 % share yields zero smelt actions and does not throw.
- [ ] AC4 A 100 % share reproduces today's behaviour exactly (regression guard).
- [ ] AC5 The share is clamped to 0-100; out-of-range config cannot produce negative stamina.

In-game (manual, with the log):
- [ ] AC6 The run header shows the campaign date/time, and two runs apart in campaign time are
      distinguishable from two runs seconds apart.
- [ ] AC7 After a run, refining has actually happened: `Charcoal` and `IronOre` move, rather than
      sitting at 0 / 949.
- [ ] AC8 Crafting stamina increases while the party travels outside a settlement, and does **not**
      increase at double rate while inside one.
- [ ] AC9 Disabling `TravelStaminaEnabled` restores exact vanilla recovery behaviour.
- [ ] AC10 MCM shows all groups; changing a value takes effect on the next run without a restart.

## Umsetzungs-Schritte (TODOs)

1. **[P]** haengt ab von: - | betrifft: `tests/SmithingSuite.Tests/StaminaBudgetTests.cs`
   Red tests for AC1-AC5.
2. **[S]** haengt ab von: 1 | betrifft: `src/Logic/SmithSnapshot.cs`
   Add `WithStaminaBudget(int)`. Additive; existing tests stay green.
3. **[S]** haengt ab von: 2 | betrifft: `src/SmithingSuiteConfig.cs`, `src/Integration/SmithingBehavior.cs`
   Add `SmeltStaminaSharePercent` (default 50, clamped) and apply it to the smelt-phase snapshots.
   Log the budget per smith so the split is visible.
4. **[S]** haengt ab von: - | betrifft: `src/Integration/SmithingLog.cs`
   Campaign date/time in `BeginRun`. Confirm the `CampaignTime` formatting member first.
5. **[S]** haengt ab von: - | betrifft: `src/Integration/TravelStaminaBehavior.cs`, `src/SmithingSuiteSubModule.cs`, `src/SmithingSuite.csproj`
   Hourly-tick behavior implementing C; registered next to `SmithingBehavior`.
6. **[S]** haengt ab von: 3,5 | betrifft: `src/SmithingSuiteMcmSettings.cs`, `src/SubModule.xml`, `src/SmithingSuite.csproj`
   MCM settings + bridge + dependency wiring.

Welle 1: #1, #4 - Welle 2: #2 - Welle 3: #3, #5 - Welle 4: #6

## Verification

Pre: confirm the `CampaignTime` formatting member (TODO 4) and `CampaignEvents.HourlyTickEvent`'s
delegate shape (TODO 5) by decompile before writing those files.

Post:
```
dotnet test BannerlordSmithingSuite\tests\SmithingSuite.Tests\SmithingSuite.Tests.csproj
```
```
& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" BannerlordSmithingSuite\src\SmithingSuite.csproj /p:Configuration=Debug /p:Platform=x64
```
In-game: AC6-AC10 on the test save, reading `smithingsuite.log`.

## Originaltext

> Ein Hinweis, der mir im Base Game überhaupt nicht gefällt und jetzt natürlich wieder auftritt,
> ist, Stamina wird nur generiert, wenn man an derselben Stelle wartet. Ich finde, Stamina sollte
> auch generiert werden, wenn man von A nach B reist, da das ja die Schmiede Stamina überhaupt
> nicht betrifft.

> 50/50 ist gut

## Status

- Gate1: 2026-07-26 - problems identified from the first in-game run
- Gate2: 2026-07-26 - 50/50 split and travel recovery approved by the user
- Surface: claude
- Mode: normal
- Welle 1: [ ] #1 [ ] #4
- Welle 2: [ ] #2
- Welle 3: [ ] #3 [ ] #5
- Welle 4: [ ] #6
- AC: see Akzeptanzkriterien
- Notes:
  - **C is the mod's first deliberate balance change.** Every earlier slice deliberately avoided
    changing vanilla economics. It is the user's informed decision and stays switchable.
  - Slice 5 (advisor inline UI, TODOs 10-12) is not part of this spec and remains open.
