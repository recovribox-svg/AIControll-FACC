# Shifter combat forms, aspects and Fury

12 November 2026. `codex/shifter-support` starts at PR #41 packaging `a6c3f53`, over fetched development
`1f58150`. It includes Eldritch Archer and the earlier review2 prerequisites. Claude reviews/integrates;
manifest remains 0.3.30. This is a development test, not a release.

## Report and evidence

The author relayed: “Ulbrig's kept shifting into his shape after battle But he never used it in battle”.
The author subsequently identified **v0.3.27 stable**, with automatic self-buff recasting **probably** enabled
because it defaults on. That setting was not confirmed; no log, save or startup MVID accompanies this report.
This is an evidence-based repair of an exposed path, not a reproduced actor-scene diagnosis.

The local release commit `a614b15` packages v0.3.27 source `3f005b9`. Its `RealTimeController.IsWorthKeepingUp`,
and the current one, have no explicit transformation exclusion: a persistent self-buff with a recognised
combat effect can enter automatic out-of-combat upkeep. Existing numerical buff appraisal also does not
price a Polymorph's complete natural-weapon replacement. Treating a form as an ordinary stat buff misses
both its useful attacks and the loss of equipment.

The supplied game assembly and actual user JBP graphs are the rule sources. [Input/native contracts](competence/SHIFTER_NATIVE.json)
record their identities; proprietary assemblies, decompiled files and full blueprint dumps are not committed.
The user's guide URLs are context, not implementation authority. The export contains 86 Shifter polymorph
buff graphs, including Griffonheart/Ulbrig tiers, Manticore, Dinosaur, Spider and dragon variants. This number
is graph coverage, not proof of every archetype/mod combination in gameplay.

## Resulting behavior

- Automatic out-of-combat self-buff upkeep excludes abilities that apply native Polymorph effects. It cannot
  repeatedly reapply a suppressed/expired form. Manual casting/pre-buffing remains deliberate. A native
  long-duration form is not forcibly cancelled at combat end.
- Shifter-style major forms (`Polymorph.AllowDamageTransfer`) get a dedicated core attack/defence comparison
  in the shared turn-based/real-time planner. The chosen form still competes with immediate attacks, healing,
  aspects and other abilities. Availability, ability lists, native action cost, normal duration and existing
  resource/rest policy remain in the normal ability pipeline. No unconditional “always transform” bonus.
- Existing numerical minor-aspect appraisal and C06 resource timing/group capacity are reused. Ulbrig's actual
  Griffon aspect reaches that path, including its Strength and Will benefits. Chimera choices obey the native
  group's current capacity; the planner does not invent extra aspect slots.
- Fury can select one of the actor's real generated natural-weapon choices when **currently off** and the
  projected native routine improves. The current selection, including a pre-fight player choice, is retained.
  Only the inspected immediate/no-resource selector contract is automated. Player ownership, ability lists,
  automatic-toggle setting, round lock, final weapon/hand identity and native availability are checked.
  A profitable major-form candidate is considered before enabling Fury in a human body.
- Existing/suppressed form buffs prevent recasting or tier oscillation. Final issue rechecks combat/form/Fury
  state. Automatic new major forms are withheld while a human-body Fury selection is active; changing the
  native selection across a future body replacement is not guessed. Other classes' non-Shifter form appraisal
  retains its existing path; the out-of-combat transformation exclusion is shared.

## Native rules and forecast boundaries

`Polymorph.OnActivate` adds descriptor-typed physical stats, +4 physical scores for native Master Shapeshifter,
NaturalArmorForm AC, absolute size via UnitPartSizeModifier, granted facts and replacement hands/limbs.
`UnitBody.ApplyPolymorphEffect` deactivates armor and the old shield when slots are replaced. The first
AdditionalLimbs weapon is promoted into the otherwise empty primary hand; remaining and secondary limbs
follow their native roles. New form size replaces current size; it is not added on top of Enlarge Person.

`ShifterFormProjection` reads these scalar inputs, native descriptor stacking, positive lost equipment/buff
modifiers and existing derived stat links. It charges known attack/damage/AC/save/HP losses. It does not
credit recovered penalties, a higher unarmored Dexterity cap or newly granted unarmored class features.
Spellcasting opportunity cost in a future animal body is not established here: automatic new major forms
are withheld for actors with a casting spellbook, including multiclass cases. Their actual existing form,
minor aspects and native attacks remain usable.

`CombatMath.Shifter` uses calculation-only native rule subclasses, explicitly triggered as the native base
event types. It adjusts each **rule result/input**, never actor stats, buff runtime data, equipment, view,
position or HP. Native weapon stats, attack bonus/stat replacement, target AC, critical immunity, damage
reduction and target damage calculation are used. Projected hits bypass the ordinary weapon-hit memo so
a future secondary weapon cannot contaminate the real current-state quote.

The native dice-size rule changes its base to the form size when UseSizeAsBaseForDamage is set; Large
griffon claw dice must not be enlarged a second time. Dinosaur/Spider use the other flag. Native unmodified
blueprint dice overrides can skip scaling in a polymorphed real actor; transferred/modified dice retain
their native scaling path. These cases have separate exported/scalar checks.

PolymorphDamageTransfer replaces dice only when the source claw formula is better and transfers the
source's critical multiplier modifier. With Fury, only the selected weapon receives that transfer unless
the native demon feature applies to all natural attacks. Fury sets other non-unarmed natural weapons to
secondary, even an AlwaysPrimary blueprint. Unarmed weapon stats keep their native exception. Hand and
limb iteratives use separate native attack-count rules; the simple future-form scalar BAB/attribute-damage
pieces are checked against the inspected native arithmetic, including the companion BAB-cap exception,
positive fractional truncation and full negative Strength penalties.

The estimate is deliberately a **core projection**, not a complete simulation of all future facts. New
Pounce/Rend, retained limbs, haste/extra attacks, sneak, new resistances/immunities and Powerful Change's
extra runtime bonus receive no pre-cast positive credit. After transformation the ordinary native routine
reads actual granted facts. The existing fake-hit path does not execute RuleDealDamage/RulePrepareDamage;
transfer-only material/ignore-DR proc benefits are not newly claimed. Arbitrary mod handlers conditional on
body/equipment state can still differ from the projected pre-cast state. No universal exact-mod forecast
or measured Unity speedup is claimed.

There are at most eight unique form quotes against the existing offence snapshot's enemy per decision,
four Fury weapon-kind/hand choices, sixteen generated Fury options and thirty-two Fury routine attacks.
An extreme future-form BAB quote credits at most eight extra iteratives. Duplicate future claws reuse a
single hit quote. Transfer facts are gathered once per decision; immutable form metadata is cached.
There is no all-target/world/path search or actor/buff activation preview. General movement, commitment,
retreat, pathfinding and stuck cancellation are unchanged; native charge/Pounce execution is not expanded.
Legacy real-time combat planning is not expanded.

## Verification and live replay

The final receipt records full unit/inventory suites, real-reference Release build, compatible/supplementary
ProfileAudit and inherited provider regressions, exact added native member signatures, package checksum and
unique MVID. Original full-corpus loading still fails separately on the known Achaekek weapon enum mismatch;
it is not presented as a passing raw audit. Shifter-specific offline checks cover actual form/aspect/selector
graphs, upkeep rejection, dice scaling, branch admission, native-layout Fury flags and compiled guard routes.
These checks do not initialize a scene actor, transform/cast, execute a Fury command or measure Unity timing.

Play **both turn-based and shared real-time**, retaining the package's startup MVID with logs:

1. Ulbrig in human form, automatic self-buffing on, a fight long enough for a useful form: inspect the form
   comparison and chosen action. Repeat around the level 9/14 form upgrades. High armor or an easy fight can
   make staying human appropriate; do not force a cast to satisfy a class label.
2. End combat, enter/leave a safe zone, wait through several upkeep ticks, start a second fight. No automatic
   post-combat form cast/recast; suppressed/ongoing forms follow native restoration/expiry without tier loops.
   Also repeat with automatic self-buffing off and with a deliberately manually/pre-buffed form.
3. Base Shifter animal, Dinosaur/Spider, Manticore ranged spikes and one dragon/Fey variant: confirm attacks,
   actual damage, size and armor after the native cast. Compare real outcomes with the conservative quote.
4. Level 6+ Fury: confirm only natural native choices are offered, useful iteratives appear on the selected
   weapon and other weapons use secondary rules; resources are not fabricated/spent. Compare Fury off/on
   against DR and critical-immune enemies. Native proc-only transfer benefits require live confirmation.
5. Preselect a Fury weapon, change it manually during combat, blacklist the selector, disable automatic toggle
   management, expire/change shape or change equipment. No stale child command, wrong restored weapon or
   override of the player's selection. AI-enabled selections return to the original all-off state after combat.
6. Minor aspects with one/two native group slots, near-empty resources, unlimited-use features and a second
   fight; no overspending or group thrash. Check Pure Mage, ordinary Ranged and a multiclass caster: the new
   physical preparation limits apply without removing ordinary ranged weapon attacks.

Supply the matching AI/combat logs if a form is repeatedly rejected or actual Fury behavior differs. No
additional blueprint archive is currently required; the missing acceptance evidence is live actor execution.
