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
