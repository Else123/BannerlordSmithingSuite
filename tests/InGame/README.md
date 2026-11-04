# In-game test scenarios

`SmithingSuite.Scenarios` is a plugin module for the Bannerlord test rig (the sibling
`BannerlordTestRig` repo). It holds `IScenario` implementations that the rig's harness
(`TestRig.Harness`) discovers and runs inside a real campaign.

It exists for the test rig only. It is not part of the shipped mod, not in the normal mod build
and not in any solution; never install it for normal play.

## Build

The rig runner builds it, not you. It builds `src/SmithingSuite.csproj`, then the harness, then
`SmithingSuite.Scenarios/SmithingSuite.Scenarios.csproj`, all with the same
`/p:BannerlordModulesPath=<stage folder>`. This project references the staged `SmithingSuite.dll`
and `TestRig.Harness.dll` from that folder and copies its own DLL, PDB and `SubModule.xml` to
`<stage folder>\SmithingSuite.Scenarios\`.

## Scenarios

| Id | Class | Checks |
|----|-------|--------|
| `smithing.smelt-then-refine` (SM-1) | `SmeltThenRefineScenario` | one run smelts and refines, removes exactly the smelted weapons, keeps smelting within the stamina share |
| `smithing.nothing-to-do` (SM-2) | `NothingToDoScenario` | with no crafted weapons and no crafting materials but full stamina, one run reports `NothingHappened` with no entries and leaves item counts, every smith's stamina and the main hero's gold unchanged; setup checks the connector sees every smith and removes those items for good |
| `smithing.travel-stamina` (SM-3) | `TravelStaminaScenario` | outside any settlement, with stamina 0 and multiplier 0.6 set on the MCM instance only (the mod's hourly tick must bridge it), each party hero gains exactly `hours x Round(vanilla hourly rate x 0.6)` over `Days(1)`; setup checks the max stamina leaves room for 24 hours of gain and `hours.max` checks that at most 24 hours passed, so the clamp cannot mask a wrong gain; MCM values and stamina are restored afterwards |
