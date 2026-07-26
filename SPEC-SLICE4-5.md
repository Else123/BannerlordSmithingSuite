# Smithing Suite - Slices 4 + 5: Crafting Advisor

Follow-up spec to `SPEC.md`, `SPEC-SLICE2.md`, `SPEC-SLICE3.md`.
This is the mod's **second pillar**: crafting itself stays a deliberate manual act, but the player
gets real, explained feedback while designing.

Two slices, specified together because the scoring model (4) is meaningless without a way to see
it (5), but built and verified separately - 4 is engine-free and unit-testable, 5 is the riskiest
UI work in the whole mod.

## Problem

Vanilla's crafting screen shows raw numbers and no judgement. The player picks parts largely
blind: the displayed damage figure does not answer "is this actually good?", because what wins
fights is getting through armour fast enough, not the biggest number on the sheet. Existing mods
either solve-and-present (a one-shot "optimize" button) or add a single missing figure; none
**explain**, and none let the player stay in control of the design while still being told what
their choice costs them.

The user's ask, precisely: *pick a weapon form, then have the mod extract the best possible weapon
of that form* - including the **length/scale of the individual pieces** - weighted towards
swinging fast and getting through, with damage as one factor rather than the goal.

## Kontext

### Verified engine anchors (all [V-BIN], decompiled 2026-07-25)

```csharp
// TaleWorlds.Core.Crafting - produces the REAL item with engine-computed stats.
// The advisor reads these numbers instead of inventing any.
public static void GenerateItem(WeaponDesign weaponDesign, TextObject name, BasicCultureObject culture,
    ItemModifierGroup modifierGroup, ref ItemObject item, string itemId);

// TaleWorlds.Core.WeaponDesignElement - THE part-scaling lever ("length of the individual pieces")
public static WeaponDesignElement CreateUsablePiece(CraftingPiece craftingPiece, int scalePercentage = 100);

// TaleWorlds.Core.WeaponDesign
WeaponDesignElement[] UsedPieces;  CraftingTemplate Template;
float CraftedWeaponLength;  float TotalLength;  float[] PiecePivotDistances;

// TaleWorlds.Core.CraftingPiece : MBObjectBase
PieceTypes PieceType;      // Blade / Guard / Handle / Pommel
int PieceTier;  MBReadOnlyList<(CraftingMaterials,int)> MaterialsUsed;
bool IsGivenByDefault;  bool IsHiddenOnDesigner;  static MBReadOnlyList<CraftingPiece> All;

// TaleWorlds.Core.CraftingTemplate : MBObjectBase
List<CraftingPiece> Pieces;  PieceData[] BuildOrders;  WeaponDescription[] WeaponDescriptions;
bool IsPieceTypeUsable(PieceTypes);  static CraftingTemplate GetTemplateFromId(string);

// gating
int SmithingModel.CalculateWeaponDesignDifficulty(WeaponDesign);   // vs hero's smithing skill
bool ICraftingCampaignBehavior.IsOpened(CraftingPiece, CraftingTemplate);  // unlocked parts only

// UI injection target - namespace TaleWorlds.CampaignSystem.ViewModelCollection.WeaponCrafting.WeaponDesign
// in TaleWorlds.CampaignSystem.ViewModelCollection.dll (NOT SandBox.ViewModelCollection.dll)
public class WeaponDesignVM
{
    public ItemObject CraftedItemObject;                       // public FIELD - the current design's item
    CraftingPieceVM SelectedBladePiece/SelectedGuardPiece/SelectedHandlePiece/SelectedPommelPiece;
    MBBindingList<CraftingPieceListVM> PieceLists;
    MBBindingList<WeaponDesignResultPropertyItemVM> DesignResultPropertyList;
    int CurrentDifficulty; int MaxDifficulty; int CurrentHeroCraftingSkill;
}
```

### The armour model - [OPEN-critical] CLOSED 2026-07-25, [V-BIN]

Found in managed code: `TaleWorlds.MountAndBlade.DefaultStrikeMagnitudeModel` (implements
`ComponentInterfaces.StrikeMagnitudeCalculationModel`), called from
`MissionCombatMechanicsHelper.ComputeBlowDamage`. **All coefficients are hardcoded IL literals -
there is no XML/`ManagedParameters` override.** Verbatim:

```csharp
public override float ComputeRawDamage(DamageTypes damageType, float magnitude,
    float armorEffectiveness, float absorbedDamageRatio)
{
    float bluntFactor = GetBluntDamageFactorByDamageType(damageType); // Cut 0.1, Pierce 0.25, Blunt 0.6
    float mitigation  = 50f / (50f + armorEffectiveness);
    float mitigated   = magnitude * mitigation;
    float blunted     = bluntFactor * mitigated;
    float penetrating;                       // threshold subtracted from the ALREADY-mitigated magnitude
    switch (damageType)
    {
        case DamageTypes.Cut:    penetrating = MathF.Max(0f, mitigated - armorEffectiveness * 0.5f);  break;
        case DamageTypes.Pierce: penetrating = MathF.Max(0f, mitigated - armorEffectiveness * 0.33f); break;
        case DamageTypes.Blunt:  penetrating = MathF.Max(0f, mitigated - armorEffectiveness * 0.2f);  break;
        default: return 0f;
    }
    return (blunted + (1f - bluntFactor) * penetrating) * absorbedDamageRatio;
}

public enum DamageTypes { Invalid = -1, Cut, Pierce, Blunt, NumberOfDamageTypes }  // TaleWorlds.Core
```

**Correction to the community model this spec previously assumed:** the numbers are all right, but
the mechanics were described wrongly. It is **not** "penetrates -> full damage, else a harsh
multiplier". Both terms are **always blended**: `bluntFactor * mitigated` plus
`(1 - bluntFactor) * max(0, mitigated - threshold)`. Below the threshold the second term is 0 and
the result collapses to `bluntFactor * mitigated`, which is why the either/or description happened
to match there. `ArmourModel.cs` must implement the blend, and AC1's threshold tests must assert
against it - not against the community wording.

`absorbedDamageRatio` (from `AttackInformation.VictimAgentAbsorbedDamageRatio`, normally 1.0) is a
final multiplier the community model omits entirely; the advisor treats it as 1.0 and says so.

**Consequence for the design:** effective-damage-vs-armour is now **engine truth, not a heuristic**.
Only the *weighting between axes* (speed vs reach vs damage) remains opinion - keep that tunable.

[PARTIAL] How the armour NUMBER itself is aggregated per body part
(`Agent.GetBaseArmorEffectivenessForBodyPart` -> `AgentDrivenProperties`) is engine-fed; the advisor
therefore evaluates against representative armour VALUES rather than simulating a specific victim.

### Honest limitation, carried from research

No public source quantifies "speed vs damage" as a coefficient. **Do not invent one and present it
as fact.** Consequences for the design, non-negotiable:
- read real engine stats, never model them;
- present **named axes with explanations**, not one opaque number;
- any composite weighting is a **tunable default**, labelled as opinion, not truth.

Prior art to differentiate against (feature level only, no code taken - both closed):
Nexus "Smithing Optimizer" (11705 / 10541) solves and presents; "Smithing Value" (3184) adds the
gold figure. Neither explains while you design. That gap is this slice's reason to exist.

## Ansatz + Architektur

### Slice 4 - scoring and search (engine-free)

`SmithingSuite.Advisor` namespace, pure `System.*`, unit-testable without a game install.

- `WeaponStats` - a plain struct/class of the numbers the engine produced: swing/thrust speed and
  damage, damage types, handling, weapon length, weight, tier, value.
- `IDesignEvaluator` - `WeaponStats Evaluate(DesignCandidate candidate)`. **The seam**: the real
  implementation (Slice 5) calls `Crafting.GenerateItem`; tests use a fake with hand-set numbers.
  This is what keeps the search algorithm testable without the game.
- `ArmourModel` - effective damage of a given (damage, damageType) against a given armour value,
  implementing the verified formula. Pure arithmetic, heavily unit-tested at the thresholds.
- `WeaponScore` - **named axes**, each with a value and a human-readable reason:
  effective damage against representative armour tiers (e.g. 20 / 40 / 60), attack speed, reach,
  handling, and **sale value as its own separate axis** (best fighter and best earner diverge -
  the user sells crafted weapons as a primary income source).
- `MountedContext { OnFoot, Mounted }` shifts axis weights (reach matters more mounted, swing
  speed more on foot) - the user named this explicitly.
- `DesignOptimizer` - searches piece choices **and `scalePercentage`** within the template the
  player already chose, subject to: only unlocked pieces, and `CalculateWeaponDesignDifficulty`
  within the smith's skill.

**Search budget is a first-class requirement.** Four slots x dozens of pieces x scale steps is
combinatorially far too large to brute-force between keystrokes. Staged search:
blade first (dominant), then handle, then guard/pommel, then hill-climb `scalePercentage`;
hard cap on evaluations; deterministic tie-breaks. The optimizer must never block the UI thread
for a perceptible time - if the budget is exhausted it returns the best found so far, labelled as
such.

### Slice 5 - inline in the vanilla screen

- `IDesignEvaluator` implemented against `Crafting.GenerateItem`.
- **UIExtenderEx** mixin on `WeaponDesignVM` (already an installed dependency), reading
  `CraftedItemObject` and the selected pieces, showing the axes + the suggested improvement inline.
- Must degrade safely: if the ViewModel shape changes in a game update, the panel disappears and
  crafting still works. **No Harmony patching of the crafting screen.**
- Adds `Bannerlord.UIExtenderEx` to `SubModule.xml` DependedModules and the csproj references.

## Scope

**In (Slice 4):** `WeaponStats`, `IDesignEvaluator`, `ArmourModel`, `WeaponScore` with named axes,
`MountedContext`, `DesignOptimizer` with a bounded staged search, and their unit tests.

**In (Slice 5):** real evaluator, UIExtenderEx mixin + inline panel, mounted/foot toggle,
dependency wiring.

**Out of scope:** automating crafting itself (the player always presses the button); picking the
weapon *type* for the player (the advisor optimises **within** the chosen template); changing any
vanilla balance; armour crafting; MCM settings (Slice 6, which will expose the axis weights).

## Akzeptanzkriterien

Slice 4, unit-testable with a fake evaluator:
- [ ] AC1 `ArmourModel` reproduces the verified formula exactly at, just below, and just above
      each damage type's penetration threshold.
- [ ] AC2 A lower-damage design that penetrates scores higher than a higher-damage design that
      does not - the whole point of modelling armour.
- [ ] AC3 The optimizer never proposes a piece that is not unlocked.
- [ ] AC4 The optimizer never proposes a design whose difficulty exceeds the smith's skill.
- [ ] AC5 The optimizer only proposes designs within the template it was given.
- [ ] AC6 `scalePercentage` is actually varied - a case exists where the winner differs from the
      same pieces at 100%.
- [ ] AC7 The evaluation count never exceeds the configured budget; on exhaustion the best-so-far
      is returned and flagged as incomplete.
- [ ] AC8 Results are deterministic: the same inputs yield the same recommendation.
- [ ] AC9 `MountedContext` changes the ranking in at least one constructed case.
- [ ] AC10 Every axis carries a non-empty human-readable reason.
- [ ] AC11 Sale value is reported as its own axis and never silently folded into the combat score.

Slice 5, in-game (manual):
- [ ] AC12 The panel appears inside the vanilla crafting screen and updates as pieces change.
- [ ] AC13 The suggested design is craftable by the current smith with the current unlocked parts.
- [ ] AC14 Applying the suggestion by hand reproduces the predicted stats.
- [ ] AC15 With the mixin disabled or failing, the vanilla crafting screen still works normally.

## Umsetzungs-Schritte (TODOs)

**Slice 4**

1. **[S]** haengt ab von: - | betrifft: (research only, no file)
   Close the **[OPEN-critical]** armour formula against the binary (`CombatStatCalculator`,
   damage-calculation model). Record the verified formula in this spec before any scoring code.
2. **[P]** haengt ab von: 1 | betrifft: `src/Advisor/WeaponStats.cs`, `src/Advisor/MountedContext.cs`
   Plain engine-free value types.
3. **[P]** haengt ab von: 1 | betrifft: `tests/SmithingSuite.Tests/ArmourModelTests.cs`
   Red tests for AC1-AC2 from the verified formula.
4. **[S]** haengt ab von: 3 | betrifft: `src/Advisor/ArmourModel.cs`
   Implement to green.
5. **[S]** haengt ab von: 2 | betrifft: `src/Advisor/IDesignEvaluator.cs`, `src/Advisor/DesignCandidate.cs`
   The seam plus the candidate description (pieces per slot + scale per piece).
6. **[P]** haengt ab von: 4,5 | betrifft: `tests/SmithingSuite.Tests/WeaponScoreTests.cs`, `tests/SmithingSuite.Tests/DesignOptimizerTests.cs`
   Red tests for AC3-AC11 using a fake `IDesignEvaluator`.
7. **[S]** haengt ab von: 6 | betrifft: `src/Advisor/WeaponScore.cs`, `src/Advisor/ScoreWeights.cs`
   Named axes + reasons; weights as labelled tunable defaults.
8. **[S]** haengt ab von: 7 | betrifft: `src/Advisor/DesignOptimizer.cs`
   Bounded staged search to green.

**Slice 5** (own gate - do not start before Slice 4 is green and reviewed)

9. **[S]** haengt ab von: 8 | betrifft: `src/Integration/EngineDesignEvaluator.cs`
   `IDesignEvaluator` via `Crafting.GenerateItem`, converting the resulting `ItemObject` /
   `WeaponComponentData` into `WeaponStats`.
10. **[S]** haengt ab von: 9 | betrifft: `src/SubModule.xml`, `src/SmithingSuite.csproj`
    Add the UIExtenderEx dependency and references.
11. **[S]** haengt ab von: 10 | betrifft: `src/UI/WeaponDesignAdvisorMixin.cs`
    UIExtenderEx mixin on `WeaponDesignVM`; fail-safe if the VM shape changed.
12. **[S]** haengt ab von: 11 | betrifft: `src/UI/AdvisorPanel.xml` (prefab) + registration
    The inline panel with axes, reasons, and the suggestion.

Welle 1: #1 - Welle 2 (parallel): #2, #3 - Welle 3: #4, #5 - Welle 4: #6
Welle 5: #7 - Welle 6: #8 - [GATE] - Welle 7: #9 - Welle 8: #10 - Welle 9: #11 - Welle 10: #12

## Verification

**Pre:** TODO 1 must be closed - no scoring code before the armour formula is binary-verified.

Post (per wave):
```
dotnet test BannerlordSmithingSuite\tests\SmithingSuite.Tests\SmithingSuite.Tests.csproj
```
```
& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" BannerlordSmithingSuite\src\SmithingSuite.csproj /p:Configuration=Debug /p:Platform=x64
```
In-game: AC12-AC15 on the test save.

## Originaltext

> beim Advisor geht es mir eher darum, dass wenn ich eine Waffe ausgewählt habe, dass der mir
> diese Waffe optimiert in der Form, in der ich sie ausgewählt habe, sprich quasi die
> bestmögliche Waffe rausholt aus dem, was ich habe, beispielsweise wenn ich ein zweihändiger
> Tischschwert habe, dass dieses dann halt auch von der Länge der einzelnen Teile am besten
> ausgesucht ist. Das ist natürlich dann sowas wie Länge, bin ich beritten oder nicht eine Option,
> wie schnell kann ich das Schwert schwingen und sowas ist dann halt interessant, damit ich halt
> gut durchkomme und weniger wie viel Schaden es tatsächlich macht

> ich würde den crafting advisor direkt im vanilla screen drin haben ansonsten macht er nicht so
> viel sinn und die tiefe sollte auch für spätere und vor allem in der kombination mit anderen
> mods geschaut werden aber am besten so tief wie möglich

## Status

- Gate1: 2026-07-25 - inherited from SPEC.md
- Gate2: 2026-07-25 - released via "weiter mit allem" (Slice 5 keeps its own gate after Slice 4)
- Surface: claude
- Mode: normal
- Welle 1: [ ] #1 (armour formula - BLOCKING)
- Welle 2: [ ] #2 [ ] #3
- Welle 3: [ ] #4 [ ] #5
- Welle 4: [ ] #6
- Welle 5: [ ] #7
- Welle 6: [ ] #8
- Welle 7-10 (Slice 5): [ ] #9 [ ] #10 [ ] #11 [ ] #12
- AC: see Akzeptanzkriterien
- Notes:
  - The armour formula is currently community-sourced and UNVERIFIED. TODO 1 exists precisely so
    the score is not built on it unchecked.
  - No public source quantifies speed-vs-damage weighting; the composite weights are declared
    opinion and must stay user-tunable (Slice 6).
