# Test3 replay and v0.3.21 comparison

The author reports a clear improvement in both modes with occasional Orvath/summon stalls remaining. They clarified
that performance is now **tolerable**. This follow-up emphasizes confirmed decision bugs and build identification;
it does not introduce a decision deadline, target pruning, score-weight cuts or animation-speed changes.

## Build identification caveat

All three supplied test3 logs have the earlier diagnostic format: one combined `kinetic` stage, parameter counters
ending in `/0 ambient`, and no `[Pacing]` entries even at combat starts. The `3230ba4` code adds separate kinetic
stages, an `active-rule` counter and pacing observations. The author confirmed downloading the `3230ba4` link.
The repository ZIP was rechecked: its DLL contains the new diagnostics and matches the published checksum
`cbdd80471d1a1dd46857fd5dea26446fa14815e9fa52a5432d972e923bf62c13`.

This establishes a package/log-code mismatch; it does not establish whether UMM retained an older installation,
the game stayed open during replacement, or different logs were copied. These logs cannot validate the empty-root
cache fix's live effect. Do not credit that fix for a measured percentage improvement from this dataset.

The new code logs `[Build]` at mod load and combat start: loaded module MVID, a file SHA-256 cached at load, and
feature markers. Combat-only excerpts can then be checked against `test-builds/build-identity.json`. No personal
installation path is printed. If reading the DLL fails, the loaded module ID/features remain available.

## Performance evidence

Use the complete per-fight `[Perf]` summaries, not counts of slow log lines: the release uses a 250 ms threshold,
whereas the development logs use 100 ms. The following are ranges of **individual fight summaries**, not pooled
percentiles or a controlled benchmark. Different encounters, random reinforcements and decision counts limit
direct comparison; identical hardware/settings were not independently established for these sessions.

| Dataset | Completed fights | Whole-party decision median range | Per-fight 95th percentile range | Worst decision |
| --- | ---: | --- | --- | --- |
| v0.3.21 TB | 3 | 36.7–60.9 ms | 99.3–191.0 ms | 744.4 ms |
| test3 TB session 1 | 2 | 6.2–7.4 ms | 126.6–145.1 ms | 488.0 ms |
| test3 TB session 2 | 2 | 4.0–4.2 ms | 135.4–180.5 ms | 194.1 ms |
| v0.3.21 RT | 4 | 24.9–69.6 ms | 101.5–296.4 ms | 975.4 ms |
| test3 RT | 5 | 4.0–5.6 ms | 123.1–134.4 ms | 555.7 ms |

TB session 2 also has one RT decision at combat start (6.6 ms), excluded from the TB ranges. Typical decisions are
much cheaper in the new sessions; the tail overlaps and still contains stalls. A Camellia decision spends 389.2 ms
on Inflict Light Wounds while a collection occurs; RT outliers include 405.4 ms on Grease, 403.5 ms in context building
and 406.7 ms in the combined kinetic stage. A collection occurring during a decision does not prove the decision
caused it, or that all its elapsed time was collection. No explicit GC or speculative timing fix is added.

Native summon brain timing is absent with the older log format. A summon turn name alone cannot attribute a whole
frame stall to AI Control. The existing native/other-mod diagnostic remains, without changing control ownership.

## Decision findings

**Confirmed: a shared target budget was credited afresh for every recipient.** In TB session 2, fight 2, Sleep is
centered on Forsaken Kobold Evoker on rounds 2 and 3; the outcome observer reports other recipients but no outcome
on the center, eventually excluding the center through its two-miss safety net. The same pattern later occurs
around Vermlek. Similar behavior exists in the release logs and is a documented pre-existing backlog item
(TACTICAL_AI §33/35), not evidence of a kineticist regression.

The native Sleep action graph checks the shared HD allowance, attempts a save/buff and then runs
`ContextActionChangeSharedValue` with `SubHD`. The old reader checked every recipient against the initial allowance.
It could therefore credit control on a later recipient after earlier recipients had consumed the pool, and spend
a spell/DC boost on that inaccurate expectation. The outcome observer's word “reached” also includes resisted spells;
it does not mean every listed creature fell asleep. Text logs alone do not prove exactly which recipient consumed
the pool on each cast, but the source scoring discrepancy is independently confirmed.

The new `SharedAreaPreview` obtains recipients and their order from native `AbilityTargetsAround.Select`. It follows
safe conditionals and invokes only the native `SubHD` shared-value action on a detached context. No buff, damage,
visual, save or resource-spending action is executed. It tracks possible remaining pools weighted by the existing
runtime SR pass chance of earlier recipients. A save still spends the allowance; a pre-action SR rejection does not.
The current recipient's SR/save chance stays in `CombatMath.EffectChance` and is not multiplied twice. The resulting
eligibility share gates/prices the target buff within that scored aim; subsequent/nested aims are isolated.

Support is structural and deliberately narrow: one distinct target buff (including repeated application branches),
one deterministic SubHD pool, recognized action containers, safe conditions and no pool changes inside a save branch.
Mixed effects, random initial pools, unfamiliar actions and excessive state spaces retain the existing estimate.
The state limit abandons the entire projection; it never truncates targets or ranks candidates by a fixed priority.
Thus this is not a complete simulation of arbitrary action graphs or future enemy behavior.

**Other reviewed choices and limits.**

- Both versions choose healing/revival as threat and missing HP rise; the test3 RT log shows Camellia withdrawing
  at 21% HP, healing on her next available standard action, then returning. Those lines alone do not establish an
  erroneous retreat loop. A 5%-HP attack while another character approaches with a heal deserves replay attention,
  but HP alone is insufficient to prove the alternative was safer or affordable.
- Free/swift boosts followed by a standard spell, and quickened spells followed by another cast in one TB turn,
  match the recorded action slots. Prompt transitions are not evidence that the mod changed animation rates.
- Most attack commands ending `Interrupt` also say `acted=True` and have spent the standard action. Treating those
  as unperformed attacks would be incorrect. No broad retry/interrupt change is made.
- Daze, Coup de Grace, defensive-mode changes and healing often have appropriate targets/results in both versions.
  A high-valued area center that is immune can still be valid for other recipients; the shared-pool bug needs the
  recipient/order analysis above, rather than a blanket ban on such centers.
- “Cambion” can also be an enemy name (TB session 2 explicitly reports `not in the party's faction`). It must not be
  assumed to refer to Orvath's allied summon in every line. Control boundaries and actor identity keys are unchanged.
- Fire Blast's unknown `IsEqual` branch warning remains visible. The existing mutually-exclusive-effect handling
  prevents simply adding both damage branches; the warning alone is not evidence of doubled damage.

## Verification and replay

The follow-up correctness audit compares against actual release source `3d280bd`, not a rewritten imitation of its
predicate. A distinctly named release DLL and the development DLL are loaded with the same staged game types.
All **160** combinations of action slots, action availability flags and movement-only status agree on non-gather
ability affordability; early pruning reaches native availability exactly when that release predicate allows it.
Another **2,560** final-affordability cases agree for ability/weapon actions, walking, independent full-attack flags,
movement-only status, inability to walk and one-action restrictions. No Gather Power projection is included in that
equivalence claim: Gather Power deliberately has new native plan/action handling, tested separately.
This covers legal entry to scoring, not equal scores on fully initialized actors. Deferred RT positioning and TB
Gather Power preparation are retained and checked separately. Weapon full attacks retain `CanFullAttack` rather
than the generic ability's `HasFullRound` rule; the action enum's numeric values are unchanged.

Both versions' blueprint readers were built and run against the same inputs: the same 1,220 row identities, zero
reader errors and the same one failed export. Exactly 42 classifications differ: 29 per-blast configurations, one
Gather Power preparation, three penalties now recognized, and nine former guessed buff values removed. The other
1,178 rows match including details. [Every difference is listed](TEST3_READER_DIFF.md). The guessed-to-zero changes
are deliberate coverage limits from the kineticist implementation, not performance optimizations or proof of
equivalent decision quality. Quiver retains its dedicated recovery path. Retaliation, Shroud of Water and some
other defensive wild talents remain incompletely appraised; restoring invented generic scores would hide that gap.

**A new regression was found and fixed:** `PartySpellShare` used `HasFlag` for a combined descriptor mask. The native
`SavingThrowBonusAgainstDescriptor` handler accepts any intersection, so a penalty against cold OR electricity must
count a cold spell alone. The helper now uses the native descriptor-wrapper intersection operator. Checks cover
partial overlap, combined spell tags, disjoint tags and an empty mask. This was a source-confirmed scoring error
introduced in the kineticist work; it was not proven to cause one particular logged choice.

The audit also checked each new early return against what it suppresses, cache entry/disposal invalidation, cloned
parameters, shared-value restoration, player-toggle locks, optional-hook failure/rollback and command dispatch.
All seven native parameter fields survive repeated cloned reads; consumer mutations do not alter later readers.
Those cache fixtures control calculator outputs; they do not establish that every installed mod's handler is pure.
Preview setup errors now stay inside the optional shared-area fallback, so a failure constructing that preview
cannot suppress ordinary scoring of the ability. Build-identity logging failures cannot fail mod initialization.
The existing game-independent value formulas were extended for kinetic effects; their pre-existing formulas were
not removed or replaced. Native action-cost projection still refuses patched `SpendAction` implementations.

The ordinary suite has 143 passing tests. The full mod builds against supplied real game references with zero
warnings/errors. New offline checks exercise the supplied Sleep graph, native HD predicate and SubHD action, ordered
recipient forecasts, no-spend when a recipient is rejected, resistance branches and context restoration. Actor
lookup/fact states and recipient sequences are controlled fixtures; these are not a live Unity selector/actor test.
Existing decision-read, condition-context and kinetic checks remain required. The known blueprint-loading exception
still requires an explicitly partial audit: 4,097 loaded, one failed; 1,220 rows, zero reader errors. This is not
a claim of complete blueprint coverage. The optional release comparison is enabled by building the release with
`-p:AssemblyName=CompanionAI_ReleaseBaseline` and setting `PA_RELEASE_BASELINE_DLL` to that DLL for the current audit.
Neither a successful build nor these fixtures prove complete Unity/Mono actor or installed-mod parity. In-game
replay is still required before merging/releasing; low medians alone are never a correctness test.

For the next replay:

1. Completely exit the game before updating through UMM. Test packages share manifest version 0.3.21; if UMM says
   already installed, ensure the DLL is actually replaced. Preserve mod settings. If necessary, with the game closed,
   overwrite only `Info.json` and `CompanionAI_Pathfinder.dll` in the existing mod folder from the ZIP; leave its
   settings files in place. The normal folder is `<game>/Mods/CompanionAI_Pathfinder`.
   Merely downloading a differently named ZIP is insufficient. Check the new `[Build]` line against the package DLL
   hash and look for `[Pacing]` plus `active-rule` on any slow-decision line before comparing performance.
2. Replay Sleep against a cluster with a limited shared allowance, including a high-HD intended target and earlier
   eligible recipients. Compare cast centers, DC-boost use and actual saves/buffs. Resistance uncertainty means a
   forecast is an expectation, not a promise that the intended recipient will be affected.
3. Repeat TB and RT checks for healing, Daze, Color Spray, ordinary/infused blasts, action cooldowns, native summons
   and toggle restoration. Record the exact ZIP and keep complete `[Build]`, `[Tactics]`, `[Cast]`, `[Perf]`, `[Pacing]`
   and `[Kinetic]` lines. If the new build markers are absent, resolve the installed/logged DLL mismatch first.

This follow-up extends PR #5 while it is still open. Claude should review its latest head, not only the original
two commits. Development integration, live acceptance and any release are separate; `main` remains unchanged.
