// ★ v0.3.18 (docs/ACTIVE_ABILITY_PLAN.md AA-4): martial abilities valued by the game's own rules.
//
//   Combat maneuvers (ContextActionCombatManeuver and its custom version, ContextActionDisarm): the chance comes from
//   RuleCombatManeuver's roll (CombatMath.ManeuverOdds), the worth from what ApplyManeuver does on a success:
//     - trip, overrun, awesome blow: the target falls prone (if it can be knocked down), for one round: it spends its
//       move standing up, provoking, and fights at -4 meanwhile - soft control (0.4 x its worth per round);
//     - disarm: the weapon in its hand (not natural, not unarmed) for (margin / 5 + 1) rounds - 0.6;
//     - dirty trick blind / entangle: those conditions for the duration - soft control (0.4); sickened and sunder armor
//       (an AC penalty): a plain debuff (0.25);
//     - bull rush (Hydraulic Push): the target is pushed 5 feet + the margin away from the caster; an enemy fighting a
//       party member in melee must walk back and loses its full attack next turn (turn-based) - 0.2, one round; nothing
//       otherwise. Pull, grapple: a position change the engine does not value (0).
//
//   Vital Strike (AbilityCustomVitalStrike): one attack with the first weapon, plus (VitalStrikeMod - 1) more rolls of its
//   damage dice after the critical multiplier (CombatMath.VitalStrikeExtra) - a standard action that beats a single
//   attack and, after a move, a full attack the unit cannot make.
//   Demoralize upgrades (Demoralize.RunAction), on a successful check: Frightening Thug's greater buff (Frightened) for a
//   round when the duration reaches 4; Hellknight Fearsomeness within 3 m puts the greater buff on instead of Shaken for
//   the whole duration; Shatter Confidence adds its buff for the duration; a mythic Trickster's Persuasion paralyses on a
//   failed Will save (10 + Persuasion ranks), saved again each turn. Each counted on the control scale of the buff it
//   applies, never on a target immune to that buff.
//   Cleave (AbilityCustomCleave): an attack on the target, then on the other enemies in the melee hand's reach by distance
//   to it, each made only while the attacks before it hit; two at most, all with Great Cleave.
using System;
using System.Collections.Generic;
using System.Linq;
using Kingmaker.Blueprints.Root;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.RuleSystem.Rules;
using Kingmaker.UnitLogic;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Abilities.Components;
using Kingmaker.UnitLogic.Buffs.Blueprints;
using Kingmaker.UnitLogic.Mechanics.Actions;
using UnityEngine;

namespace CompanionAI_Pathfinder.Tactics
{
    public static partial class TacticalEngine
    {
        /// <summary>
        /// What a successful maneuver of this type is worth on <paramref name="enemy"/>, as a share of its worth per round
        /// (the control scale), and whether it lasts the maneuver's duration (<paramref name="lasting"/>) or one round.
        /// 0 when it would do nothing to this enemy.
        /// </summary>
        private static float ManeuverStrength(UnitEntityData enemy, AbilityData ability, CombatManeuver type, out bool lasting)
        {
            lasting = true;
            try
            {
                var mechanics = BlueprintRoot.Instance.SystemMechanics;
                switch (type)
                {
                    case CombatManeuver.Trip:
                    case CombatManeuver.Overrun:
                    case CombatManeuver.AwesomeBlow:
                        lasting = false;
                        return enemy.CanBeKnockedOff() ? 0.4f : 0f;
                    case CombatManeuver.Disarm:
                        if (enemy.Buffs.HasFact(mechanics.DisarmMainHandBuff) || enemy.Buffs.HasFact(mechanics.DisarmOffHandBuff)) return 0f;
                        return HoldsManufacturedWeapon(enemy) ? 0.6f : 0f;
                    case CombatManeuver.DirtyTrickBlind:
                        return BuffWorth(enemy, ability, mechanics.DirtyTrickBlindnessBuff, 0.4f);
                    case CombatManeuver.DirtyTrickEntangle:
                        return BuffWorth(enemy, ability, mechanics.DirtyTrickEntangledBuff, 0.4f);
                    case CombatManeuver.DirtyTrickSickened:
                        return BuffWorth(enemy, ability, mechanics.DirtyTrickSickenedBuff, 0.25f);
                    case CombatManeuver.BullRush:
                        lasting = false;
                        return EngagesParty(enemy) && !CombatMemory.IsRealTime ? 0.2f : 0f;
                    case CombatManeuver.SunderArmor:
                        return BuffWorth(enemy, ability, mechanics.SunderArmorBuff, 0.25f);
                    default:
                        return 0f;
                }
            }
            catch (Exception ex)
            {
                Main.Verbose($"[Tactics] maneuver {type} on {enemy?.CharacterName}: {ex.Message}");
                return 0f;
            }
        }

        private static float BuffWorth(UnitEntityData enemy, AbilityData ability, BlueprintBuff buff, float strength)
        {
            if (buff == null || enemy.Buffs.HasFact(buff) || CombatMath.BuffImmune(enemy, buff, ability)) return 0f;
            return strength;
        }

        /// <summary>The enemy is in melee with a party member now.</summary>
        private static bool EngagesParty(UnitEntityData enemy)
        {
            try { return enemy.CombatState.EngagedUnits.Any(u => u != null && u.IsPlayerFaction && u.Descriptor.State.IsConscious); }
            catch { return false; }
        }

        /// <summary>Disarm takes a weapon that is neither natural nor unarmed (RuleCombatManeuver.ApplyManeuver).</summary>
        private static bool HoldsManufacturedWeapon(UnitEntityData unit)
        {
            foreach (var weapon in new[] { unit.Body.PrimaryHand.MaybeWeapon, unit.Body.SecondaryHand.MaybeWeapon })
                if (weapon != null && !weapon.Blueprint.IsUnarmed && !weapon.Blueprint.IsNatural) return true;
            return false;
        }

        /// <summary>Vital Strike, Cleave, Devastating Blow or an ability's melee attack on <paramref name="target"/>, valued
        /// like the weapon attacks they are.</summary>
        private static float WeaponAbilityValue(TacticalContext ctx, AbilityData ability, AbilityProfile profile, UnitEntityData target, out string why)
        {
            why = "dmg";
            // ★ v0.3.21: a melee attack on each enemy around (Blade Whirlwind: AbilityTargetsAround on the caster).
            if (profile.OnlyMeleeAttack && profile.IsAoE && target != null)
            {
                float sum = 0f;
                int reached = 0;
                foreach (var enemy in UnitsAround(ctx.Enemies, target.Position, profile.AoERadius).Where(e => profile.AreaReaches(ability, e)))
                {
                    float part = MeleeAttackValue(ctx, ability, profile, enemy, out _);
                    if (part <= 0f) continue;
                    sum += part;
                    reached++;
                }
                if (sum > 0f) { ctx.AimCounted = true; why = $"dmg:{reached} in reach"; }
                else why = "no enemy in reach";
                return sum;
            }
            if (target == null || !ctx.Unit.IsEnemy(target)) { why = "not an enemy"; return 0f; }
            float value = profile.VitalStrike != null ? VitalStrikeValue(ctx, profile.VitalStrike, target)
                        : profile.DevastatingBlow ? DevastatingBlowValue(ctx, target)
                        : profile.Cleave != null ? CleaveValue(ctx, profile.Cleave, target, out why)
                        : MeleeAttackValue(ctx, ability, profile, target, out why);
            if (value > 0f) ctx.AimCounted = true;
            else why = "nothing to gain";
            return Math.Max(0f, value);
        }

        private static float VitalStrikeValue(TacticalContext ctx, AbilityCustomVitalStrike vitalStrike, UnitEntityData target)
        {
            var unit = ctx.Unit;
            var est = CombatMath.EstimateWeaponAttack(unit, target, full: false);
            if (est == null) return 0f;
            float expected = est.ExpectedDamage + CombatMath.VitalStrikeExtra(unit, target, est, vitalStrike);
            float value = DamageValue(target, expected, est.AnyHitChance) * AttackWeight(ctx, target, est.IsRanged) *
                          WakeFactor(target, expected);
            // A shot provokes like any ranged attack roll (where the unit stands now; after a move the ability's approach
            // prices the move).
            if (est.IsRanged && ProvokeObserver.WeaponProvokes(unit, est.Weapon))
            {
                var provoke = AttackRollProvokeRisk(ctx, unit.Position, 1);
                if (provoke != null) value = value * (1f - provoke.StopChance) - provoke.Cost;
            }
            return value;
        }

        /// <summary>
        /// ★ v0.3.21: Devastating Blow (AbilityCustomMeleeAttack.Deliver): one attack with the melee hand at -5 that always
        /// threatens a critical hit (the confirmation is rolled as usual; nothing on a target immune to critical hits).
        /// </summary>
        private static float DevastatingBlowValue(TacticalContext ctx, UnitEntityData target)
        {
            var unit = ctx.Unit;
            if (unit.GetThreatHandMelee() == null) return 0f;
            var est = CombatMath.EstimateWeaponAttack(unit, target, false, (w, i) => new KeyValuePair<int, float>(i == 0 ? -5 : 0, 0f));
            if (est == null || est.IsRanged) return 0f;
            float expected = OneAttack(est, autoThreat: true);
            return DamageValue(target, expected, est.AnyHitChance) * WakeFactor(target, expected) * AttackWeight(ctx, target, false);
        }

        /// <summary>
        /// ★ v0.3.21: the expected damage of one attack (<paramref name="est"/> of a single attack); with
        /// <paramref name="autoThreat"/> every hit threatens a critical hit, the confirmation rolled as usual.
        /// </summary>
        private static float OneAttack(WeaponAttackEstimate est, bool autoThreat)
        {
            float p = est.FirstHitChance;
            if (!autoThreat || p <= 0f) return est.ExpectedDamage;
            // The weapon's threat range (FirstCritChance = threat x hit x confirmation); 0 against a target immune to crits.
            float threat = est.FirstCritChance / (p * p);
            if (threat <= 0f) return est.ExpectedDamage;
            return est.ExpectedDamage + (1f - Mathf.Clamp01(threat)) * p * (est.CritMultiplier - 1) * est.FirstPerHit;
        }

        /// <summary>
        /// ★ v0.3.21: the melee attack an ability makes (ContextActionMeleeAttack.RunAction): with the melee hand on the
        /// target - or, with SelectNewTarget, on the enemy in reach nearest to it - one attack or the full attack, with its
        /// auto-hit and auto-threat options; plus what the caster's own features add to that attack while the ability runs
        /// (<see cref="OnHitManeuvers"/>: Piledriver's trip or bull rush).
        /// </summary>
        private static float MeleeAttackValue(TacticalContext ctx, AbilityData ability, AbilityProfile profile, UnitEntityData target,
                                              out string why)
        {
            why = "dmg";
            var unit = ctx.Unit;
            var action = profile.MeleeAttack;
            var hand = unit.GetThreatHandMelee();
            if (action == null || hand == null || target == null) return 0f;
            if (profile.AppliesTo(action, ability, target) == EffectApplies.No) return 0f;
            var victim = target;
            if (action.SelectNewTarget)
            {
                IEnumerable<UnitEntityData> known;
                try { known = unit.Memory.Enemies.Select(i => i.Unit).Where(e => e != null).ToList(); }
                catch { known = ctx.Enemies; }
                victim = known.Where(e => e != target && e.Descriptor.State.IsConscious && e.HPLeft > 0 && unit.IsReach(e, hand))
                              .OrderBy(e => (e.Position - target.Position).sqrMagnitude).FirstOrDefault();
                if (victim == null) { why = "no other enemy in reach"; return 0f; }
            }
            var est = CombatMath.EstimateWeaponAttack(unit, victim, action.FullAttack,
                                                      action.AutoHit ? (w, i) => new KeyValuePair<int, float>(40, 0f) : (Func<Kingmaker.Items.ItemEntityWeapon, int, KeyValuePair<int, float>>)null);
            if (est == null || est.IsRanged) return 0f;
            float expected = action.FullAttack ? est.ExpectedDamage : OneAttack(est, action.AutoCritThreat);
            float value = DamageValue(victim, expected, est.AnyHitChance) * WakeFactor(victim, expected);
            float maneuver = OnHitManeuvers(ctx, ability, victim, est.FirstHitChance, out string maneuverNote);
            if (maneuver > 0f)
            {
                value += maneuver;
                why = $"dmg+{maneuverNote}";
            }
            return value * AttackWeight(ctx, victim, false);
        }

        /// <summary>
        /// ★ v0.3.21: maneuvers the caster's features make on a hit while <paramref name="ability"/> runs: an
        /// AddAbilityUseTrigger for it (before the cast) puts a buff on the caster whose AddInitiatorAttackWithWeaponTrigger
        /// (on a hit) runs ContextActionCombatManeuver - the Two-Handed Fighter's Piledriver trips or bull rushes with the
        /// attack. Each valued like the maneuver itself (ManeuverStrength, the CMB against CMD roll), times the hit chance.
        /// </summary>
        private static float OnHitManeuvers(TacticalContext ctx, AbilityData ability, UnitEntityData victim, float hitChance, out string note)
        {
            note = null;
            float value = 0f;
            var unit = ctx.Unit;
            try
            {
                foreach (var fact in unit.Facts.List)
                {
                    if (fact?.Blueprint?.ComponentsArray == null || !fact.Active) continue;
                    foreach (var component in fact.Blueprint.ComponentsArray)
                    {
                        if (!(component is Kingmaker.UnitLogic.FactLogic.AddAbilityUseTrigger use) || use.AfterCast) continue;
                        bool forThis = use.ForOneSpell && use.Ability == ability.Blueprint ||
                                       use.ForMultipleSpells && use.Abilities != null && use.Abilities.Any(r => r?.Get() == ability.Blueprint);
                        if (!forThis || use.Action?.Actions == null) continue;
                        foreach (var apply in use.Action.Actions.OfType<ContextActionApplyBuff>())
                        {
                            if (!apply.ToCaster || apply.Buff?.ComponentsArray == null) continue;
                            foreach (var trigger in apply.Buff.ComponentsArray.OfType<Kingmaker.UnitLogic.Mechanics.Components.AddInitiatorAttackWithWeaponTrigger>())
                            {
                                if (trigger.TriggerBeforeAttack || !trigger.OnlyHit || trigger.CriticalHit || trigger.OnlyNatural20 ||
                                    trigger.OnlySneakAttack || trigger.ActionsOnInitiator || trigger.Action?.Actions == null) continue;
                                foreach (var m in trigger.Action.Actions.OfType<ContextActionCombatManeuver>())
                                {
                                    float strength = ManeuverStrength(victim, ability, m.Type, out bool lasting);
                                    if (strength <= 0f) continue;
                                    CombatMath.ManeuverOdds(unit, victim, ability, new AbilityProfile.ManeuverEffect { Type = m.Type, Action = m },
                                                            out float chance, out float rounds);
                                    float part = hitChance * chance * CombatMath.ControlWorth(victim) * strength *
                                                 (lasting ? Math.Min(Math.Max(1f, rounds), Math.Min(ctx.ExpectedRounds, 3f)) : 1f);
                                    if (CombatMath.IsDisabled(victim)) part *= 0.4f;
                                    if (part <= 0f) continue;
                                    value += part;
                                    note = m.Type.ToString().ToLowerInvariant();
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Main.Verbose($"[Tactics] on-hit maneuvers of {ability?.Name}: {ex.Message}");
            }
            return value;
        }

        private static float CleaveValue(TacticalContext ctx, AbilityCustomCleave cleave, UnitEntityData target, out string why)
        {
            why = "dmg";
            var unit = ctx.Unit;
            var hand = unit.GetThreatHandMelee();
            if (hand == null) return 0f;
            bool greater = cleave.GreaterFeature != null && unit.Descriptor.Progression.Features.HasFact(cleave.GreaterFeature);
            // Who else is in reach: from here when the target already is; otherwise (a move first) only the enemies next
            // to the target, at a discount for not knowing where the move ends.
            bool reachNow = unit.IsReach(target, hand);
            // ★ v0.3.18 (review): from the enemies the unit knows, as the game picks them (AbilityCustomCleave: any enemy
            // not dead) - an unconscious one in reach takes a swing too, worth nothing.
            IEnumerable<UnitEntityData> known;
            try { known = unit.Memory.Enemies.Select(i => i.Unit).Where(e => e != null).ToList(); }
            catch { known = ctx.Enemies; }
            var others = known
                .Where(e => e != target && !e.Descriptor.State.IsDead &&
                            (reachNow ? unit.IsReach(e, hand)
                                      : Vector3.Distance(e.Position, target.Position) <= 1.5f + e.Corpulence + target.Corpulence))
                .OrderBy(e => (e.Position - target.Position).sqrMagnitude)
                .ToList();
            var chain = new List<UnitEntityData> { target };
            chain.AddRange(others.Take(greater ? others.Count : 1));

            float value = 0f, reached = 1f;
            int counted = 0;
            foreach (var enemy in chain)
            {
                var est = CombatMath.EstimateWeaponAttack(unit, enemy, full: false);
                if (est == null) break;
                float part = !enemy.Descriptor.State.IsConscious ? 0f
                           : reached * DamageValue(enemy, est) * WakeFactor(enemy, est.ExpectedDamage);
                value += enemy == target || reachNow ? part : part * 0.7f;
                reached *= est.FirstHitChance;
                counted++;
            }
            if (counted > 1) why = $"dmg:cleave {counted}";
            return value * AttackWeight(ctx, target, false);
        }

        /// <summary>
        /// ★ v0.3.18 (test10): the extra share of <paramref name="enemy"/>'s worth per round that Demoralize's upgrades give
        /// on a successful check, beyond its Shaken buff (<paramref name="shaken"/> share for <paramref name="rounds"/>).
        /// Negative when the greater buff replaces Shaken on a target immune to it.
        /// </summary>
        private static float DemoralizeUpgrades(TacticalContext ctx, AbilityData ability, Demoralize action, UnitEntityData enemy,
                                                float shaken, float rounds)
        {
            var caster = ctx.Unit;
            float share = 0f;
            try
            {
                var features = caster.Descriptor.State.Features;
                var greater = action.GreaterBuff;
                bool greaterWorks = greater != null && !enemy.Buffs.HasFact(greater) && !CombatMath.BuffImmune(enemy, greater, ability);
                float greaterShare = greaterWorks ? AbilityProfile.ControlShareOf(greater) : 0f;
                if (features.HellknightFearsomeness && caster.DistanceTo(enemy.Position) < 3f && greater != null)
                {
                    // The greater buff instead of Shaken, for the whole duration.
                    share += (greaterShare - shaken) * rounds;
                }
                else if (features.FrighteningThug && greaterWorks)
                {
                    CombatMath.IntimidateOdds(caster, enemy, action, out _, out _, out float fourPlus);
                    share += fourPlus * greaterShare;   // one round
                }
                if (action.ShatterConfidenceFeature != null && action.ShatterConfidenceBuff != null &&
                    caster.Descriptor.HasFact(action.ShatterConfidenceFeature) &&
                    !CombatMath.BuffImmune(enemy, action.ShatterConfidenceBuff, ability))
                    share += AbilityProfile.ControlShareOf(action.ShatterConfidenceBuff) * rounds;
                if (features.TricksterPersuasionParalyze)
                {
                    var paralysis = Kingmaker.Blueprints.Root.BlueprintRoot.Instance.SystemMechanics.ParalyzedBuffMindAffectingSaveEachTurn;
                    if (paralysis != null && !CombatMath.BuffImmune(enemy, paralysis, ability))
                    {
                        int dc = 10 + caster.Stats.SkillPersuasion.BaseValue;
                        float fail = Mathf.Clamp((dc - enemy.Stats.SaveWill.ModifiedValue - 1) / 20f, 0.05f, 0.95f);
                        // Saved again each turn: it lasts while the saves fail, at most the duration.
                        float lasts = Math.Min(rounds, 1f / Math.Max(0.05f, 1f - fail));
                        share += fail * 1f * lasts;
                    }
                }
            }
            catch (Exception ex)
            {
                Main.Verbose($"[Tactics] demoralize upgrades {caster.CharacterName}->{enemy.CharacterName}: {ex.Message}");
            }
            return share;
        }
    }
}
