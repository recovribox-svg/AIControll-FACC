# Focused Shot: prospective bonus on one ranged weapon attack

The author approved this support task after the review5 audit. Development was refreshed at `44b224f`;
this branch includes PR #48 `4c0ef7d` (and #47) so its test package retains the review7 corrections.
Review/integrate those prerequisites first. Summon control remains the separate Claude implementation task.

## Evidence and scope

Review5 TB line 244 reports Aeiluvetar's Focused Shot as effect not understood, so it is never offered.
The supplied Feats export contains the feature, action and hidden buff; identities are recorded in
[the native evidence manifest](competence/FOCUSED_SHOT_NATIVE.json). This is an actual discovered ability,
not a class-name inference or an invented damage bonus.

In the supplied game assembly, `AbilityExecutionProcess.PrepareCast` invokes `IAbilityOnCastLogic` before
delivery. `AbilityExecuteActionOnCast.OnCast` applies the action's one-second child buff to its caster.
`AddContextStatBonus` supplies AdditionalDamage from the caster's Intelligence bonus; its base-value method,
multiplier and minimum are reused. AdditionalDamage is not an attribute, so Arcanist Powerful Change cannot
add a second attribute bonus here. The native ranged action selects `GetThreatHandRanged` and triggers one
`RuleAttackWithWeapon`. Its exported FullAttack, ExtraAttack, retargeting and automatic-hit/critical flags are
all false. The hidden buff removes itself after an attack, including a miss. Descriptions are not the rules.

## Integration

The cached profile recognizes this exact supported **structure**, without ability-name/GUID dispatch.
It requires the three inspected components, one targeted physical ranged attack, an unconditional before-cast
self application, and the replace-stacking, one-second buff with only its additive damage stat and unconditional
after-attack removal. Modified actions, subclasses, additional payloads/selectors, restricted bonuses and other
value sources remain unsupported. The actual area-based Penetrating Cascade graph is a negative control;
this is not blanket support for every action named ContextActionRangedAttack.

The ability enters the existing weapon-ability valuation and ordinary native availability/action/approach/issue
paths in both modes. It competes with single/full attacks and spells. Pure Mage continues excluding physical
weapon abilities. Ranged preference works without requiring a spellbook. The value uses shared damage, kill,
role/wake and weapon-provocation functions; it does not award a permanent buff or generic utility score.

The prospective modifier goes through the existing cached weapon-attack delta model, including its DR,
physical-immunity/decline and critical handling. An existing live buff is not added twice. The native
DisableAdditionalDamage/specific-buff-immunity gates give no prospective bonus. Zero/negative values remain
zero/negative. The forecast requires one attack and the native ranged hand; an unavailable context, substituted
delivery or different/mixed enumerated hand stays unknown. No full attack, extra arrow or alternate target is
invented. Existing shared weapon-forecast approximations, including damage-delta and critical approximations,
remain; this is not a numerical overhaul of weapon damage.

The profile stores two metadata references. It adds no scene/party/target scan, actor preview, temporary buff,
inventory edit or native attack event. Each normal target appraisal adds one read-only ability context and
reuses the frame-scoped weapon routine for the delta; metadata admission is cached with the existing profile.
No general movement, target commitment, retreat, native pathfinder or stuck cancellation policy changes.

## Verification and replay

ProfileAudit exercises the actual exported graph, changed flags/payloads, extra-attack and area negatives,
native scalar bonus/minimum calculation, zero/negative/active/disabled cases and compiled production routes.
Scalar tests replace the value temporarily and restore the fixture; they do not initialize an actor, calculate
its live Intelligence, apply a buff or execute an attack. All existing unit/inventory/native regressions and
the real-reference Release build are recorded in the final receipt. Offline checks do not establish gameplay.

In both TB and RT, use a ranged character with Focused Shot and positive Intelligence bonus. After a move,
compare Focused Shot with one ordinary shot; with a full action available it must still compete against the
full routine. Verify exactly one shot, the game's actual bonus and native resource/action accounting. Repeat
with zero/negative Intelligence bonus, DR, weapon-immune swarms, critical immunity, an existing buff and an
engaging enemy that can provoke. Check Ranged preference on a non-caster, Pure Mage exclusion, ability
blocklists and ordinary spell/weapon actions. Keep the startup MVID in the resulting logs. Timing and native
scene/command replay remain pending. Manifest stays 0.3.30; no main/development push, merge or release.

Checked source `b890309`, [receipt](competence/FOCUSED_SHOT_CHECKS.json) and [focused1 test ZIP](../test-builds/AI-Control-FACC-v0.3.30-dev-focused1-b890309-UMM-test.zip), startup MVID `22e24115-7500-4df6-828a-305f0075e591`. 668 unit + 27 inventory tests and native checks pass; actual-reference Release 0 warnings/errors; 10/10 added signatures resolve. Compatible/supplementary audit scopes pass; original full-corpus enum failure remains distinct. Gameplay/matched Unity timing pending.
