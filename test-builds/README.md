# Test[4] fixes playtest — c6af593

**[Download the new UMM test ZIP — c6af593](AI-Control-FACC-test4-fixes-c6af593-UMM-test.zip?raw=true)**

The supplied Test[4] logs confirm automatic Burning executes in both modes, including actual DC19 saves and
periodic damage. This build preserves that functionality and adds the **author-approved narrow spell-safety
check**: with Avoid friendly fire on, a new harmful lasting control circle is withheld when an ally's accepted
attack approach clearly ends wholly inside it. No new pathfinding, movement scores, target locks or emergency
cancellation policy changes.

It also reuses repeated boost descriptions and identical periodic reduction reads within one evaluation,
keeping candidates and native context/dice/save calculations. Slow decisions now include stage timing;
slow TB stand-up previews are measured separately. **No measured large speedup is claimed.** Both Test[3]
and Test[4] were played on the same budget laptop; hardware and workload both matter.

[Draft PR #10](https://github.com/recovribox-svg/AIControll-FACC/pull/10) targets development and includes pending
PR #9/#8/#7/#6 as dependencies. Main/shared development are unchanged. This is an unmerged test for intended
v0.3.22; UMM release metadata remains 0.3.21. The withdrawn broad implementation stays withdrawn.

## Install and identify

1. Fully exit the game.
2. Download the ZIP above and install/update it through Unity Mod Manager's **Mods** tab.
3. UMM still shows **0.3.21**. If it will not replace a same-version install, extract the two files into
   `<game>/Mods/CompanionAI_Pathfinder/`, overwriting those files only. Keep existing settings.
4. Restart and verify the complete log contains:

```text
[Build] test4-logic-fixes module=d98eb9f4-8bb4-448f-bfcb-4a701fb87368 on v0.3.21
```

A different/missing marker means another DLL is loaded. To return to normal play, fully exit and reinstall
stable v0.3.21. Send both AI logs and the native **CombatLog**, this ZIP filename and your observations.

## Focused playtest, both TB and RT

- Enable **AI manages toggles**. Start a fresh fight with learned infusions off. Check useful activation,
  native buff/save/burn, replacements/off, management disabled, player-owned groups and original-state restoration.
  A better ordinary action, immunity, applicability, extra burn or the existing threshold can keep an infusion off.
- RT spell safety: with **Avoid friendly fire** on, approach an enemy on flat ground while another companion
  considers Grease centred on that melee target. A clearly contained compact arrival should produce a `[Walk]`
  withhold explanation. Compare FF off, Selective/immunity and an already-in-reach attack. Slopes, boundaries,
  partial overlaps and many large-target/ranged approaches remain uncertain and receive no new guarantee.
  The specific logged Seelah incident is not proven prevented without replay.
- Compare cold/warm Orvath, summons and ordinary companions with the same save/settings/mods, including idle
  portrait selection and TB stand-up. `[Perf]` slow lines now show stages; nested active-infusion time is already
  included in those stages. Slow stand-up previews are separate from normal decision summaries.
- Continue checking Fire Blast before/after resisted hits. **Initial optimistic fire estimates remain unresolved.**
  A failed native reduction query now logs `[Math]` instead of silently falling back. Send native damage details.

## Verification and limits

- **164 tests pass**, zero failed/skipped; Release has zero warnings/errors against real supplied references.
- Native-data checks pass: 13 compiled memo identity/hash assertions, five Grease structural gates, existing
  activation/Free/Swift/layout checks, 486 native cost/quiver vectors, Rare Metal and cone bindings.
- Partial ordinary reader audit: 4,097 loaded exports, one known failed export; 1,220 rows / zero reader errors,
  all normalized verdict/detail rows unchanged from the preceding build.
- **This new build has not been tested in game.** Offline checks do not prove live spell vetoes, command handlers,
  frame-time gains or every infusion family. Only Burning is exercised by the supplied Test[4] gameplay logs.
- Unraveling and Chain Arrows remain withheld; Deep Slumber keeps its spreading restriction. No new Gather,
  metakinesis or form optimizer. Search limits exclude ordinary/active-effect work and cannot interrupt native calls.

[Full Test[4] review](../docs/TEST4_REVIEW.md) · [infusion support matrix](../docs/INFUSION_SELECTION_PLAN.md).

## Identity

- Runtime source: `c6af5932caac1cd821c11dc77fa126bcb710cf90`.
- MVID: `d98eb9f4-8bb4-448f-bfcb-4a701fb87368`.
- ZIP SHA-256: `39d5c86ddd4908032841f4696421db2d02e5bb6d4d7f38b5763f2c1c897e5c1d`.
- DLL SHA-256: `c364089a5a3ef53bf45f380c3203b8dd2b8da1024411a487fe10d00ff43a017f`.
- Flat archive contains exactly `CompanionAI_Pathfinder.dll` and unchanged `Info.json`.
- [Versioned identity](AI-Control-FACC-test4-fixes-c6af593-UMM-test.identity.json) · [SHA256SUMS](SHA256SUMS.txt).

## Earlier downloads retained

- [Previous native-infusion-command fixes — aa9fe94](AI-Control-FACC-test3-fixes-aa9fe94-UMM-test.zip?raw=true)
  and [identity](AI-Control-FACC-test3-fixes-aa9fe94-UMM-test.identity.json).
- [Infusion-selection Test[3] — 05d1959](AI-Control-FACC-infusion-selection-05d1959-UMM-test.zip?raw=true)
  and [identity](AI-Control-FACC-infusion-selection-05d1959-UMM-test.identity.json).
  **Known failed activation path; use the new build for further testing.**
- [Test[1] scoring fixes — 8fb0257](AI-Control-FACC-test1-scoring-fixes-8fb0257-UMM-test.zip?raw=true)
  and [identity](AI-Control-FACC-test1-scoring-fixes-8fb0257-UMM-test.identity.json).
- [Original Test[1] — 0a7ed97](AI-Control-FACC-infusion-value-0a7ed97-UMM-test.zip?raw=true)
  and [identity](build-identity.json).
- [Optional diagnostics — 664815b](AI-Control-FACC-infusion-value-664815b-UMM-test.zip?raw=true)
  and [identity](AI-Control-FACC-infusion-value-664815b-UMM-test.identity.json).

Old filenames/checksums remain unchanged so logs can be tied to the installed binary.
