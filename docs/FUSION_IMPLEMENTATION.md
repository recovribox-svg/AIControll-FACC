# Magic Fusion implementation

## F0: configuration coverage and rejection attribution

Task `codex/fusion-coverage-audit`, based on development `6cf42818ade856814003ac0cab8872795d884ffd`.
Reconciled with integrated development `e1ae334` (S1 summon inventory and S2 approaches); those changes are retained.
The initial fetch only updated the configured main tracking ref; the development ref is now explicitly fetched.
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
