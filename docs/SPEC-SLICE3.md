# Smithing Suite - Slice 3: Perk-aware routing and research-aware smelt ordering

Follow-up spec to `SPEC.md` (Slice 1) and `SPEC-SLICE2.md` (Slice 2), both shipped.
Same architecture, conventions and constraints - not repeated here.

## Problem

Slices 1 and 2 treat every smith as interchangeable: the planners walk the smith list in whatever
order the engine returns and hand out work first-come-first-served. Candidate items are likewise
taken in roster order. That leaves two things on the table the user explicitly asked for:

1. **Distribute work by strengths.** Smiths differ by perk: one smelts more cheaply, another
   learns parts faster, another can refine steel at all. Ignoring that wastes stamina and slows
   part unlocking.
2. **Smelting is not only material harvesting - it is how crafting parts get unlocked.** Research
   points accrue **per `CraftingTemplate`** and scale with the smelted item's value. Smelting an
   arbitrary item is strictly worse than smelting one that still advances an unfinished template.

## Kontext

All [V-BIN], decompiled from `DefaultSmithingModel` on 2026-07-25.

```csharp
// research gain scales with ITEM VALUE and is multiplied by the CuriousSmelter perk
public override int GetPartResearchGainForSmeltingItem(ItemObject item, Hero hero)
{
    ExplainedNumber n = new ExplainedNumber(1f + MathF.Round(0.02f * item.Value));
    if (hero.GetPerkValue(DefaultPerks.Crafting.CuriousSmelter))
        n.AddFactor(DefaultPerks.Crafting.CuriousSmelter.PrimaryBonus);
    return (int)n.ResultNumber;
}

// energy costs - flat base, perk-reduced, item-independent (smelting/refining)
GetEnergyCostForSmelting : base 10, factor from DefaultPerks.Crafting.PracticalSmelter
GetEnergyCostForRefining : base  6, factor from DefaultPerks.Crafting.PracticalRefiner
```

Confirmed crafting perk members (`DefaultPerks.Crafting.*`):
`CharcoalMaker`, `IronMaker`, `SteelMaker`, `SteelMaker2`, `SteelMaker3`,
`PracticalRefiner`, `PracticalSmith`, `PracticalSmelter`,
`ExperiencedSmith`, `MasterSmith`, `LegendarySmith`,
`CuriousSmelter`, `CuriousSmith`.

Perk read: `hero.GetPerkValue(PerkObject)` -> bool [V-BIN].

Part-unlock state: `ICraftingCampaignBehavior.IsOpened(CraftingPiece, CraftingTemplate)` [V-BIN].
Template of a smeltable item: `item.WeaponDesign.Template` [V-BIN].
All pieces of a template: `CraftingTemplate.Pieces` [V-BIN].

**Not controllable:** `OpenPart` is private; unlocking happens indirectly when accumulated
research points cross `ResearchPointsNeedForNewPart`. We can only *steer where the points go*
(by choosing which item to smelt) and *how many accrue* (by choosing which smith smelts it).

Existing files this slice changes:
`src/Logic/SmithSnapshot.cs`, `src/Logic/SmeltCandidate.cs`, `src/Logic/SmeltPlanner.cs`,
`src/Logic/RefinePlanner.cs`, `src/Integration/SmithingConnector.cs`.

## Ansatz + Architektur

Unchanged: drive vanilla, never reimplement; the stamina bound stays the hard limit. This slice
only changes **who does what, in which order** - never how much can be done.

### Smelt policy

Two priority classes over candidates:

- **Research-relevant**: the item's template still has unopened pieces. Within the class, order by
  **descending item value**, because research gain scales with value - the same stamina buys more
  progress.
- **Bulk**: everything else, also by descending value (higher-tier items yield better materials).

Research-relevant work is assigned preferentially to smiths holding **`CuriousSmelter`** (their
research gain is multiplied). Bulk work prefers **`PracticalSmelter`** holders, whose lower cost
means more actions out of the same stamina.

Assignment stays capacity-bounded exactly as in Slice 1; this is a reordering of *who gets which
item*, never an increase in how many actions exist.

### Refine policy

Unchanged in substance (charcoal-first, then highest tier). Only the **smith order** changes:
smiths with **`PracticalRefiner`** go first, since a cheaper cost converts the same stamina into
more refines. Formula availability remains per-hero and perk-gated by vanilla.

### Keeping the logic engine-free

Perks enter the Logic layer as plain booleans on `SmithSnapshot`; template state enters as a
plain `TemplateId` string plus a `TemplateHasUnopenedParts` bool on `SmeltCandidate`. The
connector does the engine lookups. No `TaleWorlds.*` reaches `src/Logic`.

## Scope

**In:** perk flags on the snapshot, value/template fields on the candidate, the two ordering
policies above, connector population of the new fields, tests, and log lines showing the chosen
class and routing reason.

**Out of scope:** the crafting advisor (Slices 4-5), MCM settings (Slice 6), any balance change,
any `SmithingModel` override, forcing specific part unlocks (engine-private), and smithing
(crafting) automation - crafting stays manual by design.

## Akzeptanzkriterien

Unit-testable:
- [ ] AC1 Candidates whose template has unopened parts are planned before bulk candidates.
- [ ] AC2 Within a priority class, higher-value items are planned before lower-value ones.
- [ ] AC3 Research-relevant candidates are assigned to a `CuriousSmelter` smith when one has capacity.
- [ ] AC4 Bulk candidates prefer a `PracticalSmelter` smith when one has capacity.
- [ ] AC5 Routing never increases the total action count beyond the sum of all smiths' capacities
      (the Slice 1 guarantee must survive the reordering).
- [ ] AC6 A locked candidate is still never planned, regardless of class or value.
- [ ] AC7 With no perked smiths at all, behaviour degrades to the Slice 1 ordering and the plan is
      still valid (no smith starved, no capacity exceeded).
- [ ] AC8 `RefinePlanner` processes `PracticalRefiner` smiths before others.
- [ ] AC9 The refine policy itself (charcoal-first, then highest tier) is unchanged by the
      reordering - the Slice 2 tests must all still pass untouched.

In-game (manual, test save):
- [ ] AC10 The log shows each candidate's class (research/bulk), value and template.
- [ ] AC11 The log shows which smith was chosen and why (perk-based reason).
- [ ] AC12 Smelting an item whose template has unopened parts visibly advances part unlocking
      faster when done by a `CuriousSmelter` smith.

## Umsetzungs-Schritte (TODOs)

1. **[S]** haengt ab von: - | betrifft: `src/Logic/SmithSnapshot.cs`
   Add `bool HasCuriousSmelter`, `bool HasPracticalSmelter`, `bool HasPracticalRefiner`.
   **Additive only** - existing constructors keep working and default the flags to false, so the
   21 existing tests stay green untouched.

2. **[S]** haengt ab von: - | betrifft: `src/Logic/SmeltCandidate.cs`
   Add `int Value`, `string TemplateId`, `bool TemplateHasUnopenedParts`. Additive, same rule.

3. **[P]** haengt ab von: 1,2 | betrifft: `tests/SmithingSuite.Tests/SmeltRoutingTests.cs`
   Red tests for AC1-AC7, written before TODO 4.

4. **[S]** haengt ab von: 3 | betrifft: `src/Logic/SmeltPlanner.cs`
   Implement the two-class ordering and perk-preferring assignment. Must not change the capacity
   arithmetic. Keep the existing `Plan` signature working (the new fields are optional inputs).

5. **[P]** haengt ab von: 1 | betrifft: `tests/SmithingSuite.Tests/RefineRoutingTests.cs`
   Red test for AC8.

6. **[S]** haengt ab von: 5 | betrifft: `src/Logic/RefinePlanner.cs`
   Order smiths by `HasPracticalRefiner` first, stable otherwise. Policy body unchanged.

7. **[S]** haengt ab von: 4,6 | betrifft: `src/Integration/SmithingConnector.cs`
   Populate the new fields: perks via `hero.GetPerkValue(DefaultPerks.Crafting.*)`; candidate
   `Value` from `item.Value`; `TemplateId` from `item.WeaponDesign.Template.StringId`;
   `TemplateHasUnopenedParts` by testing the template's pieces with `IsOpened(piece, template)`.
   **Cache the per-template unopened check per run** - it is called once per candidate and walks
   every piece of the template; recomputing it per item would be quadratic on a large roster.

8. **[S]** haengt ab von: 7 | betrifft: `src/Integration/SmithingBehavior.cs`
   Extend the log: per candidate the class, value and template; per action the routing reason.

Welle 1 (parallel): #1, #2
Welle 2 (nach #1,#2): #3, #5
Welle 3 (nach #3): #4 - (nach #5): #6
Welle 4 (nach #4,#6): #7
Welle 5 (nach #7): #8

## Verification

Pre: none open - all anchors are [V-BIN] (see Kontext).

Post:
```
dotnet test BannerlordSmithingSuite\tests\SmithingSuite.Tests\SmithingSuite.Tests.csproj
```
```
& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" BannerlordSmithingSuite\src\SmithingSuite.csproj /p:Configuration=Debug /p:Platform=x64
```
All 21 existing tests must stay green **without being edited** - this slice reorders, it does not
change what is allowed.

## Original request

*Translated from the user's original German - not verbatim English utterances.*

> And of course also distribute the tasks among the companions who have the respective strengths,
> meaning whatever perks they currently have set, so there's just a bit more depth to the
> smithing, without the manual grind.

> Go ahead with everything.

## Status

- Gate1: 2026-07-25 - inherited from SPEC.md
- Gate2: 2026-07-25 - released via "weiter mit allem"
- Surface: claude
- Mode: normal
- Welle 1: [x] #1 [x] #2
- Welle 2: [x] #3 [x] #5 (red: 5 failing, 3 passing invariants)
- Welle 3: [x] #4 [x] #6 (green)
- Welle 4: [x] #7
- Welle 5: [x] #8
- Build: green (MSBuild Debug/x64, 0 warnings, 0 errors), deployed
- Tests: 33/33 green (25 prior + 8 routing)
- Note: TODO 4 required restructuring `SmeltPlanner` from smith-major to candidate-major looping.
  A research-relevant item must be able to skip past a non-preferred smith to reach a
  `CuriousSmelter` one, which sorting the outer smith loop alone cannot express. Slice 1
  invariants verified unchanged by its existing tests.
- AC: see Akzeptanzkriterien
- Notes:
  - Slice 1 and 2 remain unverified in-game; this slice builds on them at the user's request.
  - Slice 2 has an outstanding code review that was interrupted by an API outage - still owed.
