# Magic Fusion implementation

The next slice is the [fusion1 playtest review and Fusion2 qualified valuation core](FUSION2_PLAYTEST_REVIEW.md)
(§169). It gives each supported control application its own eligibility/duration, removes fake Burning control
and keeps shared-save damage outcomes through nonlinear value. Full planned support/state/refresh work remains
staged; G1 grapple release is separate. [Checks](competence/FUSION2_CHECKS.json) and
[compiler/reference provenance](competence/FUSION2_BUILD_PROVENANCE.json) identify source `6638404` and its
unplayed fusion2 test package. The declared-list admission count stays 7,474; 3,230 pass the new metadata gate.

F0/F1 were reviewed and merged into development; see §167 and
[Claude's integration receipt](competence/INTEGRATION_60_62_CHECKS.json). The original receipts omitted compiler
versions. The dated [provenance supplement](competence/FUSION_BUILD_PROVENANCE.json) records SDK 8.0.426,
Roslyn 4.11.0-3.25569.22 and successful local byte-for-byte replays using the original Source Link revisions.
Historical receipts and the validated fusion1 package remain intact; this follow-up changes documentation only.

## F0: configuration coverage and rejection attribution

Task `codex/fusion-coverage-audit`, based on development `6cf42818ade856814003ac0cab8872795d884ffd`.
Reconciled with integrated development `e1ae334` (S1 summon inventory and S2 approaches); those changes are retained.
The initial fetch only updated the configured main tracking ref; the development ref is now explicitly fetched.
Concurrent Invocation tooling PR #61 is independent and reserves §164; Fusion uses §165 (F0) / §166 (F1).
Shared handover/tool index additions must both be retained during review; no Invocation runtime change is included.
The author authorized implementation of the [revised plan](MAGIC_FUSION_PLAN.md). No open PRs at task start.

The offline audit now enumerates **15,494 declared-list configuration templates** from 60 levelled sources and
3,540 ordered distinct source pairs, using native `SpellbookMagicHackUtility` school/save/delivery selection.
It runs the actual Fusion profile reader and records all its known rejection reasons with graph locations.
The existing last-reason admission result, decisions and normal logs are unchanged.

One change from the tooling-only F0 sketch is necessary: the reader must retain its rejection findings during
cold profile construction because its previous single string discarded earlier findings. This adds cached
metadata to the gameplay DLL, with no new rule/target/path query or actor/context retention. It does **not**
enable an excluded fusion. A separately reviewed F1 repairs selected cone delivery and the hard-veto re-aim handoff.

Results ([summary and source identifiers](competence/FUSION_COVERAGE_BASELINE.json)):

- 7,474 profiles admitted, 8,020 withheld, zero reader exceptions. Profile admission is not useful or successful casting.
- 1,180 admitted cone profiles have an unvalidated aim and are exposed to the confirmed holder/delivery mismatch.
- 2,706 templates have only the buff-outside-failed-save blocker; 1,304 only shared-HD; 746 only line geometry;
  322 only secondary-spell payload; 260 only clicked-target secondary payload; 230 only healing/caster-side effects.
- Reasons co-occur; “present” counts must not be added as disjoint configuration counts.

The denominator is the supplied declared spell list, not every installed mod's generated list or any character's
known spells. Actor availability, legality of a live aim, policy, value and execution are **not assessed**.
Normal UI source selection excludes cantrips and identical/already-fused sources; runtime availability and
book-specific levels remain separate. The tool does not instantiate a character, execute a cast or write a save.

Run the audit against complete local graphs with:

```sh
tools/ProfileAudit/run.sh <Managed> <mod.dll> <private-harness> <audit.txt> \
  --fusion-coverage <fusion.json> <complete-blueprint-roots...>
```

Missing required list/book/slot/source graphs, ambiguous source levels or reader exceptions fail the requested
coverage check. An incomplete load replaces stale JSON with an explicit INCOMPLETE record; an allowed partial
load remains PARTIAL. Full case rows stay generated artifacts; no game graphs or assemblies are committed.

Integration with S1 exposed a metadata-only audit dependency: asking for a nested UI enum array's namespace can
resolve UnityEngine.UI before the walk discards the array. Scalar arrays now exit before that query, retaining the
same graph traversal and classifications. No game/UI dependency is added. The Fusion validation scope retains the
explicit missing-reference counts in the summon inventory; it does not substitute for S1's broader stock supplement.

Validation and exact source/DLL identity are recorded in [F0 checks](competence/FUSION_F0_CHECKS.json).
This slice is not a test release. Stable v0.3.52 and its ZIP remain unchanged. No new in-game acceptance is claimed.

Next: F1 selected cone delivery with existing native geometry and bounded turned aims; then F2/F3 valuation.
Additional areas share the native clicked target; do not add a speculative second aim or action.

## F1: selected cone delivery and friendly-fire re-aim

Task `codex/fusion-cone-delivery` builds on F0 / PR #60 and includes development `e1ae334` (S1/S2).
Review F0 first; both PRs target development. This is a narrow geometry/handoff repair, not F2/F3 valuation.

The native cast owns a slot blueprint and `MagicHackData` selects its delivery blueprint. The existing profile
reader already uses the latter, but `ProjectileCone.TryCreate` looked for the projectile component on the empty
slot. It now resolves `AbilityData.GetDeliverBlueprint()` and uses that exact cone component through the existing
native range, recipient, obstacle, body and Selective queries. The actual cast/context remains the slot cast; no
source is independently cast, no extra touch roll is invented, and no actor/context is kept in the component cache.
Ordinary cone resolution is identical. A changed delivery is read on each call, so its previous cone is not reused.

A second confirmed handoff gap affected all cones: hard friendly-fire refusals returned before filling the penalty
note that admitted the existing turned-aim search. A per-score boolean now carries that veto to the same search.
It resets on every score and adds no formatting or query. The existing six orientations, current casting position,
target retention, native legality/recipient checks and full rescoring remain. The best existing alternative is
still chosen by fewer allies / then enemy count, not the later F2/F3 useful-payload model. Moving allies remain in
the final scorer's safety check; a rejected alternative does not authorize an unsafe cast.

All current Fusion payload exclusions remain. In particular, selecting a cone does not make shared-HD Color Spray,
nested saves or additional/clicked payloads supported. Policy, native costs, blocklists, final dispatcher checks,
movement and S2's approach work retain their behavior. The added boolean can cause an ordinary cone with a hard
friendly-fire refusal to find a previously missed safe aim; this shared fix is intentional.

### Verification and gameplay checkpoint

`FUSION-CONE` uses the real empty slot and seven real cone-source graphs, ordinary/fused/edited deliveries, the
compiled scorer/query handoffs and the existing six-turn bound. A warm metadata resolver microcheck measures its
allocations. The full F0 matrix additionally verifies the selected component of every admitted cone template.
These are metadata/compiled-contract checks: they do not execute native recipient geometry in Unity, commands or
combat. See the [F1 receipt](competence/FUSION_F1_CHECKS.json) for the exact checked source, DLL and results. No Unity speedup or gameplay success is
claimed from the microcheck. The cumulative test ZIP has a distinct startup MVID and identity/checksum; it is not
a new stable release and does not replace v0.3.52.

Play both turn-based and real time with a Magical Deceiver whose created fusion has a supported cone delivery
(for example Burning Hands + Snowball, if available in that character's book). Keep the fused spell unblocked,
slots available and spell policy permitting it; alternatives still compete on existing utility.

1. Compare straight fused and ordinary cone casts with enemies in range. Check the chosen fused identity, recipients,
   native cast outcome and paired combat damage/control evidence; admission alone is insufficient.
2. Put an ally near an edge, then where a turned aim could spare it. With Avoid Friendly Fire enabled, verify a safe
   turned cone or refusal. With no safe turned aim, no harmful cast should be issued. Repeat with the setting off.
3. Repeat with a downed ally, an ally walking into the cone (real time), a wall and Selective if available. A native
   geometry/query failure must omit the aim rather than assume safety.
4. Change the created spell's selected delivery between fights, including a non-cone source. Its footprint must
   follow the new choice. Check ordinary cones, ranged weapons and a cone requiring approach as regressions.
5. Include a named refused complex Fusion configuration and confirm it stays withheld. Record settings, source
   pair/delivery/save, startup MVID, AI log and the matching combat log. Compare warm decision timing separately
   from cold profile creation; no full-performance acceptance exists yet.

Next remains F2 effect-level useful control and F3 correlated outcome utility; F1 alone does not fix those judgements.
