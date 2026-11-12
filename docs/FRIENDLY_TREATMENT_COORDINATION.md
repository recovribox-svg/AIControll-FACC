# Coordinate an in-flight friendly treatment

12 November 2026. `codex/friendly-dispel-coordination` starts at PR #39 head `8886762`, containing
development `1f58150` and all reviewed-task prerequisites. The broad [exit1 review](EXIT1_PLAYTEST_REVIEW.md)
and [input identities](EXIT1_PLAYTEST_INPUTS.json) identify the playtest.

## Evidence and change

AI 6722–6737: Lann and arcan select Treat Affliction on Seelah before either finishes. Lann's command succeeds;
arcan is interrupted before acting. This is two competing selections, not evidence that two cures/resources
were consumed. Five treatment selections occur across the captures; the other four command outcomes act.

The supplied Classes archive already contains the two actual exported graphs under
`Classes/Basic/BasicActions/Skills/`. The uploaded BubblePrints engine also read the local stock 2.7.0x
database (236,660 records), independently selecting native
`LoreReligionUseAbility` (`4843cb4c23951f54290c5149a4907f54`) and `LoreReligionCooldown`
(`b89dcf508d48da74f8dd5234e6a5eb84`). The action graph applies the daily marker to the recipient and its
`AbilityTargetHasFact` checker is inverted, with `FromCaster=false`: another caster cannot treat that marked
recipient either. Native removal uses a skill check, so an attempted treatment does not establish a successful
removal. [Input/cross-check provenance](competence/TREATMENT_STOCK_INPUTS.json) fingerprints both sources.
Their Data objects match after omitting the presentation name added to the stock envelope. The duplicate
stock records do not add blueprint or ability coverage, nor reconstruct other installed mod patches.

`ScoreAbility` already checks pending enemy non-additive actions. Its friendly unmodelled-dispel fallback
had no equivalent check. That narrow fallback now withholds its +2 utility value when another actor's identical
ability is in flight on this recipient, using `CombatMemory.CastPendingByOther`. Other choices remain eligible.
This changes no native cure/skill rules or daily limits and introduces no blueprint-name/GUID special case.
It does not cancel a command that is already running.

The guard is inside the existing fallback only. Recognized HP healing, modelled descriptor cleansing, damage,
and independently valued effects retain their original appraisal. Hostile handling is unchanged. Existing
exact ability identity, real-time gate, recipient/caster checks, native command completion/interruption,
eight-second reservation bound and combat reset control the lifetime. Once the first command finishes/fails,
the next decision can reassess the actual remaining affliction and native target eligibility. This is not a
permanent treatment lock or a new combat timer.

## Cost and verification

One existing bounded pending-cast lookup, only when this removal fallback has a real eligible effect to remove.
No context, actor simulation, new cache, path query, world scan or resource getter. Both TB and RT retain the
same backend; the existing memory gate makes TB unaffected by in-flight reservations.

Ten checks run in the compatible audit: four compiled-route/lifetime and six actual exported graph/checker
checks. A stock cross-check of the same two IDs does not expand unique coverage. The checks verify actual
removal/heal classification, applied daily marker, recipient-wide native
exclusion, skill checks and the fallback route. These are metadata/compiled-route checks, not execution of the
two-actor race. Full unit/native checks, real-reference Release and exact package identity are in the final receipt.
The original corpus's one-file enum failure remains separately incomplete; compatible/supplementary passes
must not be called an original-full-corpus pass.

## Replay and limits

1. Shared RT, two companions with Treat Affliction, one poisoned recipient: only one gets the treatment-only
   fallback while that identical cast is running. The other should choose another useful action.
2. Interrupt the first before it acts: another companion can reassess treatment once the native command ends.
   After an acted treatment, let native marker/target eligibility decide availability; do not assume removal succeeded.
3. Check two different recipients and two different treatment abilities; the existing exact identity/recipient
   key must not serialize those. Normal HP heals and damage focus fire remain eligible in parallel. Check TB too.
4. Retain startup MVID and command/combat records. The new DLL is unplayed; native command timing and a modified
   treatment graph need live confirmation. No movement, target commitment, retreat, pathfinder or cancellation changes.

Checked source `9464c2d`, [receipt](competence/TREATMENT_PENDING_CHECKS.json) and [review2 test ZIP](../test-builds/AI-Control-FACC-v0.3.30-dev-review2-9464c2d-UMM-test.zip), startup MVID `a21cbb58-f176-4d4a-8ec2-cf912910bc3e`. 629 unit + 11 inventory tests and native checks pass; actual-reference Release 0 warnings/errors; 0/0 added signatures resolve. Compatible/supplementary audit scopes pass; original full-corpus enum failure remains distinct. Gameplay/matched Unity timing pending.
