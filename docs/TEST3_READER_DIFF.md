# Ability-reader comparison with v0.3.21

Both readers were built from their actual source revisions and run against the same supplied dumps and offline game staging. Each loaded 4,097 blueprints with the same one failed export and classified the same 1,220 ability rows with zero reader errors. This compares blueprint-reader classification, not live scores, commands or all 4,801 abilities in the author’s larger dataset.

Exactly 42 rows change classification: 29 modifier configurations, one Gather Power preparation, three debuffs recognized by their penalties, and nine former guessed buff values removed. The other 1,178 classifications/details match. Quiver still has a dedicated runtime recovery path despite losing its generic buff guess.

The nine guessed-to-zero changes are **coverage limitations**, not evidence that those game abilities have no benefit. They predate the latest cache optimizations; do not attribute faster decisions to complete preservation of their former guessed scores. Retaliation, Shroud of Water and Evasion-style defenses remain incompletely valued.

| Ability blueprint | v0.3.21 reader | Current reader |
| --- | --- | --- |
| ArmorPiercingInfusionActivatableAbility | toggle not managed: a modifier of another action (AA-4) | blast modifier: configuration candidate |
| BleedingInfusionAbility | toggle not managed: a modifier of another action (AA-4) | blast modifier: configuration candidate |
| BowlingInfusionAbility | toggle not managed: a modifier of another action (AA-4) | blast modifier: configuration candidate |
| BurningInfusionAbility | toggle not managed: a modifier of another action (AA-4) | blast modifier: configuration candidate |
| ChainArrowsInfusionActivatableAbility | toggle not managed: a modifier of another action (AA-4) | blast modifier: configuration candidate |
| ChillingInfusionAbility | toggle not managed: a modifier of another action (AA-4) | blast modifier: configuration candidate |
| ClusterArrowsInfusionAbility | toggle not managed: a modifier of another action (AA-4) | blast modifier: configuration candidate |
| DazzlingInfusionAbility | toggle not managed: a modifier of another action (AA-4) | blast modifier: configuration candidate |
| ElementalBastionAbility | GUESSED: buff worth ~0 (marker 0.05) | nothing: buff worth 0 |
| EntanglingInfusionAbility | toggle not managed: a modifier of another action (AA-4) | blast modifier: configuration candidate |
| EnvelopingWindsAbility | GUESSED: buff worth ~0 (marker 0.05) | nothing: unreadable buff on a limited-use ability (worth 0) |
| FaerieFire | GUESSED: debuff (0.25 of worth) | valued: debuff by its penalties |
| FlameShieldAbility | GUESSED: buff by old quality (unread 0.5) | nothing: buff worth 0 |
| FlashInfusionAbility | toggle not managed: a modifier of another action (AA-4) | blast modifier: configuration candidate |
| FoxfireAbility | GUESSED: debuff (0.25 of worth) | valued: debuff by its penalties |
| FoxfireInfusionAbility | toggle not managed: a modifier of another action (AA-4) | blast modifier: configuration candidate |
| GatherPower | valued: cleanse, removal (+ guessed buff by old quality (unread 0.5)) | blast preparation: Gather Power |
| GatherPowerModeHigh | toggle not managed: not activated by a command | blast modifier: configuration candidate |
| GatherPowerModeLow | toggle not managed: not activated by a command | blast modifier: configuration candidate |
| GatherPowerModeMedium | toggle not managed: not activated by a command | blast modifier: configuration candidate |
| GrapplingInfusionAbility | toggle not managed: a modifier of another action (AA-4) | blast modifier: configuration candidate |
| GutWrenchingInfusionAbility | toggle not managed: a modifier of another action (AA-4) | blast modifier: configuration candidate |
| JaggedFleshAbility | GUESSED: buff by old quality (unread 0.5) | nothing: buff worth 0 |
| KineticSharpshooterCreateQuiverAbility | GUESSED: buff by old quality (unread 0.5) | nothing: buff worth 0 |
| MagneticInfusionAbility | toggle not managed: a modifier of another action (AA-4) | blast modifier: configuration candidate |
| MetakinesisEmpowerAbility | toggle not managed: not activated by a command | blast modifier: configuration candidate |
| MetakinesisEmpowerCheaperAbility | toggle not managed: not activated by a command | blast modifier: configuration candidate |
| MetakinesisMaximizedAbility | toggle not managed: not activated by a command | blast modifier: configuration candidate |
| MetakinesisMaximizedCheaperAbility | toggle not managed: not activated by a command | blast modifier: configuration candidate |
| MetakinesisQuickenAbility | toggle not managed: not activated by a command | blast modifier: configuration candidate |
| MetakinesisQuickenCheaperAbility | toggle not managed: not activated by a command | blast modifier: configuration candidate |
| PureFlameInfusionAbility | toggle not managed: a modifier of another action (AA-4) | blast modifier: configuration candidate |
| PushingInfusionAbility | toggle not managed: a modifier of another action (AA-4) | blast modifier: configuration candidate |
| RareMetalInfusionAbility | toggle not managed: a modifier of another action (AA-4) | blast modifier: configuration candidate |
| SearingFleshAbility | GUESSED: buff worth ~0 (marker 0.05) | nothing: unreadable buff on a limited-use ability (worth 0) |
| ShroudOFWaterArmorAbility | GUESSED: buff worth ~0 (marker 0.05) | nothing: unreadable buff on a limited-use ability (worth 0) |
| ShroudOFWaterShieldAbility | GUESSED: buff worth ~0 (marker 0.05) | nothing: unreadable buff on a limited-use ability (worth 0) |
| ShroudOFWaterUpgradeAbility | GUESSED: buff worth ~0 (marker 0.05) | nothing: unreadable buff on a limited-use ability (worth 0) |
| SymbolOfRevelation | GUESSED: debuff (0.25 of worth) | valued: debuff by its penalties |
| SynapticInfusionAbility | toggle not managed: a modifier of another action (AA-4) | blast modifier: configuration candidate |
| UnravelingInfusionAbility | toggle not managed: a modifier of another action (AA-4) | blast modifier: configuration candidate |
| VampiricInfusionAbility | toggle not managed: a modifier of another action (AA-4) | blast modifier: configuration candidate |

Full verification scope, source inspection and replay requirements are in [TEST3_COMPARISON.md](TEST3_COMPARISON.md). Neither these reader results nor the unit suite establish complete live-actor or third-party-mod parity.
