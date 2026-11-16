# gaps2 playtest review: release execution and Invocation evidence

16 November 2026 (Europe/London). Task branch `codex/gaps2-playtest-review`, based on development `96dabaa` (#72
stock-data handover) and retaining #71 `3664663` -> #70 -> #69 -> #68 -> #67 -> #66 -> #65. Only the task branch is
published for Claude's review; no gameplay integration, main push, tag or release is authorized by this playtest.
Manifest stays 0.3.52.

## Inputs and limits

The AI log identifies **gaps2**, source `96108ba`, startup MVID **`9ce04a64-f9a0-43ce-8275-5f1b0c7bbe73`**. The
author confirms ToyBox spawns, rests **between** encounters, Grabber left to the engine, and explicitly requests
Profane Invocation assessment. [Input identities](competence/GAPS2_PLAYTEST_INPUTS.json) retain the hashes; raw logs
remain private. Row references count **LF**, including in `combatLog.txt`, which also contains bare CRs. This pair
is distinct from gaps1 and supplies no controlled before/after performance benchmark.

Ten encounters include **240 TB / 1,278 RT measured decisions**. All **572 issues** (145 TB, 427 RT) have the matching
actor, command type and label at their terminal: 433 Success/acted, 83 Interrupt/acted and 56 Interrupt/not-acted;
no Fail, unmatched or superseded command. An acted interrupt can be the intended grapple cutting an attack short.
There is **no recorded AI Control exception**. OwnerBlueprint errors at AI 1569/4786 retain WrathPatches provenance;
the profile branch-condition messages are recognition limits rather than additional runtime exceptions.

## Release Grapple: the gaps2 repair is exercised

Five Release Grapple commands are actually issued and end **Success/acted**, all in RT:

| Appraisal | Command issue/end | Victim | Release worth |
| --- | --- | --- | --- |
| 2959 | 2961-2962 | Marauder | 26.3 |
| 3521 | 3523-3524 | Large Earth Elemental | 1.1 |
| 3604 | 3606-3607 | Brimorak | 26.1 |
| 4965 | 4967-4968 | Skeletal Champion | 23.3 |
| 5057 | 5059-5060 | Babau | 27.1 |

The decision rows now show `cantmove`, `cantusestandardactions` and the reciprocal holder. The following rows show
ordinary permissions/actions again, subject to spent actions/cooldowns: e.g. after Earth Elemental release, 3531-3532
selects a heal with movement available but the standard action still spent. A free release does not refund it.
No null-reason evaluation exception occurs. This is RT acceptance of the observed command path, **not TB release
acceptance**, an optimal holder-alternative model or a claim that every state is covered.

Marauder initially has keep value +67.9 (2928). An independent Stun Bomb fails its save (2936), then the hold's victim
worth drops from 58.0 to 4.2 and release is offered. Brimorak similarly fails the independent stun at 3596 before
release. Thus these low prices are not by themselves evidence that grappling priced away its own benefit. A later
Marauder hold is valued positively at 3013. The native combat sequence and changing stun state need to be considered
before calling that re-grab pointless. Holder generic opportunity cost, immobility safety and monster-held allies
retain their prior documented boundaries.

## Profane Invocation decisions

The relevant native/exported rules remain those in [I1](INVOCATION_LEDGER.md): twelve permanent, free-action,
resource-free variants; +2 Profane to one stat; one holder per caster and buff; another stat in the **same tier** is
removed from that recipient; the two tiers of the same stat do not stack. A low-ranked free cast can precede the
main attack, rather than replace it. The ledger reads the engine's estimated utility, not measured realized benefit.
The [derived ledger](competence/GAPS2_INVOCATION_LEDGER.json) records identity/value sources on each row. Issue
rounds can differ from actual placement rounds when a command waits; absence of a combat-log Invocation entry
does not independently verify a stat bonus's realized effect.

There are **32 issues / 32 Success/acted casts** across ten fights, including ten different initial placements in
fight 1. The corrected ledger identifies **27 by current direct ability-ID appraisal**, **4 by explicitly labelled
holder inference**, with **1 tier unresolved**. Classified rows: 13 first placements, 14 settled moves, 2 overrides,
2 holder-unseen and 1 unread. No identified move is under settling; no known same-stat stacking flag remains.
There are no Lann slow-decision rows in this pair. Quantity alone does not establish spam or a steady invocation
performance bottleneck.

The four inferred rows are 3889 (Dexterity 2), 4699 (Dexterity 2), 5253 (Dexterity 1) and 5346 (Constitution 1).
Each has a current chosen value and a single source-holder witness naming its ability GUID. The emitted table marks
that evidence source and leaves gross/slot unknown. Their missing transfers previously left stale holdings: row
4844 was incorrectly flagged as stacking both Dexterity tiers on Bandit Alchemist, although tier 2 had moved to
Tripper at 4699. The detailed-appraisal match must not be keyed only by a display label across fights/casters.

At 4387, the old tool silently recycled the initial Strength 2 appraisal's **7.1**. The actual chosen row 4386 says
**8.8**; its current tier/gross are not printed. The revised tool retains 8.8, refuses the stale ID/gross, and keeps
the tier unread. The later Strength 2 holder note at 4692 corroborates that likely tier but does not retrospectively
turn this cast into a direct ID witness. Do not call its exact tier or holder loss established.

Specific judgment checks:

- **Dexterity return, fight 6:** Bandit -> Drow Magus at 3428 gains 1.5 after losing 3.1. Returning it at 3594 gains
  7.5 after losing 0.3 on Drow; this clears the 5-point early-move override. It is not a sub-margin settling bypass.
- **Constitution chain, fight 10:** Drow Magus -> Tripper at 5346 gains 1.4 (tier inferred). Tripper -> arcan at 5525
  gains 5.3 after losing 1.8, one round later. Recording the first cast correctly makes the second an **override**,
  rather than the old tool's false "settled" classification. The change in value is visible; predicting arcan's
  later danger from the first decision is not established.
- **Apparent cross-tier shortcut, fight 9:** 4699 moves Dexterity 2 from Bandit Alchemist to Tripper for net 2.8,
  holder loss 0.5. Then 4844 moves Dexterity 1 from Bandit to Alchemist for net 6.3, gross 16.8, loss 10.5. The same
  final Dexterity coverage could use tier 1 straight onto Tripper, but that does not show the earlier cast was waste.
  At the first decision Bandit's held Dexterity is worth **7.3** (4688). Tripper's printed net/loss imply roughly
  3.3 before that loss, subject to rounding/other delivery terms. That shortcut does not clear the engine's existing
  1.25x move margin then; the actual tier-2 move does. By the second decision the alchemist's forecast incoming damage
  is 34.2/round and there are 11 enemies instead of 10. Equal eventual coverage is not an earlier-value
  counterfactual, especially with combat spawning. The tool now labels it a **coverage alternative requiring review**.
- **Sibling replacement:** 2063 replaces Wenduag's tier-1 Wisdom worth 0.1 with Strength, net 3.0; 5253 replaces
  Bandit's tier-1 Wisdom worth 0.1 while moving Dexterity, chosen net 5.9. Their own lost buff is not ignored.
- **Holder-unseen:** the engine's ally list excludes unconscious/dead/out-of-combat actors. The two remaining
  identified cases must retain that inference limit; absence of a priced holder does not prove a specific death.

These observations do not justify a new cast cap, joint assignment/foreknowledge model or arbitrary transfer
penalty. The identified defects are **evidence capture and interpretation**. Runtime selection/weights, resource,
settling and recipient policies are preserved.

## Corrections and bounds

The session-wide `buffwhy` key included unit, round and ability/recipient but omitted **fight**. Repeating the same
round/option in a later encounter suppressed its normal `[Buff]` ID/gross explanation; the restraint diagnostic had
the same scope error. Both now use a shared fight-scoped scalar key, preserving once-per-round/option suppression.
Message/appraisal formatting uses the existing lazy report overload. No new query, forecast, target/path scan,
actor cache, choice or movement policy is introduced. No Unity speedup is claimed.

The offline ledger scopes holders/ages/sequences by caster and detailed rows by fight/decision. If a detailed row
is absent, it uses only the **chosen** decision's net and one unambiguous logged source holder to infer a known tier.
It never guesses from a top-five option, ambiguous holders, an unknown explicit GUID or a later observation. Unknown
acted transfers make older state for their stat uncertain. Identity/value sources are emitted. Display-name aliases
and missing/manual/runtime events remain observational limits. Cross-tier final coverage alone is not a waste verdict.

## Other findings and remaining gaps

- Two RT Griffon Shape casts (2824, 4809) and subsequent Fury choices are observed. Material form appraisals follow
  current weapon/target/horizon changes. No TB shape cast is recorded; extra form facts/KeepSlots remain uncredited.
- A Grabber-valued leap is offered at 2915 (39.7); **no actual leap is issued**. Most refusals cite native target,
  occupied landing or hazard; the 4-quote/8-landing limits remain. This does not justify overriding safety.
- Ordinary and no-save Runes/Frigid still have the one-round forecast at 521/534/535; **neither is issued**. Critical
  extension and saved unconditional-control templates retain #69's boundary (168 withheld). Other Fusion casts
  execute through the existing joint model; that is not acceptance of every combination or control outcome.
- One rat-swarm exit is issued at 5380. The three "arrived by the game's own test" cancellations are existing
  safeguards, not themselves proof that a new movement rule is needed. No new movement approval is inferred.
- There are **62 slow rows**, median **349 ms of that subset**, maximum 1,730 ms, nine with collections. Warm Orvath
  rows 4797/5436 still spend about 450 ms in ability scoring. Row 156 is not a clean warm baseline: it builds two
  profiles and assesses enemies. Signed stage heap deltas are not allocation totals. Bound/reuse exact repeated
  per-decision inputs only after proving equality; do not retain MechanicsContext/actors across turns or shrink
  target/aim coverage as a guessed optimization. No Lann slow row supports invocation-specific cost as the priority.
- No named burn-consequences-unknown warning, issued Faerie Fire or reveal witness is present. Those cases remain
  untested, rather than accepted by silence. High-risk bombs and holder best-alternative/safety still need their
  controlled native/per-recipient evidence; no new model is invented here.

## Verification and replay

Source **`9a02565`**, MVID **`48ece05d-5895-46e3-896c-d87cb5a60b29`** passes **866 unit + 27 inventory + 3 audit-script + 18 ledger tests**,
real-reference Release **0 warnings/errors**, compatible ProfileAudit 44,639 loaded / 4,807
abilities with no load/reader failure or prerequisite skip. **DIAGNOSTIC-LOG 16** adds two native checks; inherited
checks, supplementary Magus 44 and native providers 98/14/10 pass. **No new external signature** is added. Reports
match #71 outside the diagnostic check count/output/optional coverage paths. The raw archive's known enum mismatch
remains a separate expected negative-input check. A provider build was interrupted by workspace-full; after cleaning
only generated duplicates, remaining checks resumed on the exact unchanged source/DLL. No code/check failure remains.

[Receipt](competence/GAPS2_PLAYTEST_CHECKS.json), [compiler/reference provenance](competence/GAPS2_PLAYTEST_BUILD_PROVENANCE.json)
and [cumulative gaps3 test ZIP](../test-builds/AI-Control-FACC-v0.3.52-dev-gaps3-9a02565-UMM-test.zip) identify the
frozen output. ZIP SHA-256 `e97222e507007a321f1b97334a1158a796c22fe90ba9a569809a805573b62ca6`; identity/checksum
sidecars accompany it. SDK 8.0.426 / Roslyn 4.11, UMM 0.33.2.0; manifest 0.3.52. The supplied pair is **played gaps2**;
the new diagnostic **gaps3 code is unplayed**. Scoring/transfer policy is preserved, not newly tuned.

In TB/RT, leave Grabber to the engine and include an actual TB release, open-ground leap and ordinary/no-save
Frigid cast if feasible. For invocation evidence, repeat early-round recipients across multiple fights: each
chosen buff should retain its own current ID/gross appraisal, at most once per fight/round/option. A return or a
burst alone is not failure; compare current recipients, holder/sibling losses and override margins. Retain both
AI/combat logs and the startup identity. No additional uploads are required for this diagnostic/tooling repair.
