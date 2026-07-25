# Smithing Suite - Spec

Mount & Blade II: Bannerlord mod (v1.4.7 + War Sails / NavalDLC).
Own module on vanilla anchors - **no fork, no third-party code copied**.
Public repo -> English only. New code: high standard (Clean Code, SRP/DI).

Scope of THIS spec: **architecture for the whole mod + detailed TODOs for Slice 1 only.**
Later slices are sketched in section 7 and get their own follow-up specs
(per SPEC-SCHEMA: first slice here, follow-up specs for the rest).

## Problem

Bannerlord's smithing has a good game idea - smelt, refine, source hardwood, craft, all
bounded by crafting stamina - wrapped in miserable ergonomics: production means hundreds of
identical clicks spread across several smiths, with constant switching between them.
Existing automation mods solve this by removing the stamina limit, which makes smithing
overpowered: the micromanagement is gone, but so is the game idea.

This mod automates **production** (the primary goal; skill leveling is only a side effect)
**without devaluing the game idea**: it clicks faster, not cheaper. Second pillar: crafting
itself stays manual, but gets real inline feedback so the player can make an informed choice
instead of guessing which part combination is good.

## Kontext

Verification legend (same as `CompanionManagementSuite/SPEC.md`):
**[V-BIN]** confirmed in decompiled DLL output (signature quoted verbatim) -
**[V-SRC]** proven by compiling source in this workspace -
**[A]** assumption -
**[OPEN]** must be decompiled before the affected slice.

### Vanilla anchors (all [V-BIN], decompiled 2026-07-25 via `ilspycmd`)

DLL: `E:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\bin\Win64_Shipping_Client\TaleWorlds.CampaignSystem.dll`

```csharp
// TaleWorlds.CampaignSystem.CampaignBehaviors.CraftingCampaignBehavior
public void DoSmelting(Hero currentCraftingHero, EquipmentElement equipmentElement)
public void DoRefinement(Hero hero, Crafting.RefiningFormula refineFormula)
public int  GetHeroCraftingStamina(Hero hero)
public void SetHeroCraftingStamina(Hero hero, int value)      // clamps to Max(0, value)
public int  GetMaxHeroCraftingStamina(Hero hero)              // 100 + Round(Crafting * 0.5)
public bool IsOpened(CraftingPiece craftingPiece, CraftingTemplate craftingTemplate)
```

`DoSmelting` is self-contained: it adds materials to `MobileParty.MainParty.ItemRoster`,
removes the smelted item, grants Crafting XP, **deducts stamina**, adds part-research points,
and fires `OnEquipmentSmeltedByHero`. `DoRefinement` behaves analogously. **This is the
load-bearing fact of the whole design** (see Ansatz).

```csharp
// TaleWorlds.CampaignSystem.ComponentInterfaces.SmithingModel : MBGameModel<SmithingModel>
public abstract int GetEnergyCostForSmelting(ItemObject item, Hero hero);   // Default: base 10, reduced by perk PracticalSmelter
public abstract int GetEnergyCostForRefining(ref Crafting.RefiningFormula refineFormula, Hero hero); // base 6, perk PracticalRefiner
public abstract IEnumerable<Crafting.RefiningFormula> GetRefiningFormulas(Hero weaponsmith); // PER-HERO, perk-gated
public abstract int[] GetSmeltingOutputForItem(ItemObject item);            // int[9], index == CraftingMaterials ordinal
public abstract int GetPartResearchGainForSmeltingItem(ItemObject item, Hero hero);
public abstract float ResearchPointsNeedForNewPart(int totalPartCount, int openedPartCount);
public abstract int CalculateWeaponDesignDifficulty(WeaponDesign weaponDesign);
```

Active model accessor: `Campaign.Current.Models.SmithingModel` [V-BIN].
Concrete impl is `TaleWorlds.CampaignSystem.GameComponents.DefaultSmithingModel` - **in
`TaleWorlds.CampaignSystem.dll`, not `SandBox.dll`**.

Notable: `GetEnergyCostForSmelting` base cost is **flat 10, independent of item tier** (only
perk-reduced). Smelt cost therefore depends on the **smith**, not the item - which makes the
planner arithmetic trivial (see Ansatz).

```csharp
// TaleWorlds.Core.CraftingMaterials  (ordinal == index into GetSmeltingOutputForItem)
IronOre=0, Iron1=1, Iron2=2, Iron3=3, Iron4=4, Iron5=5, Iron6=6, Wood=7, Charcoal=8, NumCraftingMats=9
```
(Consistent with the AutoTrader's existing `[7] = Wood/Hardwood` usage.)

```csharp
// TaleWorlds.CampaignSystem.CampaignGameStarter
public void AddGameMenuOption(string menuId, string optionId, string optionText,
    GameMenuOption.OnConditionDelegate condition, GameMenuOption.OnConsequenceDelegate consequence,
    bool isLeave = false, int index = -1, bool isRepeatable = false, object relatedObject = null)
```

For later slices (recorded now, not used in Slice 1):
- `TaleWorlds.Core.WeaponDesignElement.CreateUsablePiece(CraftingPiece, int scalePercentage = 100)` [V-BIN]
  - the **part-scaling lever** the advisor optimizes over.
- `TaleWorlds.Core.Crafting.GenerateItem(WeaponDesign, TextObject, BasicCultureObject, ItemModifierGroup, ref ItemObject, string)` [V-BIN]
  - produces the **real** `ItemObject` with engine-computed stats; the advisor reads these
    instead of inventing numbers.
- Inline UI injection target: `TaleWorlds.CampaignSystem.ViewModelCollection.WeaponCrafting.WeaponDesign.WeaponDesignVM`
  (in `TaleWorlds.CampaignSystem.ViewModelCollection.dll`, **not** `SandBox.ViewModelCollection.dll`),
  public field `ItemObject CraftedItemObject` [V-BIN].
- Part unlocking cannot be forced: `OpenPart` is **private**; unlocking is driven indirectly by
  research points accumulated **per `CraftingTemplate`**. Threshold
  `ResearchPointsNeedForNewPart = Sqrt(100/totalPartCount) * (openedPartCount*9 + 10)` [V-BIN].

### Scaffolding templates (this workspace, quoted by research agent - not personally line-read)

- `BannerlordAutoTrader\AutoTrader\src\AutoTrader.csproj` - old-style csproj
  (`TargetFrameworkVersion=v4.8`, `PlatformTarget=x64`, `LangVersion=7.3`, `Deterministic=true`,
  `OutputType=Library`; game refs via `HintPath=$(BannerlordBinPath)\...` + `<Private>False</Private>`).
- `BannerlordAutoTrader\AutoTrader\Directory.Build.props` - install-path autodetect
  (`paths.props.user` import + drive probing), derived `BannerlordBinPath` /
  `BannerlordModulesPath` / `McmBinPath`, `ValidateBannerlordPath` target, `CopyToBannerlord`
  post-build target.
- `BannerlordAutoTrader\AutoTrader\src\AutoTraderBehavior.cs` - the **town menu pattern to copy**:
  `CampaignBehaviorBase.RegisterEvents()` hooks `CampaignEvents.OnSessionLaunchedEvent`, which
  calls `AddGameMenuOption("town", ...)` / `("village", ...)`; condition delegate uses
  `Campaign.Current.Models.SettlementAccessModel.CanMainHeroDoSettlementAction(...)` +
  `MenuHelper.SetOptionProperties(...)`.
- `BannerlordAutoTrader\tests\SpeedTradingTests\SpeedTradingTests.csproj` - the test pattern:
  SDK-style, `net10.0`, xUnit, `<Compile Include="..\..\AutoTrader\src\...\*.cs" Link="..."/>`
  linking engine-free sources so they compile without any TaleWorlds reference.

### Conventions (personally read)

- `Modding\CLAUDE.md` - English only in public repos; Logic + Connector split; patch-free
  CampaignBehavior/GameModel hooks preferred over Harmony; MCM v5 for config, no bespoke
  settings UI; only **one override per GameModel type** (extend the previously registered one);
  vertical slices; `[V-BIN]`/`[V-SRC]`/`[A]`/`[OPEN]` legend.
- `#infra\SHARED-FACTS.md` - PowerShell without `&&`/heredoc; explicit `git add`; ASCII commits;
  only affected tests during dev.

## Ansatz + Architektur

### Core architectural decision: drive vanilla, never reimplement

`DoSmelting` / `DoRefinement` are public and already do everything correctly - including
deducting stamina. The automation therefore **calls the real game operations in a loop and
stops when stamina runs out**.

Consequence: the "not overpowered" guarantee is **structural, not disciplinary**. The mod
cannot grant free production without deliberately bypassing the engine, which it never does.
This is the single most important property of the design and every later slice must preserve it.

### Decision: no `SmithingModel` override in Slice 1

The user's initial framing was "replace all smithing models". The decompile shows this is
**not required** for any stated goal: the public operations suffice, and a model override
would only be needed to *change balance* - explicitly a non-goal. Registering an override also
costs compatibility surface (one override per GameModel type).

-> **Deferred.** The connector isolates all model access behind `ISmithingConnector`, so an
override can be added later as a pure delegating subclass without touching logic code.
Recorded as an accepted deviation from the original wording, agreed in Phase 1/2 discussion.

### Layering (mirrors AutoTrader / CompanionSuite)

- `SmithingSuite.Logic` - **pure C#, only `System.*`, no TaleWorlds reference.** Planning and
  (later) advisor scoring. This is what the test project compiles.
- `SmithingSuite.Integration` - the single implementation of `ISmithingConnector` against real
  TaleWorlds APIs, plus the `CampaignBehaviorBase` that owns the town menu option.
- `SmithingSuite` - `SmithingSuiteSubModule : MBSubModuleBase` entry point.

Because `GetEnergyCostForSmelting` is item-independent, a smith's smelt capacity is simply
`floor(stamina / smeltEnergyCost)`. The planner is therefore engine-free integer arithmetic
over a snapshot - fully unit-testable without a game install.

### Slice 1 flow

1. Player opens a town menu -> option "Automatic smithing" (condition mirrors AutoTrader's
   settlement-access check).
2. Connector builds a snapshot: every smith (player hero + party companions) with stamina,
   max stamina and per-smith smelt energy cost; every **unlocked** smeltable item in
   `MobileParty.MainParty.ItemRoster`.
3. `SmeltPlanner` (engine-free) produces an ordered `SmeltPlan`: assign smelts smith by smith
   until each smith's stamina is exhausted, then move to the next.
4. Connector executes the plan by calling `DoSmelting(hero, element)` per action.
5. A summary message reports per smith: items smelted, stamina spent, materials gained.

Item locks are honoured via the same lock source the AutoTrader uses
(`IViewDataTracker.GetInventoryLocks()`) - a locked item is the player's "keep this" signal.

## Scope

**In (Slice 1):**
- Module scaffold (csproj, `Directory.Build.props`, `paths.props.user.template`, `SubModule.xml`).
- `MBSubModuleBase` entry point + `CampaignBehaviorBase` registering the town menu option.
- Engine-free smelt planner + its snapshot/plan types.
- `ISmithingConnector` + its single TaleWorlds implementation.
- Live execution of `DoSmelting` (user explicitly approved going live in Slice 1 - test save).
- Summary message.
- xUnit test project covering the planner.

**Out of scope (this spec):**
- Refining (Slice 2), perk-aware routing (Slice 3), advisor (Slices 4-5), MCM settings (Slice 6).
- Any `SmithingModel` override / any balance change whatsoever.
- Companion development, perk or point spending, equipment distribution -> `CompanionManagementSuite`.
- Buying or selling anything -> `BannerlordAutoTrader`.
- Automating weapon crafting itself - crafting stays a deliberate manual act by design.
- Forcing specific part unlocks (engine-private; only influenceable via research points).
- Coexistence with BetterSmithing - it is to be uninstalled, and is read only as a
  **functional** reference (closed source, no license -> no code taken).

## Akzeptanzkriterien

Unit-testable (engine-free, `dotnet test`):
- [x] AC1 A smith is never assigned more smelts than `floor(stamina / smeltEnergyCost)`.
- [x] AC2 When a smith's remaining stamina is below their smelt cost, the planner moves to the
      next smith rather than stopping.
- [x] AC3 A smith with stamina below their smelt cost is assigned zero actions and does not
      block later smiths.
- [x] AC4 Locked items never appear in the plan.
- [x] AC5 Every candidate item is planned at most as often as it exists in the roster.
- [x] AC6 With no smiths, or no candidates, the plan is empty and no exception is thrown.
- [x] AC7 The plan's total planned actions never exceed the sum of all smiths' capacities.

(AC1-AC7 green: 12/12 in `tests/SmithingSuite.Tests`, incl. 4 added after review for
`SmithSnapshot.Capacity` edge cases.)

In-game (manual, test save):
- [ ] AC8 The menu option appears in a town and is disabled with a reason when settlement
      access is denied.
- [ ] AC9 Triggering it smelts items, and each participating smith's crafting stamina has
      visibly decreased afterwards.
- [ ] AC10 Materials appear in the party inventory; smelted items are gone; locked items remain.
- [ ] AC11 A summary message lists per smith what was smelted and how much stamina was spent.
- [ ] AC12 Running it again with all smiths at zero stamina performs no actions and says so.

## Umsetzungs-Schritte (TODOs)

1. **[P]** haengt ab von: - | betrifft: `src/Logic/SmithSnapshot.cs`, `src/Logic/SmeltCandidate.cs`
   Engine-free value types. `SmithSnapshot`: `HeroId` (string), `Name`, `Stamina`,
   `MaxStamina`, `SmeltEnergyCost`; computed `Capacity => SmeltEnergyCost <= 0 ? 0 : Stamina / SmeltEnergyCost`.
   `SmeltCandidate`: `ItemId`, `Name`, `Count`, `IsLocked`. Only `System.*`.

2. **[S]** haengt ab von: 1 | betrifft: `src/Logic/SmeltPlan.cs`, `src/Logic/SmeltPlanner.cs`
   `SmeltAction { HeroId, ItemId }`; `SmeltPlan { IReadOnlyList<SmeltAction> Actions }`.
   `SmeltPlanner.Plan(IReadOnlyList<SmithSnapshot>, IReadOnlyList<SmeltCandidate>) -> SmeltPlan`:
   skip locked candidates; walk smiths in order, consuming each smith's capacity against the
   remaining candidate counts; move on when capacity is exhausted; stop when candidates run out.
   No engine types, no I/O.

3. **[P]** haengt ab von: - | betrifft: `src/SubModule.xml`, `src/paths.props.user.template`
   Module manifest: `Id="SmithingSuite"`, `SingleplayerModule=true`, DependedModules
   `Native` / `SandBoxCore` / `Sandbox` / `StoryMode` (no MCM, no Harmony, no UIExtenderEx yet),
   `SubModuleClassType="SmithingSuite.SmithingSuiteSubModule"`, `DLLName="SmithingSuite.dll"`.
   Template file mirrors the AutoTrader's.

4. **[P]** haengt ab von: - | betrifft: `src/SmithingSuite.csproj`, `src/Directory.Build.props`
   Old-style csproj per workspace convention; `Directory.Build.props` copied in structure from
   `BannerlordAutoTrader\AutoTrader\Directory.Build.props` with `ModOutputPath` ->
   `$(BannerlordModulesPath)\SmithingSuite`. References (all `<Private>False</Private>`):
   `TaleWorlds.CampaignSystem`, `TaleWorlds.Core`, `TaleWorlds.Library`, `TaleWorlds.ObjectSystem`,
   `TaleWorlds.MountAndBlade`, `TaleWorlds.Localization`, `TaleWorlds.CampaignSystem.ViewModelCollection`.

5. **[S]** haengt ab von: 2 | betrifft: `src/Integration/ISmithingConnector.cs`
   Interface only: `IReadOnlyList<SmithSnapshot> GetSmiths()`,
   `IReadOnlyList<SmeltCandidate> GetSmeltCandidates()`,
   `bool ExecuteSmelt(string heroId, string itemId)`, `void Report(string message)`.
   Keeps `SmithingSuite.Logic` free of engine types.

6. **[S]** haengt ab von: 4,5 | betrifft: `src/Integration/SmithingConnector.cs`
   The only file touching TaleWorlds smithing APIs. Smith pool = player hero + companion heroes
   of `MobileParty.MainParty`. Stamina via `GetHeroCraftingStamina` /
   `GetMaxHeroCraftingStamina`; cost via `Campaign.Current.Models.SmithingModel.GetEnergyCostForSmelting`.
   Candidates = `MobileParty.MainParty.ItemRoster` entries with a non-null `WeaponDesign`,
   excluding locked ones (`IViewDataTracker.GetInventoryLocks()`). `ExecuteSmelt` resolves hero +
   `EquipmentElement` and calls `DoSmelting`. Behavior instance via
   `Campaign.Current.GetCampaignBehavior<ICraftingCampaignBehavior>()` - **[OPEN-light] CLOSED
   2026-07-25**, see "Closed open points" below.

### Closed open points (decompiled 2026-07-25, all [V-BIN] unless noted)

```csharp
// TaleWorlds.CampaignSystem.Campaign - public generic accessor
public T GetCampaignBehavior<T>()

// public class CraftingCampaignBehavior : CampaignBehaviorBase, ICraftingCampaignBehavior, ...
// public interface ICraftingCampaignBehavior : ICampaignBehavior
//   -> namespace TaleWorlds.CampaignSystem.CampaignBehaviors
//   ALL required members are on the INTERFACE, so the concrete class is never needed:
int  GetHeroCraftingStamina(Hero hero);
void SetHeroCraftingStamina(Hero hero, int value);
int  GetMaxHeroCraftingStamina(Hero hero);
void DoSmelting(Hero currentCraftingHero, EquipmentElement equipmentElement);
void DoRefinement(Hero hero, Crafting.RefiningFormula refineFormula);
Hero GetActiveCraftingHero();

// Helpers.CraftingHelper - public static; this IS the smith pool (includes the player hero)
public static IEnumerable<Hero> GetAvailableHeroesForCrafting()
    => from t in PartyBase.MainParty.MemberRoster.GetTroopRoster()
       where t.Character.IsHero select t.Character.HeroObject;

// Locks + roster [V-SRC] - exactly as used by the shipping AutoTrader
Campaign.Current.GetCampaignBehavior<IViewDataTracker>().GetInventoryLocks()  // IEnumerable<string>
// lock key = item.StringId, with ItemModifier.StringId appended when a modifier is present
ItemRoster roster = PartyBase.MainParty.MobileParty.ItemRoster;
ItemRosterElement e = roster[i];   // e.EquipmentElement is what DoSmelting takes
```

7. **[S]** haengt ab von: 6 | betrifft: `src/Integration/SmithingBehavior.cs`
   `CampaignBehaviorBase`; empty `SyncData`; `RegisterEvents` hooks
   `CampaignEvents.OnSessionLaunchedEvent`; adds `AddGameMenuOption("town", "smithingsuite_town",
   ...)` with a condition mirroring the AutoTrader's settlement-access check. Consequence:
   build snapshot -> `SmeltPlanner.Plan` -> execute each action -> `Report` summary
   (per smith: count smelted, stamina spent; plus "nothing to do" case).

8. **[S]** haengt ab von: 7 | betrifft: `src/SmithingSuiteSubModule.cs`
   `MBSubModuleBase`; `InitializeGameStarter` adds `SmithingBehavior` when
   `game.GameType is Campaign`; startup `InformationManager` message.

9. **[P]** haengt ab von: 2 | betrifft: `tests/SmithingSuite.Tests/SmithingSuite.Tests.csproj`, `tests/SmithingSuite.Tests/SmeltPlannerTests.cs`
   SDK-style `net10.0` xUnit project linking `..\..\src\Logic\*.cs` (no TaleWorlds reference).
   Tests cover AC1-AC7. Written **before** step 2 is implemented (TDD: red first).

Welle 1 (parallel): #1, #3, #4
Welle 2 (nach #1): #9 (red tests), dann #2
Welle 3 (nach #2): #5
Welle 4 (nach #4,#5): #6
Welle 5 (nach #6): #7
Welle 6 (nach #7): #8

## Verification

**Pre (before coding step 6):** close the [OPEN-light] in TODO 6 -
`ilspycmd "<game>\bin\Win64_Shipping_Client\TaleWorlds.CampaignSystem.dll" -t TaleWorlds.CampaignSystem.CampaignBehaviors.CraftingCampaignBehavior`
and confirm how the behavior instance is obtained, plus the exact `ICraftingCampaignBehavior` member list.

**Post - unit tests (affected only, per SHARED-FACTS):**
```
dotnet test BannerlordSmithingSuite\tests\SmithingSuite.Tests\SmithingSuite.Tests.csproj
```

**Post - build + deploy:**
```
& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" BannerlordSmithingSuite\src\SmithingSuite.csproj /p:Configuration=Debug /p:Platform=x64
```

**Post - in-game (test save):** AC8-AC12 by hand. BetterSmithing must be disabled for the run.

## Later slices (follow-up specs, not approved by this spec)

- **Slice 2 - Auto-Refine.** `GetRefiningFormulas(hero)` is per-hero and perk-gated
  (`CharcoalMaker`, `IronMaker`, `SteelMaker` 1-3); execute via `DoRefinement`. Aggressive by
  default per user preference, with optional minimum-stock guards.
- **Slice 3 - Perk-aware routing + research-point-aware smelt ordering.** Route by
  `PracticalSmelter` / `PracticalRefiner` (cost) and the part-learning perks; prefer smelting
  items whose `CraftingTemplate` still has unopened pieces, since research points accrue per
  template.
- **Slice 4 - Advisor core (engine-free).** Scoring over a plain weapon-stat struct: effective
  damage against representative armour tiers using the documented mitigation model
  (`Armor/(50+Armor)` plus per-damage-type penetration thresholds), swing/thrust speed, reach,
  handling, plus sale value as its own axis. Search strategy over pieces **and
  `scalePercentage`**, staged (blade first, then handle, then guard/pommel, then scale
  hill-climbing) under an evaluation budget, behind an `IDesignEvaluator` so it is testable
  with a fake evaluator.
- **Slice 5 - Advisor integration + inline UI.** Real evaluator via `Crafting.GenerateItem`;
  inline panel injected into `WeaponDesignVM` via UIExtenderEx; mounted/on-foot context toggle.
  **Constraint: optimises within the template the player already chose** - it does not pick the
  weapon type for them.
- **Slice 6 - MCM v5 settings.**

**Risk carried into Slice 4/5:** no public source quantifies "speed vs damage" weighting, so
any single composite score is a heuristic. Mitigation: read real engine stats rather than
inventing numbers, present named axes with explanations rather than one opaque score, and make
weights MCM-tunable. Prior art to differentiate against (feature-level only, no code):
Nexus "Smithing Optimizer" (11705 / 10541) and "Smithing Value" (3184).

## Originaltext

> ich möchte jetzt gern noch ein smithing mod machen

> primär ist es tatsächlich die produktion die ich haben möchte das skill leveling ist für mich
> ein sekundärer nebeneffekt ich habe bisher mods gehabt die das ganze feature quasi
> rausgenommen haben die die ausdauer komplett ignoriert haben und damit das smithing sehr
> überpowert haben [...] ich hätte gerne quasi die spielidee vom smithing trotzdem weiter

> smelten und refinen soll automatisiert erfolgen und das craften von waffen soll mit mehr infos
> versehen werden was am ende gut und was schlecht ist vor allem solche sachen wie schnelligkeit
> der waffe ist super wichtig während der waffenschaden ab einem gewissen grad auch irgendwann
> egal wird

> Da bei mir die Produktionsautomatisierung im Vordergrund steht, fände ich cool, wenn die
> Companions automatisch durchgeschaltet werden. [...] und da natürlich auch die Aufgaben auf die
> Companions verteilen, die jeweiligen Stärken haben, sprich irgendwelche Perks gerade gesetzt
> haben

> button wäre gut vielleicht wie ort trader button button bei jeder stadt

> Du kannst dir in BetterSmithing einfach Sachen abschauen, aber das soll am Ende nicht
> koexistent sein [...] Es soll quasi ein Autotrader für Smithing sein mit mehr, mit einer
> höheren Mächtigkeit

> beim Advisor geht es mir eher darum, dass wenn ich eine Waffe ausgewählt habe, dass der mir
> diese Waffe optimiert in der Form, in der ich sie ausgewählt habe, sprich quasi die
> bestmögliche Waffe rausholt aus dem, was ich habe [...] Das ist natürlich dann sowas wie Länge,
> bin ich beritten oder nicht eine Option, wie schnell kann ich das Schwert schwingen

> slice0 darf sie direkt scharf schalten, da ich in einem Testspiel das Zeug mache.

## Status

- Gate1: 2026-07-25 - problem confirmed ("passt, ab in Phase 2")
- Gate2: 2026-07-25 - spec approved ("passt"); Slice 1 released for implementation
- Surface: claude
- Mode: normal
- Welle 1: [x] #1 [x] #3 [x] #4
- Welle 2: [x] #9 (red: 8 tests, CS0103) [x] #2 (green: 8/8)
- Welle 3: [x] #5
- Welle 4: [x] #6
- Welle 5: [x] #7
- Welle 6: [x] #8
- Build: green (MSBuild Debug/x64, 0 warnings, 0 errors), deployed to Modules\SmithingSuite
- Tests: 8/8 green
- AC: see Akzeptanzkriterien
- Review: code-reviewer pass 2026-07-25 - no BLOCKER. Two SHOULD-FIX applied:
  (a) dead null-check on `CraftingHelper.GetAvailableHeroesForCrafting()` replaced by a real
  `PartyBase.MainParty?.MemberRoster` precondition check (the LINQ query source is evaluated
  eagerly, so the old post-call check could never fire);
  (b) direct unit tests added for `SmithSnapshot.Capacity` zero/negative-cost and negative-stamina
  guards. NICE-TO-HAVE items consciously not done: duplicate "nothing to smelt" string,
  per-action recomputation of locks/hero list, duplicate-smith dedup.
- Notes:
  - AC8-AC12 are **not** verified - they require a manual in-game run on the test save.
  - Repo is not initialised yet; a licence must be chosen before publishing (own module, no
    inherited licence). Out of scope for Slice 1.
  - Original wording "replace all smithing models" consciously deferred - see Ansatz.
  - BetterSmithing (Workshop 3435494593) is closed source with no licence -> functional
    reference only, no code taken. Its exact feature list could not be confirmed (Steam blocks
    automated fetch); web research suggests it is a bulk-action QoL mod.
