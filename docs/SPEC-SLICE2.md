# Smithing Suite - Slice 2: Auto-Refine

Follow-up spec to `SPEC.md` (Slice 1, shipped). Same architecture, conventions and constraints -
not repeated here. Read `SPEC.md` first.

## Problem

Slice 1 automated smelting: scrap weapons become raw materials. Those materials are only useful
after refining - ore into iron, iron up the tier chain, hardwood into charcoal. Refining is the
same click-grind as smelting, but unlike smelting it is **order-dependent**: every iron step
consumes charcoal, and charcoal only exists if wood was refined first. Doing it by hand means
mentally solving a small dependency puzzle a few hundred times.

## Kontext

Refining chain [V-BIN], decompiled from `DefaultSmithingModel.GetRefiningFormulas(Hero)` on
2026-07-25. It is **per-hero and perk-gated** - two smiths in the same party can have different
formula sets:

```csharp
// perk DefaultPerks.Crafting.CharcoalMaker -> 3 charcoal instead of 1
Wood 2                 -> Charcoal 3   (without the perk: Charcoal 1)
IronOre 1 + Charcoal 1 -> Iron1 (IronMaker perk: 3, else 2)
Iron1 1   + Charcoal 1 -> Iron2 1
Iron2 2   + Charcoal 1 -> Iron3 1 + Iron1 1
// perk SteelMaker
Iron3 2   + Charcoal 1 -> Iron4 1 + Iron1 1
// perk SteelMaker2
Iron4 2   + Charcoal 1 -> Iron5 1 + Iron1 1
// perk SteelMaker3
Iron5 2   + Charcoal 1 -> Iron6 1 + Iron1 1
```

```csharp
// TaleWorlds.Core.Crafting.RefiningFormula - all read-only properties
CraftingMaterials Input1 { get; }  int Input1Count { get; }
CraftingMaterials Input2 { get; }  int Input2Count { get; }
CraftingMaterials Output { get; }  int OutputCount { get; }
CraftingMaterials Output2 { get; } int Output2Count { get; }

// execution + cost + material item lookup
void ICraftingCampaignBehavior.DoRefinement(Hero hero, Crafting.RefiningFormula refineFormula);
int  SmithingModel.GetEnergyCostForRefining(ref Crafting.RefiningFormula f, Hero hero); // base 6, perk PracticalRefiner
ItemObject SmithingModel.GetCraftingMaterialItem(CraftingMaterials m);   // -> count it in the party ItemRoster
IEnumerable<Crafting.RefiningFormula> SmithingModel.GetRefiningFormulas(Hero weaponsmith);
```

Materials enum ordinals (already recorded in `SPEC.md`):
`IronOre=0, Iron1=1 .. Iron6=6, Wood=7, Charcoal=8`.

Note `Input2Count` may be 0 (the wood->charcoal formula declares `Iron1, 0` as a dummy second
input) - a zero count means "no second input" and must not be treated as a requirement.

Existing code this slice extends:
`src/Logic/SmeltPlanner.cs`, `src/Integration/ISmithingConnector.cs`,
`src/Integration/SmithingConnector.cs`, `src/Integration/SmithingBehavior.cs`.

## Ansatz + Architektur

Unchanged core principle: **drive vanilla, never reimplement.** The planner only decides the
order; `DoRefinement` does the work and deducts the stamina.

### Refining policy (the actual design decision)

Aggressive by default, per the user's stated play style, but **charcoal-aware** so it cannot
strand itself:

1. **Keep the charcoal pipeline ahead of demand.** Prefer an applicable charcoal-producing formula
   while `charcoal < remaining refine capacity of this smith`. Each iron step burns exactly one
   charcoal, so charcoal below the number of refines still available means the smith will run dry
   mid-run; top it up first, then fall back to the tier preference.

   *Revised 2026-07-25 after code review.* The original wording ("act when charcoal is 0") was a
   no-op: every iron formula requires `Charcoal >= 1`, so at zero charcoal `MaterialStock.CanApply`
   already excludes them all and the wood->charcoal formula is the only candidate left. The branch
   decided nothing, and the test that claimed to prove it only proved ordinary availability
   filtering. Charcoal does not fail suddenly - it drains - which is what the rule now models.
2. **Otherwise refine as far up the tier chain as the smith's perks allow**, preferring the
   highest-tier applicable formula (that is what "refine aggressively" means in practice: iron
   ends up as high-tier steel rather than sitting as Iron1).
3. **Respect minimum stock floors** (default 0 = fully aggressive, configurable later in the MCM
   slice). A floor means "never consume this material below N".
4. **Stop** when the smith is out of stamina, or no formula is applicable at all.

Termination is guaranteed structurally: every planned action costs stamina, and total stamina
across smiths is finite. The Iron1 -> Iron2 -> Iron3(+Iron1) chain is cyclic in material terms, so
a purely stock-based loop guard would not be sufficient - the stamina bound is what makes this safe.

### Engine-free representation

The planner must stay free of TaleWorlds types, so formulas are projected into a plain snapshot:
materials are `int` ordinals, matching `CraftingMaterials`. The connector maps back by index when
executing, which is why the formula's index within the hero's own formula list is carried along.

## Scope

**In:**
- `RefineFormulaSnapshot`, `MaterialStock`, `RefinePlan`/`RefineAction`, `RefinePlanner` (all engine-free).
- `SmithSnapshot` gains a per-smith refine energy cost and its available formulas.
- `ISmithingConnector` gains `GetMaterialStock()`, formula projection, and `ExecuteRefine(...)`.
- The existing town menu action runs smelting first, then refining, and reports both.

**Out of scope:**
- Perk-aware routing between smiths (Slice 3), advisor (Slices 4-5), MCM settings (Slice 6).
- Any balance change, any `SmithingModel` override.
- Refining anything other than the vanilla formulas (no custom recipes).
- A second, separate menu option - refining joins the existing one.

## Akzeptanzkriterien

Unit-testable:
- [ ] AC1 A refine action is never planned when its inputs are not in (simulated) stock.
- [ ] AC2 Planned actions per smith never exceed `floor(stamina / refineEnergyCost)`.
- [ ] AC3 Stock is decremented by inputs and incremented by outputs between planned actions, so a
      later action sees the effect of an earlier one.
- [ ] AC4 When charcoal is 0 and only a wood->charcoal formula is applicable, that formula is
      planned before any iron formula.
- [ ] AC5 A minimum stock floor is never breached by a planned action.
- [ ] AC6 With the highest-tier formula applicable, it is preferred over a lower-tier one.
- [ ] AC7 Empty smiths, empty stock, or an empty formula list each yield an empty plan without throwing.
- [ ] AC8 A formula with `Input2Count == 0` is treated as single-input (the dummy `Iron1, 0` case).
- [ ] AC9 Planning terminates even when formulas form a material cycle (bounded by stamina).

In-game (manual, test save):
- [ ] AC10 Triggering the town option refines materials; the material counts change accordingly.
- [ ] AC11 Each participating smith's crafting stamina has visibly decreased.
- [ ] AC12 The summary reports smelting and refining separately.
- [ ] AC13 A smith without the SteelMaker perks never performs the steel formulas.

## Umsetzungs-Schritte (TODOs)

1. **[P]** haengt ab von: - | betrifft: `src/Logic/RefineFormulaSnapshot.cs`, `src/Logic/MaterialStock.cs`
   `RefineFormulaSnapshot`: `int FormulaIndex, Input1, Input1Count, Input2, Input2Count, Output,
   OutputCount, Output2, Output2Count`; helper `int HighestOutputTier` for the preference rule.
   `MaterialStock`: mutable count-per-ordinal (`int[9]` behind an API), `Get(int material)`,
   `CanApply(formula, floors)`, `Apply(formula)`. Engine-free, `System.*` only, C# 7.3.

2. **[S]** haengt ab von: 1 | betrifft: `src/Logic/SmithSnapshot.cs`
   Add `RefineEnergyCost` and `IReadOnlyList<RefineFormulaSnapshot> RefineFormulas` plus
   `RefineCapacity`. **Additive only** - the existing constructor and `Capacity` stay working so
   Slice 1's tests and behaviour are untouched.

3. **[P]** haengt ab von: 1 | betrifft: `tests/SmithingSuite.Tests/RefinePlannerTests.cs`
   Red tests for AC1-AC9, written before TODO 4.

4. **[S]** haengt ab von: 2,3 | betrifft: `src/Logic/RefinePlan.cs`, `src/Logic/RefinePlanner.cs`
   `RefineAction { HeroId, FormulaIndex }`; `RefinePlanner.Plan(smiths, stock, floors) -> RefinePlan`
   implementing the policy above. Green up TODO 3.

5. **[S]** haengt ab von: 4 | betrifft: `src/Integration/ISmithingConnector.cs`
   Add `MaterialStock GetMaterialStock()` and `bool ExecuteRefine(string heroId, int formulaIndex)`.

6. **[S]** haengt ab von: 5 | betrifft: `src/Integration/SmithingConnector.cs`
   Project each hero's `GetRefiningFormulas` into snapshots (index-stable), read material counts
   from the party roster via `GetCraftingMaterialItem`, fill `RefineEnergyCost`, and implement
   `ExecuteRefine` by re-resolving the hero + formula by index and calling `DoRefinement` after a
   live stamina re-check (same defensive pattern as `ExecuteSmelt`).

7. **[S]** haengt ab von: 6 | betrifft: `src/Integration/SmithingBehavior.cs`
   Run smelting first, then refining (smelting produces the inputs refining consumes - order
   matters). Extend the summary to report both phases separately, keeping the existing
   "nothing to do" behaviour when neither phase produced an action.

Welle 1 (parallel): #1, #3
Welle 2 (nach #1): #2
Welle 3 (nach #2,#3): #4
Welle 4 (nach #4): #5
Welle 5 (nach #5): #6
Welle 6 (nach #6): #7

## Verification

Pre: none open - the full formula chain and all APIs are [V-BIN] (see Kontext).

Post:
```
dotnet test BannerlordSmithingSuite\tests\SmithingSuite.Tests\SmithingSuite.Tests.csproj
```
```
& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" BannerlordSmithingSuite\src\SmithingSuite.csproj /p:Configuration=Debug /p:Platform=x64
```
In-game: AC10-AC13 on the test save, BetterSmithing disabled.

## Original request

*Translated from the user's original German - not verbatim English utterances.*

> Actually, the way I play this right now, or when I do it manually, I actually refine more,
> because past a certain point my smithed weapons are stronger and better anyway than anything you
> can get on the market.

> Go ahead and continue with the implementation then.

## Status

- Gate1: 2026-07-25 - inherited from SPEC.md (same problem, next slice)
- Gate2: 2026-07-25 - released via "mache dann weiter mit der umsetzung"; refining policy
  (aggressive + charcoal-aware) decided by the orchestrator and flagged to the user for correction
- Surface: claude
- Mode: normal
- Welle 1: [x] #1 [x] #3 (red: 11x CS0103 RefinePlanner)
- Welle 2: [x] #2
- Welle 3: [x] #4 (green)
- Welle 4: [x] #5
- Welle 5: [x] #6
- Welle 6: [x] #7
- Build: green (MSBuild Debug/x64, 0 warnings, 0 errors)
- Tests: 21/21 green (12 from slice 1 + 9 refine)
- AC: see Akzeptanzkriterien
- Notes:
  - Slice 1's in-game criteria (AC8-AC12 there) are still unverified; this slice builds on that
    untested foundation deliberately, at the user's request.
