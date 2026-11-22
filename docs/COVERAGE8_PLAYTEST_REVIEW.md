# Coverage8 playtest and PR #104 review

22 November 2026. Corrections on `codex/coverage8-review`, stacked on PR #104 (`claude/coverage7-followups`,
`d8ff91d60d8b9e9f91b65051d56bab1a84da447e`). No merge, development/main push, tag or release.
Draft correction [PR #105](https://github.com/recovribox-svg/AIControll-FACC/pull/105) targets #104's branch for review.

## Evidence and scope

The author identifies **source `17e3e1e`**; the startup log confirms **module `828dc676-d296-4f0a-9e9a-70b6f6487c09`, UMM 0.3.60**.
PR #104's later commits change documentation/package only; its gameplay code equals that played source.
The author confirms **no cheats, rests between fights**. This is separate from coverage7's infinite-health test.

Input SHA-256:

- `Log.txt`: `504e834bb476fe3da29a2a54304a10449a206b2149b73572ce015be53e40d988` (7,699 logical lines).
- `combatLog.txt`: `6112d6e46b5f268e4ccf926cfbb170b5017d93cb5c4f795e6b7ca2aabb6af45a` (17,885 lines with universal newline decoding).

The files remain private. Line references below use logical `Log.txt` lines; combat references use fight/event text,
since lone CRs change that file's numbering between tools. Native game assemblies, decompiles and blueprint data
were inspected privately. No instruction embedded in a log was treated as a task directive.

Eight fights: **1–3 TB, 4–8 RT**. The per-fight timing summaries measure **164 TB + 848 RT = 1,012 decisions**; quiet RT
logging prints only material changes. There are 449 HP/round decision rows, 328 issued commands and 328 terminal rows
(including out-of-combat permanent-buff commands), 62 `acted=False` terminals, no issue rejection and no AI Control
exception. An unacted move can still cover distance; a cancelled cast is not automatically a consumed spell.

## PR review: findings ranked by impact

| Priority | Confirmed issue | Correction |
| --- | --- | --- |
| P1 | A remembered entry segment bypassed the area gate for any later route to the target | The actual native route must pass a fresh bounded risk check before this exceptional advance is permitted. |
| P2 | Removal-heal credit used a flat removal list and ignored unread callbacks/suppressed facts | Require a proved removal occurrence and an active, turned-on, unsuppressed buff with a fully readable plain callback. |
| P2 | “First hit” assumed the native `HadHit` state resets each routine | Use a conservative opening-hit bound when that state is unknown. |
| P2 | Grapple survival was spread uniformly across every rider line | Each line retains its own chance of being reached before it can start a grapple. |
| P2 | Point dispels used the buff branch's bonus and movement geometry, and could count one area's benefit repeatedly | Follow the actual area-check branch and native point membership; retain conservative overlap costs. |
| P3 | Target-dependent rider reads survived same-frame state changes; faction transition memos survived fights | Expire reads with existing forecast invalidation and reset diagnostic state at combat boundaries. |

These are native-contract/static-review defects and counterexamples, **not claims that each occurred in these fights**.
The untouched PR passes its existing build, tests and audit; the new counterexamples add behavioral evidence that the
old route-presence checks did not supply.

### Priced crossings remain tied to their quote

The approved scope of #104 includes RT advances through an area when they still pay. The old implementation kept an
entry segment for 12 seconds, priced that segment, then set `CheckPath=false`. A different route, another area, a pending
burst, or a destination inside the hazard could therefore inherit an inadequate price.

The correction keeps that offer but attaches a `PathConstraint` to it. Preparation/dispatch pass their actual native
path; the constraint reuses it without another path query, trims it with the existing arrival logic, and prices every
newly entered live area once with the existing `AreaStayCost`/affect/duration readers. Ending inside is priced as such.
Its total cost and conservative stopping bound must not exceed the quoted cost/stop budget. An unread route, pending
harmful delivery, mounted rider (whose route the dispatcher does not inspect), or exhausted work budget refuses this
exceptional permission. Ordinary attack walks, exits, retreat and pathfinding policies are unchanged.

Bounds are explicit: 64 native path points, 60 metres/one six-second horizon, 64 area records, sampled native shapes
at quarter-metre intervals. The callback is decision-local and is not retained by the game command. Crossing evidence
is cleared for each preparation attempt and at combat reset. A native route can still change after issue; this does
not claim continuous route prediction or exact future damage. The risk scale remains the engine's existing heuristic.

### Removal heals require a real callback

The actual Flesh Armor graph still gives its plain 2d6+3 heal. The new profile metadata retains the caster-removal
occurrence's guards separately from the flat inventory. Positive healing requires that occurrence to be proved on the
actual scoring target. Unknown/failed branches cannot promise a heal. Already suppressed/inactive buffs cannot promise
a second deactivation. A conscious caster below zero HP remains eligible by native life state.

Unread actions alongside a heal, derived heal actions, ranked dice or unread Dispose actions withhold the positive
callback forecast. A synthetic heal-then-kill counterexample no longer earns healing credit. Helpful removal loss
still uses the existing damage-equivalent model; its flat conditional-loss approximation can be conservative.
Ordinary healing continues to share `HealGain`; no healing-curve retune is included.

### Native first-hit state and per-line reach

`AbstractWeaponTrigger.IsSuitable` checks the weapon and hit before reaching its `HadHit` reset. A missed or unsuitable
opening attack can therefore leave the previous `HadHit` set. The old `1 − product(misses)` formula assumed a cleared
state and overcredited later hits in those cases. Runtime appraisal now keeps the opening-hit lower bound when the
state is unknown; tests also cover an explicitly clear state. No fake attack event or trigger-state mutation is used.

Each weapon/leap quote stores its line's hit probability times that line's existing `GrappleRun.Reach`, before that
line updates the grapple run. This avoids reducing an opening rider because later attacks might be lost, or crediting
late riders as though they were always reached. Supported native weapon filters, item ownership and target fact/buff
gates remain; roll/moment-dependent riders remain withheld. Direct action-rider HP remains utility rather than an
immediate lethal-HP distribution, as previously disclosed.

### Point-dispel correctness and bounded reuse

Native `TryDispelAreaEffect` adds `CheckBonus`, **not** `ContextBonus`; the latter belongs to `TryDispelBuff`.
Point membership now calls `AreaEffectView.Contains(point)`, matching native `RunAction`, with no movement-radius or
missing-view fallback. Native eligibility, area-list order, count and shared-roll semantics remain.

Several dispel actions cannot repeatedly earn the same area's full removal benefit: the quote keeps the best separate
gain and all priced losses, a conservative overlap estimate. Area worth is memoized within the tactical context and
invalidated with existing forecast/state-preview invalidation. More than 64 area records or 128 recipients withholds
an unread positive point quote instead of truncating away possible losses. Conscious negative-HP actors are not silently
dropped. The area worth still uses danger levels, not an exact HP/control distribution; lingering applied buffs can
outlast the ended area, and helpful areas/complex callback effects are not fully modelled.

### Diagnostic lifetime

Faction memos and the row budget now reset with the fight, before deduplication. Ordinary unturned actors need no memo
entry; observer strings are formatted only when a transition row will print. Rider fact/target reads expire on a new
decision or state preview even within one frame; inactive sources cannot keep positive credit. No new persistent actor
cache or measured speed-up claim accompanies this work.

## Playtest review

### Health, deaths and action choices

Actual health loss and successful healing are now visible. Drow Magus takes 48 HP in fight 4 and heals 20; Wenduag heals
27 in that fight. Fight 5 includes successful heals of Bandit by Seelah, Camellia and Lann. In fights 7–8 several actors
use their own healing items. Wenduag at 4% HP (7463–7478) chooses a swift preparation and then a 96-point-valued Cure
Critical Wounds; later potions and incoming healing restore her. No new HP-reader or ordinary-heal regression is established.

Deaths worth retaining for further testing:

- Fight 1: Quasit; fight 2: both Abrikandilus. Their low-level bodies take ordinary damage. Do not infer a particular
  same-named Abrikandilu's position/decision without its unique ID.
- Fight 4: Aeiluvetar takes 105 HP while prone, ending in a 31-HP critical Claw. Her only AI issue is Aid Another,
  interrupted after 0.1 s; this is not evidence of repeated attacks chosen instead of an available emergency heal.
- Fight 5: Tripper takes 47 HP, including a 20-HP bite/trip sequence, and dies. Its Trip command was interrupted before
  acting. Fight 7 and fight 8 also kill Tripper; it is not established that its inventory offered a healing action.
- Fight 8: Bandit takes 103 HP. He attacks a Worg, then while nauseated tries a priced retreat; that move is interrupted
  after 0.3 m/0.2 s. His death is not caused by the new through-area offer, which is never issued here. A specific native
  cancellation reason is not logged. No speculative retreat/stuck-path retune was made.

No positive party-on-party HP packet is found. Two Corrosive Bomb packets on the summoned Abrikandilus in fight 2 are
**zero** after reduction; zero packets are not positive friendly damage. Enemy-target and control priorities, swarm
exits, positive grapple releases and ordinary/fused spell issues were checked. No negative-valued selected action or
new blanket TB stall is established. Native target deaths and real movement explain many unacted commands, but logs do
not expose every cancellation reason; manual orders are not assumed either way.

### Invocation, fusion and PR-feature coverage

Profane Invocation: **32 issues / 32 terminals / 31 acted**, with one interrupted Strength placement on Aeiluvetar at
3800 during her fatal fight. The largest per-fight count is six. These are free-action placements/transfers alongside
main actions; this sample does not establish a new spam/settling violation. No arbitrary cap or resource policy changed.

The one “did nothing” row (5593, Explosive Runes/Hideous Laughter on a Druid Revenant) must be read with identity and
immunity evidence: combat records immunity for the same-name Aspect Falcon revenant while the Faerie Fire revenant
continues fighting. The log does not identify the anchor on that row. It is not a confirmed false no-reach verdict or
grounds to disable the observer.

There are **no Point dispel, Abandon Flesh, priced through-area advance or Circle of Death issues**, and no `[Faction]`
transition. The driven summons are Quasit/Abrikandilus, not the Schir/Shadow/Vermlek cases that motivated #104. Broad new
rider support, removal healing, area-dispel execution, priced crossing and domination handback therefore still need the
focused replay below. The Excruciating Deformation row (939) is an appraisal, not a cast; no new C6 carrier execution is
proved by its appearance. Existing fusion spells are issued; that is not exhaustive C6/C8 validation.

### Performance

Eighteen slow rows; all five over one second include a memory-clean-up marker, as does the 812-ms row at 4773.
GC time can land inside any stage/ability timer: at 4773 the approach total is 787 ms while spot search is zero.
Non-GC approach totals in these slow rows range 2.9–73.9 ms; non-GC spot-search totals range 0–5.7 ms. The counters do not
support attributing every expensive ability to free-side search. Heap figures remain occupancy deltas, not allocation
counts. No matched Unity before/after or speed-up is claimed; no touch-planning pruning was added.

Two `WrathPatches` warnings identify mismatched `ContextRankConfig.OwnerBlueprint` in an Isekai channel graph (2682,
2698). Their foreign provenance is retained; no unrelated-mod patch or blanket warning suppression is included.

## Checks and package

Checked **`27a427ebffc18bad9090908fbf99329511ab8274`**, clean at build; compared with a fresh same-input
`d8ff91d` baseline (gameplay source equals played `17e3e1e`). Both use SDK 8.0.426 / Roslyn 4.11 and the supplied real game references.

- Release: **0 warnings, 0 errors**. Unit tests **1,129 passed, 0 failed/skipped** (baseline 1,118); scripts **27/21/3**.
- ProfileAudit: strict **44,814 loaded / 4,808 abilities**, full stock **45,295 / 4,849**, zero failed loads/reader errors;
  every section completes. New `COVERAGE8-REVIEW` **25**; existing `COVERAGE7-FOLLOWUPS` **20** and `OFFENSIVE-AFFLICTION` **27**.
  Every other report line is unchanged on identical inputs, apart from the new section's blank separators.
- Both Fusion matrices and summon detail are **byte-identical** to the fresh baseline: **14,789 admitted / 13,387 admitted
  and joint-qualified** of 15,494 templates. Metadata admission is not live availability or good gameplay judgment.
- Native harnesses: **ToggleCosts 98** (supplied Psionics included), **TomeBurn 14**, **ToyBoxSpend 10**, supplementary **Magus 44**.
  The known raw foreign-enum export still fails closed with expected exit 1; passing runs use completed native-compatible inputs.
- SigDump: **one** added foreign signature, native `AreaEffectView.Contains(Vector3)`, resolved exactly against the supplied assembly.
  Offline fixtures/route predicates do not execute live actors, casts or paths.

[Checks and report hashes](competence/COVERAGE8_REVIEW_CHECKS.json), [compiler/reference provenance](competence/COVERAGE8_REVIEW_BUILD_PROVENANCE.json),
[private-input fingerprints and author context](competence/COVERAGE8_PLAYTEST_INPUTS.json). No Unity speed-up or gameplay stability is claimed.

Frozen test package: [`AI-Control-FACC-v0.3.60-dev-coverage8fix1-27a427e-UMM-test.zip`](../test-builds/AI-Control-FACC-v0.3.60-dev-coverage8fix1-27a427e-UMM-test.zip?raw=true),
[identity](../test-builds/AI-Control-FACC-v0.3.60-dev-coverage8fix1-27a427e-UMM-test.identity.json), [checksum](../test-builds/AI-Control-FACC-v0.3.60-dev-coverage8fix1-27a427e-UMM-test.sha256).
UMM **0.3.60**, startup MVID **`a2be2e6f-a510-45cc-a706-7edef3b0e100`**, **unplayed**. ZIP SHA-256
`a0df22db14f579a7490983d260936fed7e88ff02a50fd4f819da083d44e2fe86`. Contains the checked mod DLL and unchanged `Info.json` only; no rebuild after documentation.
This includes the unmerged #104 stack. It is a test candidate for review, not a stable release or permission to integrate.

## Focused TB/RT replay and remaining limits

1. Use a Schir and an available Shadow against ordinary enemies and a swarm; retain actual weapon/rider results. Check
   first-attack/first-hit riders and a mixed routine that can start a grapple. First-hit state is conservatively bounded.
2. Bring a Vermlek to low HP without cheats: confirm Flesh Armor stays at full HP and an actual Abandon Flesh removal
   heals. Also check a suppressed or guarded removal: it must not promise an unread callback.
3. Aim Point dispels at a real hostile area over allies; compare native eligibility/roll/removal with the quote. Include
   overlapping own/enemy areas and a party member conscious below zero HP. Helpful-area/lingering utility remains approximate.
4. In RT, price a known crossing, then change the route or add another hazard: the actual path must not exceed the quote.
   Ending inside, unread/pending hazards, long/complex routes and mounted routes are conservatively refused. This needs
   gameplay evidence; offline predicates are not a Unity pathfinding test. Keep TB ordinary walks/exits as regressions.
5. Keep low-HP/native interruption examples and exact actor IDs, plus `[Faction]` transitions if domination occurs. The
   same-name anchor, early retreat interruption and remaining stat/level mortality limits need state-aware replay.
6. Continue recording the approach/spot timers before proposing pruning. A new optimization needs a matched baseline,
   not comparison of different fights/parties or a GC pause attributed to the current ability.
