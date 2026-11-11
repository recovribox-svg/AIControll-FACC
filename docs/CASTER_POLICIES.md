# Caster policies and native Magic Fusion

Task branch: `codex/ranged-pure-caster`, starting at C06R5 / PR #32 `0f3061f`, over development `1f58150`.
The author's v0.3.29 feedback describes ranged mages choosing staff/crossbow attacks and failing to use created
Magic Fusion spells. These are reported symptoms, not a new playtest of this branch. The supplied architectural
plan and Magic Deceiver guide are advisory; [native evidence and input fingerprints](competence/CASTER_NATIVE_EVIDENCE.json)
record the actual contracts. [Final checks](competence/CASTER_CHECKS.json) identify the tested source and package.

## Ranged remains a preference; Pure Mage is an explicit opt-in

Ordinary Ranged characters keep weapon attacks and their existing scoring. The new per-character **Pure Mage**
checkbox is shown under Ranged and defaults to false, including old JSON files. Mixed/Melee suspend the retained flag.
Changing the setting applies to subsequent AI decisions; it does not replace the command owner or cancellation policy.

With both Ranged and Pure Mage selected:

- No ordinary melee/ranged weapon fallback, Charge, Vital Strike, compound melee strikes, Coup de Grace or mundane
  weapon/maneuver abilities. Admission happens before dependent movement/boost choices, at the final candidate filter
  and again before issuing the command.
- Spells, cantrips, kinetic blasts and native ranged spell commands remain eligible. An active, enabled, unsuppressed
  native energy-dice buff can authorize a ranged shot only with a matching primary weapon and positive saved-context
  simple/rank values. Conditional, critical-only, random, custom-value and other unverified rider mechanisms do not.
- Such an energy shot is **one native attack** in TB and RT. Its preflight command and actual command use the same flags;
  a one-use rider cannot authorize an entire otherwise physical full attack. Native procs retain native execution.
- A reusable low-value damaging cantrip may replace the old low-value weapon fallback. It must be an available level-zero
  book spell with no item/spent material and an actual negative (unlimited) native cast count. Paid spells keep their bar.
- No offensive physical touches. Support touches already in range and self-heals remain eligible. Walking for a touch
  heal requires a critical recipient, a real healing branch and the existing approach/risk checks: no known AoO,
  interception, harmful area, swarm or melee/full-attack exposure. Final path prices and recipient position are rechecked.
  Missing native views fail closed. Normal healing thresholds still govern ranged heals.

Buff offence forecasts do not assume a forbidden staff/crossbow follow-up. Weapon-only advances are omitted when the
mode forbids the underlying attack. Affordable spell approaches and the existing escapes remain available. There is no
new long-range retreat priority or spell-only advance beyond the existing affordable approach. A mage can therefore wait
when no permitted useful cast is reachable; broader positioning would need a separate movement proposal under the author's
standing boundary. General movement, commitment, retreat, pathfinding and stuck-walk cancellation are not retuned.

## Reach uses the actual cast's delivery

Source touch spells are resolved to their native sticky-touch delivery for attack forecasting. Native `HasMetamagic`
decides whether Reach applies, including the caster's actual special-metamagic state. Mere rod ownership is insufficient.
`AbilityDeliverTouch` uses the native ray weapon for Reach; the forecast now does too. Attack stat, touch AC and ranged
attack-roll risk follow that weapon. Reach does not mutate the shared blueprint profile and avoids the physical-touch
pre-walk workaround; normal cast range/LOS and dispatcher preparation still apply.

The inspected `AddFamiliar` contract supplies a visual familiar, without a verified controllable touch-delivery command.
No synthetic familiar cast, teleport, guessed delivery chance or familiar-specific scoring multiplier is added.

## Spell-use modes

| Displayed mode | Behavior |
| --- | --- |
| Conservative | The existing lowest-viable-tier gate and native boss bypass. Buffs, heals, items and cantrips retain their rules. |
| Standard | Existing behavior, including the easy-fight discount on offensive daily slots. |
| Aggressive | Removes that discount for offensive book spells. Native resistance, immunity, overkill, friendly fire and casting/movement risks still determine value. |

The internal persisted member stays `Progressive = 1`, displayed as Conservative, to preserve numeric **and textual**
old settings. Standard remains 0; Aggressive is appended as 2. No duplicate enum aliases or save migration are needed.
The guide's broader high-CR/wipe-risk bypass is not invented: the existing gate and explicit native boss bypass remain.
There is no spell-cycling counter. Aggressive does not remove bomb, item, burn, toggle, healing or other resource prices,
and does not apply the plan's second resistance/overkill multiplier to effects already valued through those rules.

The shared tactical engine implements these features in both combat modes. When the retired RT fallback is selected,
explicit Pure Mage users, Aggressive spellcasters and native Magic Deceiver spellbooks use the shared engine. Ordinary
non-casters retain their selected backend. The retired engine itself is not adapted.

## Bounded native Magic Fusion support

Native UI-created fusions were already discovered through `GetAllCustomSpells`. Their fixed slot blueprints have empty
effects/target flags; interpreting only that blueprint lost the two source payloads. The reader now uses `MagicHackData`:

1. Read the selected delivery blueprint's actual targets, geometry, range and attack roll.
2. Walk the two ordered native `AbilityEffectRunAction` payloads, with the game's sticky-touch fallback. Do not separately
   cast either source spell or inherit each source's original projectile/touch shape.
3. Retain the chosen shared save. Weight each damage action's native `HalfIfSaved`/failed/passed branch, including Evasion,
   without an additional global damage-save discount. The same save's useful-outcome probability informs Conservative.
4. Ask the actual spellbook's `MagicHackSpellbookComponent` for its per-action damage/stat-damage dice cap. Flat bonuses
   remain; native energy drain has its own dice path. The supplied level-six value is 100, not literal infinity.
5. Cache immutable metadata per live fusion configuration with weak keys. Edits to either source, delivery, level, school,
   save, target type, touch flag or extra area rebuild it. Discovery/cast/failure keys include configuration; condition and
   target-free context caches distinguish configurations. Only scalar opportunity chances live in a synchronous forecast
   scope; state-preview epochs invalidate them. No actor, action execution or alternate spell is created by the reader.

Supported native fixtures include Fireball + Lightning Bolt delivered as a burst, Fireball + Slow, Slow + Confusion, and
Shocking Grasp + Fireball delivered as a burst. Source touch damage does not add a fictitious touch roll to that burst.
Repeated source payloads remain two applications, while save probability is shared.

Known unpriced structures are explicitly withheld from **AI casting** with a named `[Fusion]` diagnostic: line footprints,
extra/secondary delivery, clicked-target secondary actions, spawned areas/creatures, shared-HD selections, independent
nested saves, healing/caster-side effects, or buffs/kills outside the supported failed-save branch. This includes some
useful combinations (e.g. extra Disintegrate/Enervation buff branches); support is not claimed for the whole spell list.
Ordinary spells retain their existing readers. Next-spell boost spending is not bought speculatively for a fused cast;
the native cast itself, its existing buffs and its resource/payment rules remain native.

## Verification and focused gameplay handover

Release compilation uses the supplied real game/Unity/UMM references. Automated tests, strict compatible-scope ProfileAudit,
native provider regressions, member-reference checks, source and ZIP identity are recorded in the final receipt. The full
original export still has its previously named enum failure; compatible-scope success is not a full-corpus pass.
The offline harness neutralizes profiling/Unity instrumentation in private copies. Metadata and command-flag fixtures do
not initialize actors or prove successful casts, rod behavior, navigation or matched Unity decision time.

Play the checked ZIP in both TB and RT and retain its startup MVID with the logs:

- **Ordinary ranged:** archer/crossbow user, Pure Mage off; normal attacks/full attacks. Check old settings and a non-caster.
  Toggle Mixed/Melee/Ranged with the retained Pure Mage flag and verify it is active only under Ranged.
- **Pure Mage:** staff and crossbow equipped in separate fights; use cantrips/spells, never ordinary attacks. Repeat adjacent
  to an enemy, silenced/out of paid slots, with a weak useful cantrip, and with no permitted cast. Existing safe escapes
  should still compete. An active supported energy buff permits one shot; after it ends, ordinary shots are withheld.
- **Touch/Reach:** ordinary offensive touch blocked; genuinely active Reach allowed at native range and using the ray roll.
  Compare inactive/active/exhausted rods. Self/support touches in place work; a critical touch-heal walk is refused through
  danger and can proceed to a safe recipient. Recheck a moving recipient and a ranged healing alternative.
- **Modes:** compare Standard/Conservative/Aggressive on the same caster, including a resistant/immune target and an almost
  dead enemy. Aggressive removes daily-slot thrift, while bombs/items/toggle costs and friendly-fire refusal remain.
- **Magic Deceiver:** create the supported combinations in the native UI; vary the selected save and burst/single delivery,
  edit a slot between decisions, and verify `[Fusion]` payload/delivery diagnostics, selected targets, both actual effects
  and one native slot payment. Exercise an unsupported line/secondary/nested-save combination: clear diagnostic, manual use
  still available. Record performance in the same party/encounter as the comparison build; no live speedup is claimed here.

Claude reviews/integrates this task branch into development. Manifest remains 0.3.30; the archive is an unplayed test build,
not a release. C07 and broader movement proposals remain separate.
