# 0.3.22-test1 playtest: decision stalls

Follow-up branch: `codex/kineticist-playtest-stutter`, based on development
`d456882` (after Claude integrated PR #2). Implementation: `ab0c9ea716747e9dd8471f39ee33737ab360fbce`.

## Evidence supplied by the author

Logs: `Turn Based mode log v.0.3.22-test1.txt` and `realtime mode log v.0.3.22-test1.txt`.
The author reports consistent visible stalls on Orvath's turns, on a laptop with an Intel Core i5-8365U.
The logs identify the author's test-build label, not its exact binary checksum.

| Measurement from the logs | Turn-based | Real-time |
| --- | --- | --- |
| Orvath kinetic summaries | 19 | 27 |
| Orvath summary decision time: min / median / max | 221.3 / 319.5 / 840.6 ms | 342.6 / 534.8 / 1126.9 ms |
| Slow-decision lines: Orvath / Camellia | 17 / 13 | 27 / 28 |
| Longest decision in fight summaries | 850.0 ms (Orvath) | 1322.1 ms (Camellia) |

Every Orvath summary reports `search 0.0ms/0 aims/0 alternatives`, zero native action queries and zero shape
builds. The measured delay therefore precedes or lies outside modifier-search work; increasing or shrinking its
40 ms budget cannot resolve these stalls. Camellia's slow decisions establish that the affected path is shared.
Many waits/end-of-turn decisions still take hundreds of milliseconds after the main action is spent.
Old logs lack stage timings, so they cannot establish how much time each shared stage consumes.

The RT log also contains a WrathPatches owner-blueprint warning for `ChannelPositiveHarm` /
`IsekaiChannelPositiveEnergyAbility`. This is a separate mod-data warning, not evidence of a kinetic preview
exception. The supplied logs contain no CompanionAI evaluation exception identifying the stalls' cause.

## Changes

- **Prune spent action slots before availability/effect queries.** `AddAbilityCandidates` uses the same
  `ActionRules.HasSlot` check as final affordability. A spent Standard/FullRound/Swift slot no longer incurs
  full spell/recipient/area scoring. Free actions remain eligible; a Standard can still pay for a Move.
  Nauseated movement-only units skip ability scoring. Kinetic hypothetical/recovery scoring remains a direct
  `EvaluateAbility` call under its projected action type, so Quicken alternatives are not eliminated by the current slot.
- **Reuse native parameters within one synchronous decision.** `CombatMath.AbilityParameters` calls the game's
  actual `CalculateParams`, caching its result per ability object identity, main aim/point/orientation and rulebook
  context. Results are cloned for consumers. Active mechanics contexts bypass memoization because their target and
  shared values are mutable. Execution contexts are always fresh. No DC, burn, damage or SR formula is substituted.
- **Reuse discovery and fact-component reads.** Ability/conversion discovery and kinetic SR/DR component searches
  no longer rescan the same unit for each estimate. Discovery callers receive their own list. Modifier entry and
  restoration invalidate all these reads; nested stand-up previews have a separate scope. No cache survives its decision
  or reaches command execution. Native query counts remain available after disposal for diagnostics.
- **Expose remaining cost.** Existing slow-decision `[Perf]` lines now report the three slowest stages, the most
  expensive ability evaluation, native/reused parameter counts, fresh execution-context count, fact scans,
  discovery builds and slot skips. Timings cover all companions in both modes. They do not create a preference order.
- **Accept native calculator subclasses.** The Burning Infusion coverage line shows a modded
  `ContextCalculateAbilityParamsBasedOnClasses`, which the old single-class whitelist rejected. Projection now
  accepts `ContextAbilityParamsCalculator` subclasses and uses native `MechanicsContext` virtual dispatch.
  Other unmodelled components still keep a toggle fixed; existing current-state parity checks remain.
  Coverage now describes per-blast projection/fixed status instead of the superseded burn-blockage-only path.

These changes remove identifiable duplicate/unusable work. Their frame-time improvement on the author's laptop
has **not** been measured. Baseline scoring still has no whole-decision deadline; native calls remain synchronous.
The existing cooperative modifier-search limits are unchanged. No release version bump or main-branch update.

## Verification

- `dotnet test tests/CompanionAI.Tests`: **143 passed**, zero failed/skipped. Slot tests cover spent main actions,
  free/swift availability, full-round eligibility and Standard-as-Move.
- Full mod and ProfileAudit builds against the supplied real game references: **zero warnings/errors**.
- ProfileAudit `--kinetic-checks`: **4,098 loaded, zero failed; 1,220 rows, zero reader errors**.
- Integration checks exercise actual read-cache/candidate entry points: 200 repeated parameter requests make
  one intercepted native-method call; returned clones cannot poison later reads; distinct ability instances,
  absent/point/oriented aims, nested decisions and modifier entry/restoration remain isolated. Mutable ambient
  contexts and out-of-decision queries recompute. The slot gate runs before native availability queries.
- A synthetic calculator subclass passes the projection whitelist and the game's virtual parameter dispatch.
  This checks compatibility through the native base type, not live parity with every third-party calculator.
- Existing optional-hook rollback, inactive fast exits, native action cache, inert burn cost and pre-dice dispel
  checks still pass. The Linux audit uses isolated engine-bound rewrites; none are shipped.

The native counting seams use detached data and patched returns where a live Game singleton would be required.
They establish query reuse/isolation, not gameplay latency. No post-fix in-game result is claimed.

## Replacement package and focused replay

Package: `AIControll-FACC-stutter-ab0c9ea-test.zip` (provided in the task chat).

- ZIP SHA-256: `e341480253ada9ca7d761c13789b34f712b883d898812ba9e4892057ee48f723`
- Mod DLL SHA-256: `ef04a67e9922aaaf77eb7f562fdd0290d50e7cf40773677beb0f930cf66dc91f`
- Contains only the mod DLL, existing Info.json and a test identity note. Info.json remains `0.3.21` per repository
  release rules; identify this experimental build by the commit/checksum, not that version string.

After Claude's review, replay the same encounters with the same party/settings/mods on the same laptop:

1. **TB:** compare Orvath and Camellia's initial decisions and the re-decision after casting/attacking. Check `[Perf]`
   stage times, parameter reuse and slot skips. Main spells must not be scored after the Standard is spent; free
   reservoir/marks and remaining Swift or Move/item actions must still work when useful.
2. **RT:** repeat the larger encounter, including cooldown waits. Compare fight-summary median/95th/slowest times
   and the number/cost of no-action decisions. Keep logging settings the same for the comparison.
3. **Burning Infusion:** verify the coverage note now says per-blast projection when the modded calculator derives
   from the native base and no other unsupported component exists. Expect an eligible comparison, a parity fallback,
   or a budget fallback; no mandatory choice of the infusion. Check burn/DC behavior and restoration.
4. **Regressions:** test a quickened blast with no Standard left but Swift available, a normal caster's heal/buff,
   Standard-as-Move item use, a prone/stand-up decision, and AI toggle management off/player-owned toggles.
5. Return complete logs and the exact package identity. If stutters remain, the new stage/top-ability counters
   identify the next path to fix. Startup, actual third-party calculator parity and frame-time gains need live testing.
