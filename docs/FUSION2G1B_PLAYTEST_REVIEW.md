# Fusion2g1b playtest review (15 November 2026, Claude)

The author played the fusion2g1b package and supplied one AI Control `Log.txt` and the matching `combatLog.txt`. Raw
logs are private and are not committed. This page records what they show, one defect fixed, one reader gap left open,
and the next replay. The same task also implements G2 at the author's request: [GRABBER_G2.md](GRABBER_G2.md).
Receipt: [G2_GRABBER_CHECKS.json](competence/G2_GRABBER_CHECKS.json); `docs/TACTICAL_AI.md` §172.

## Inputs

| File | SHA-256 | Lines |
|---|---|---|
| `Log.txt` (AI Control) | `fb2033f84c4bc55abeae54fb3e31c3deb42055a2f3d6f9f70578343930d9d363` | 6,029 |
| `combatLog.txt` | `4aa2fc9652021ab58ae9ea93e74aca84e2f23c45341cd9b5ba682f05415454b9` | 33,913 |

The startup line names module `e4f231af-1782-4e64-9697-3a3b2455e1a2`: the fusion2g1b package (checked source `981299e`).

## Run summary

- 9 fights; 646 commands issued, 646 with a terminal row (217 turn-based, 429 real time).
- 185 ended `Interrupt`, no repeated pattern: 92 are weapon attacks that had already acted. 3 ended `Fail`: two failed
  concentration checks (arcan's Sleep and Explosive Runes/Hideous Laughter) and one Snowball by the Drow Magus.
- No AI Control exception or error.
  - 154 WrathPatches `OwnerBlueprint` errors (14 in fusion2g1): 151 are emitted during ToyBox's blueprint collation at
    startup, before any fight; three fire when Isekai's channel-energy blueprints first load.
  - Five ToyBox warnings. All are foreign.
- 69 slow-decision rows (median 327 ms, against 31 rows in fusion2g1), with much more turn-based play this time.
  - The four rows over 1.2 s all ran a garbage collection mid-decision (the heap fell by 276-340 MB).
  - arcan's decisions take 250-500 ms over 44 scored abilities. Each Runes option costs 18-49 ms (18-33 ms in
    fusion2g1); Incendiary Runes/Color Spray creates about 380-550 rule contexts, as it did there (354). No
    performance change is warranted.

## The Runes fix works

Six of the seven Runes fusions now reach the joint outcome model (`[FusionValue]` rows, lines 513-527). Explosive
Runes/Excruciating Deformation falls back for another, documented reason (a support, fact-grant or poison carrier).

## Defect fixed: grapple-immune targets were priced as grappled

- **What happened.** Two `[Grapple]` rows priced a hold on a Vescavor Swarm: +10.6 (line 2731) and +10.0 (line 2890),
  with escape 0%. G1 also cut Wenduag's forecast full attacks against swarms at the expected grab.
- **What it cost.** At line 3066 she left the swarm's gibber area (value 73.4). At line 3094 she walked back in to claw
  the swarm: net value 4.5, after a priced movement cost of 68.5. Without the false hold the net would be about −6.
- **What the game did.** The combat log shows both of her claw hits on the swarm (lines 15456 and 15674) with no
  grapple after them.
- **Cause.**
  - `RuleCombatManeuver.OnTrigger` cancels a maneuver "because of immunity" on the `ImmuneToCombatManeuvers` condition,
    or on `AutoFailure`.
  - Every turned-on `ManeuverImmunity` component of that maneuver type (or of type None) sets `AutoFailure`.
  - `SwarmTinyFeature` and `SwarmDiminutiveFeature` carry ten such components (Grapple, Trip, Bull Rush and others) and
    no condition. Freedom of Movement and its kin carry the Grapple one: 14 facts in the exports.
  - The mod read only the condition, in all three of its maneuver forecasts.
- **Fix.** `CombatMath.ImmuneToManeuver` reads the condition and every turned-on `ManeuverImmunity` by its type, once a
  frame per unit. It is used by the G1 grapple opening, the shared `ManeuverOdds` (trip, bull rush and the other ability
  maneuvers) and the trip-on-hit forecast.

## G1 behaviour in this log

- **The decisions.** 152 `[Grapple]` rows. Hold values are positive for dangerous targets in every row (Ferocious
  Schir 18 of 18, Minotaur 6 of 6, Manticore 5 of 5) and negative for Alpha Wolf (5 of 5) and Earthly Totem Guardian
  (8 of 8).
- **The holds.** The combat log has 21 successful grapple maneuvers by Wenduag, none against a summon. Three escape
  maneuvers against her failed (two by a Scythe Tree, one by a Fire Elemental).
- **While holding.** One decision with a hold in its header (line 2425, turn-based): END TURN.

## Reader gap left open: `ContextActionSelectByValue`

- **The gap.** Frigid Touch staggers through `ContextActionSelectByValue`: one minute on a critical, otherwise one
  round. The profile walker does not enter its `m_Variants`, so the stagger is not valued, for an ordinary cast or a
  fused one (Explosive Runes/Frigid Touch showed control 0.00, line 522).
- **The scale.** Only three blueprints in the exports use it: Frigid Touch, Elemental Assessor and one area effect.
  Frigid Touch was a candidate once in this log and was not chosen.
- **Why it is left open.** A faithful reading needs the critical chance to weigh the two variants. That is noted here
  rather than guessed.

## Not changed

- **Seelah R4 (line 1953).** She was in a Babau's reach but could not reach it, with no free spot in reach and only
  negative options, so she ended her turn. Changing that is a movement-policy change, for the author to decide.

## Next replay (g2 package)

- The G2 replay in [GRABBER_G2.md](GRABBER_G2.md).
- A swarm in melee: no `[Grapple]` row for it, and no walk back into its area for a claw.
- The fusion2 and G1 checks as before.
