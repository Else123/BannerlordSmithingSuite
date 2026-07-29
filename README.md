# Bannerlord Smithing Suite

A gameplay mod for Mount & Blade II: Bannerlord (v1.4.7, War Sails) that takes the tedium out of
smithing **without taking the game out of it**.

## What it does

**Production automation.** One menu option in any town smelts and refines across every smith in your
party — cycling through them the way you would by hand, routing work by their perks, and stopping
when their crafting stamina runs out.

**A crafting advisor.** Weapon crafting stays a deliberate manual act, but the vanilla crafting
screen gets an inline panel that scores the current design on named, explained axes and can suggest
a better combination of pieces and part scales for the weapon form you already chose.

## The design principle

Existing smithing automation mods remove the crafting-stamina limit and make smithing overpowered.
This one does not. It calls the game's own `DoSmelting` and `DoRefinement` operations in a loop and
stops when stamina is spent — **it clicks faster, never cheaper**. That guarantee is structural, not
a matter of discipline: the mod cannot grant free production without deliberately bypassing the
engine, which it never does.

The one deliberate exception is opt-out and clearly marked: crafting stamina can optionally recover
while travelling, not only while parked in a settlement. It is off with a single switch.

The advisor's effective-damage numbers are the engine's own — the armour formula is taken verbatim
from `DefaultStrikeMagnitudeModel`, not from community guesswork. Only the *weighting between* axes
(speed vs reach vs damage) is opinion, and it is exposed as tunable settings that say so.

## Status

**In testing. Not released.** Production automation has been verified in-game; the crafting advisor
panel has not yet been confirmed on screen. Expect rough edges.

## Requirements

- Mount & Blade II: Bannerlord v1.4.7
- [Mod Configuration Menu v5](https://steamcommunity.com/sharedfiles/filedetails/?id=2859238197) (required)
- [UIExtenderEx](https://steamcommunity.com/sharedfiles/filedetails/?id=2859222409) (optional — only
  the inline crafting advisor needs it; everything else works without it)

## Building from source

1. Clone this repository
2. Copy `src/paths.props.user.template` to `src/paths.props.user`
3. Set `BannerlordPath` to your Bannerlord installation (the build also auto-detects common Steam
   library locations, so this is often unnecessary)
4. Build `src/SmithingSuite.csproj` with MSBuild — a post-build step deploys the module into
   `<game>/Modules/SmithingSuite/`

The engine-free logic layers (`src/Logic`, `src/Advisor`) are unit-tested without a game install:

```
dotnet test tests/SmithingSuite.Tests/SmithingSuite.Tests.csproj
```

## Design documents

The `SPEC*.md` files record how the mod was designed, slice by slice — including the decompiled
engine anchors each decision rests on, and the corrections made when in-game testing disproved an
assumption.

## License

MIT — see [LICENSE](LICENSE).
