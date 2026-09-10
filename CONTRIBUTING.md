# Contributing to Smithing Suite

Thank you for your interest in contributing to the Smithing Suite mod for Mount & Blade II:
Bannerlord! This document covers setup, build and the conventions the codebase follows.

## Prerequisites

- **Visual Studio 2022 or later** (MSBuild for the old-style csproj)
- **.NET Framework 4.8 SDK** (mod DLL)
- **.NET SDK** matching `net10.0` (engine-free unit tests)
- **Mount & Blade II: Bannerlord**, v1.4.7 or later (built against v1.4.8, War Sails)

## Initial setup

### 1. Clone the repository

```
git clone git@github.com:Else123/BannerlordSmithingSuite.git
```

### 2. Configure your environment

The build resolves your Bannerlord install path via `Directory.Build.props`, which auto-detects
common Steam library locations. If auto-detection fails, point it at your install explicitly:

```
cd BannerlordSmithingSuite/src
copy paths.props.user.template paths.props.user
```

Then edit `paths.props.user` and set `BannerlordPath`:

```xml
<BannerlordPath>YOUR_PATH_HERE\Mount &amp; Blade II Bannerlord</BannerlordPath>
```

`paths.props.user` is gitignored and never committed — it holds a path specific to your machine.

## Build and deployment

Build `src/SmithingSuite.csproj` with MSBuild:

```
& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" src\SmithingSuite.csproj /p:Configuration=Debug /p:Platform=x64
```

- **Debug** is what you build against while developing.
- **Release** (`/p:Configuration=Release /p:Platform=x64`) is the configuration to ship.

A post-build step deploys the DLL, PDB, `SubModule.xml`, the advisor's UI prefab XML files and the
English language files into `<BannerlordPath>\Modules\SmithingSuite\`, matching the structure
Bannerlord expects:

```
Modules\SmithingSuite\
    SubModule.xml
    bin\Win64_Shipping_Client\
        SmithingSuite.dll
        SmithingSuite.pdb
    GUI\Prefabs\
        AdvisorTriggerButton.xml
        AdvisorPopup.xml
    ModuleData\Languages\
        language_data.xml
        std_smithingsuite_strings.xml
```

## Running the tests

The engine-free logic layers (`src/Logic`, `src/Advisor`) are unit-tested with xUnit on the host
.NET SDK — no game install required:

```
dotnet test tests/SmithingSuite.Tests/SmithingSuite.Tests.csproj
```

Per workspace convention, run only the tests affected by your change during development, not the
whole suite on every keystroke.

## The layering rule (read this before adding a file)

`src/Logic` and `src/Advisor` are **engine-free**: plain C# using only `System.*`, no
`TaleWorlds.*` reference. The test project compiles these files directly by source
(`<Compile Include>` linking, not a project reference), which is what lets the tests run without a
game install.

**A `TaleWorlds` reference anywhere in `src/Logic` or `src/Advisor` breaks every test in the
suite** — the test project cannot resolve it and fails to compile. Anything that needs the engine
belongs in `src/Integration` or `src/UI` instead, behind the existing connector interfaces
(`ISmithingConnector`, `IDesignEvaluator`).

## Adding a new file

The mod project (`src/SmithingSuite.csproj`) is an **old-style csproj**: new `.cs` files are not
picked up automatically. Add an explicit `<Compile Include="...">` entry in the relevant
`<ItemGroup>`, or MSBuild will silently not compile the file. The same applies to
`tests/SmithingSuite.Tests/SmithingSuite.Tests.csproj` when adding new engine-free source that the
tests should cover.

## Language constraint

The mod targets **C# 7.3** (`LangVersion` in the csproj, matching Bannerlord's own Unity/Mono
toolchain). Avoid newer C# syntax (e.g. records, pattern-matching features beyond 7.x, top-level
statements) in `src/` — it will fail to build even though the test project's modern SDK accepts it.

## License

By contributing to this project, you agree that your contributions will be licensed under the
same license as the project (MIT).

---

Thank you for contributing to Smithing Suite!
