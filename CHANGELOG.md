# Changelog

All notable changes to this mod are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [Unreleased]

### Fixed

- The "Smelt and refine at the smithy" town menu option now sits directly after "Enter smithy".
  It used to land near the top of the menu because it was registered before the game had built
  the town menu.

### Changed

- Errors and caught exceptions are now always written to `smithingsuite.log`, tagged `[ERROR]`,
  even with Verbose Logging off. Previously a failure left no trace unless verbose logging was on.
  Informational and trace lines, including expected fallbacks such as an unknown crafting
  template from another mod, are still written only in verbose mode.

### Known issues

- The crafting advisor popup has never been confirmed on screen in-game. Production automation
  (smelting and refining) has been verified in-game; the advisor has not.

## [1.0.0]

Initial version.

### Added

- **Automatic smelting and refining.** A single town menu option smelts and refines across every
  smith in the party, the way you would by hand, but faster — never cheaper. It calls the game's
  own smelting/refining operations in a loop and stops when a smith's crafting stamina runs out.
- **Perk-aware routing.** Smelting prefers items whose crafting template still has unopened parts,
  and routes that work to smiths with the `CuriousSmelter` perk; bulk smelting and refining prefer
  `PracticalSmelter` / `PracticalRefiner` smiths, whose lower stamina cost yields more actions.
- **Phase stamina budget.** Smelting and refining each get a configurable share of a smith's
  stamina (50/50 by default) so refining is no longer starved by smelting spending it all first.
- **Optional travel stamina recovery.** Crafting stamina can recover while the party travels
  between settlements, not only while parked in one — vanilla only recovers stamina in a
  settlement. Off by a single switch; the rate mirrors vanilla's own formula.
- **Crafting advisor (unverified in-game).** An inline popup in the vanilla crafting screen scores
  the current weapon design on named, explained axes — effective damage against representative
  armour tiers, attack speed, reach, handling, and sale value as its own axis — and can suggest a
  better combination of pieces and part scales within the weapon form already chosen. Damage
  numbers come from the game's own armour-penetration formula, not community guesswork; only the
  weighting between axes is opinion, and it is exposed as tunable settings that say so.
- **MCM v5 settings**, organised into General (logging), Production (smelt/refine stamina split),
  Travel (recovery toggle and rate), and Advisor (axis weights, evaluation budget) groups.
- **Verbose logging**, off by default, writable to
  `Documents\Mount and Blade II Bannerlord\SmithingSuite\smithingsuite.log` when enabled in MCM.

### Save safety

Nothing is persisted in the save. Uninstalling has one lasting side effect: heroes keep whatever
crafting stamina they recovered while travelling; vanilla simply stops topping it up outside
settlements from then on.
