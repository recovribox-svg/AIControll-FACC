# Prompt for the advanced planner

Give Claude the following prompt. The research is available in this repository; attach WorldCrawl.zip and
CompanionAI_v3-master (1).zip if Claude needs to independently inspect those reference implementations.
Neither mod is a dependency or compatibility target.

---

Plan a worthwhile next feature for AI Control in recovribox-svg/AIControll-FACC.

Read CONTRIBUTING.md, AGENTS.md, the current CLAUDE.md handover, then
docs/TACTICAL_EXPANSION_INVESTIGATION.md and its evidence receipt
docs/competence/TACTICAL_EXPANSION_RESEARCH.json. Check current development
claude/dreamy-pasteur-b9h3rn and open PRs before choosing a base. The research inspected development 96dabaa and
the later candidate in PR #73; do not assume its prerequisite stack has merged.

The goal is to improve AI Control itself. WorldCrawl and Rogue Trader's CompanionAI v3 are design references,
not compatibility targets or architectures to port. Verify the research's claims against current code and native
Wrath mechanics; improve or reject its recommendations when the evidence warrants it.

My recommended planning candidate is native tactical mobility shared by companions and summons: a creature
uses a suitable owned repositioning ability when the legal action or safety it enables is worth its real cost.
Consider a first slice for one verified self-only native teleport; reuse existing action forecasts, safety,
bounded candidates and command dispatch. Group teleports, swaps, mounts, unit-anchor offsets and action lockouts
need their own contracts. Movement implementation will need my specific approval after the plan.

Compare its benefit, risk and cost with the strongest alternative: more complete weapon-outcome forecasting,
starting with a verified native miss-damage effect such as Destructive Shockwave. Summon signature effects
(especially Unholy Nimbus's delayed blast) and the existing Fusion per-save-outcome control gap are other
evidence-backed options if one offers a better return. Choose and plan one focused project; do not combine them
into an engine rewrite.

The report also investigates my possible ranged Coup de Grace observation. Gaps2 contains no issued Coup,
so do not claim a live reproduction. Assess the specific first-weapon range versus actual melee-delivery mismatch
and propose a separate small correction and test, without a global player-command patch.

For summon work, reuse S1 and existing control. Verify runtime behavior rather than trusting every inventory
label: the report identifies a personal-AoE classification mismatch for Screech/Drone. Do not propose adding
mechanisms that the live engine already has, or importing NPC quotas, random casting, flat focus bonuses,
spell-level buff tiers, Rogue Trader AP rules or combat-time LLM calls.

Produce one implementation-ready plan document with:

- The concrete player benefit, current failing/unsupported examples and non-goals, distinguishing verified
  evidence from inference.
- The native contracts and current code paths to reuse; proposed small reviewable slices, dependencies and
  the earliest useful package.
- Action/resource accounting, recipient/weapon/destination consistency, uncertainty, failure recovery and
  preservation of player ownership, blocklists and both combat modes.
- Explicit bounds on quotes, destinations and per-decision work; cache keys/invalidation and a performance
  comparison that does not sacrifice valid options merely to improve timings.
- Meaningful tests, native ProfileAudit/real-reference zero-warning Release checks, focused TB/RT replay
  including a mixed 4–8 summon pack, package identity/provenance rules if a build is later produced, and clear
  go/no-go criteria.
- Any missing inputs named precisely, and the risks/benefits that determine whether this work warrants its cost.

Feasible, evidence-based, integrated, effective and performant code is the standard. Planning only for now:
no gameplay implementation, movement-policy change, merge, main push or release.
