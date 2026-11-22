// ★ cast disruption (17 November 2026; docs/BURST_AIM_AND_TACTICS_PLAN.md §4, slice B): in real time, a hit on an enemy
// whose spell has not gone off yet forces a concentration check, and a failed check spends the spell for nothing
// (UnitConcentrationController, RuleCheckConcentration, UnitUseAbility.FailIfConcentrationCheckFailed). A weapon attack or a
// damaging ability that can land before the cast goes off gains the chance it breaks the cast times what the cast would
// cost the party. Pure rules: DisruptRules. Turn-based: nothing (the enemy casts on its own turn).
// Stun, daze and the like also stop a cast (UnitCommandController: !CanAct / !CanCast), but the enemy keeps the spell; the
// existing control value already counts the turns they take, so no bonus is added for them here.
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Commands;
using Kingmaker.UnitLogic.Commands.Base;
using Kingmaker.View.Animation;
using UnityEngine;

namespace CompanionAI_Pathfinder.Tactics
{
    public static partial class TacticalEngine
    {
        /// <summary>An enemy's cast that damage can break: what it is, what it would cost, how long until it goes off.</summary>
        internal sealed class CastInfo
        {
            public AbilityData Spell;
            public int SpellLevel, Concentration;
            /// <summary>What the cast would cost the party if it went off, in damage-equivalent points.</summary>
            public float Stakes;
            /// <summary>The stakes are the spell's own forecast (else the caster's worth for a round).</summary>
            public bool Read;
            public string Victim;
            /// <summary>Seconds until the cast goes off, at this decision.</summary>
            public float SecondsLeft;
            /// <summary>
            /// ★ review follow-up F2: the chance the cast survives the hits allies have already been ordered to make on the
            /// caster, and how many of them land in time. Read lazily once per decision (reset in ReadCasting).
            /// </summary>
            public float OthersStill = 1f;
            public int OthersHits;
            public bool OthersRead;
        }

        // Per running cast: its spell, parameters and stakes are read once (weak: the command goes when it ends).
        private static readonly ConditionalWeakTable<UnitCommand, CastInfo> CastInfos = new ConditionalWeakTable<UnitCommand, CastInfo>();
        private static int s_DisruptFight = int.MinValue, s_DisruptLines;
        private const int MaxDisruptLinesPerFight = 64;

        // ConfigureAnimations can shorten this for IsIgnoreCooldown. Read the window actually stored on the issued
        // command, without configuring animations (which mutates the view and draws a sequence-selection random roll).
        private static readonly HarmonyLib.AccessTools.FieldRef<UnitAttack, float> AttackWindowRef = ReadAttackWindowRef();

        private static HarmonyLib.AccessTools.FieldRef<UnitAttack, float> ReadAttackWindowRef()
        {
            try { return HarmonyLib.AccessTools.FieldRefAccess<UnitAttack, float>("m_AnimationsDuration"); }
            catch { return null; }
        }

        private static bool TryAttackWindow(UnitAttack attack, out float window)
        {
            window = 0f;
            if (attack == null || AttackWindowRef == null) return false;
            try { window = AttackWindowRef(attack); }
            catch { return false; }
            return window > 0f && !float.IsInfinity(window);
        }

        /// <summary>The cast <paramref name="enemy"/> is making that damage can break, or null (real time only).</summary>
        internal static CastInfo CastingFor(TacticalContext ctx, UnitEntityData enemy)
        {
            if (enemy == null || !CombatMemory.IsRealTime) return null;
            if (ctx.Casting == null) ctx.Casting = ReadCasting(ctx);
            return ctx.Casting.Count > 0 && ctx.Casting.TryGetValue(enemy, out var cast) ? cast : null;
        }

        /// <summary>
        /// The enemies whose Standard command is a running cast that has not gone off and can be broken by damage - the
        /// native condition for a concentration check (UnitConcentrationController.OnEventAboutToTrigger).
        /// </summary>
        private static Dictionary<UnitEntityData, CastInfo> ReadCasting(TacticalContext ctx)
        {
            var map = new Dictionary<UnitEntityData, CastInfo>();
            foreach (var enemy in ctx.Enemies)
            {
                try
                {
                    if (!(enemy?.Commands?.Standard is UnitUseAbility use) || !use.IsRunning || use.IsActed || use.Cutscene) continue;
                    var spell = use.Ability;
                    if (spell?.Blueprint == null || !spell.CanBeInterruptedWithDamage) continue;
                    float left = BurstWatch.NativeCastTime(use, BurstRules.StandardCastSeconds) - use.TimeSinceStart;
                    if (!(left > DisruptRules.MarginSeconds)) continue;
                    bool fresh = !CastInfos.TryGetValue(use, out var info);
                    if (fresh)
                    {
                        info = AssessCast(ctx, enemy, use, spell);
                        CastInfos.Add(use, info);
                    }
                    info.SecondsLeft = left;
                    info.OthersRead = false;   // allies' orders change between decisions
                    map[enemy] = info;
                    if (fresh) NoteCast(enemy, info);
                }
                catch (Exception ex)
                {
                    Main.Verbose($"[Disrupt] reading {enemy?.CharacterName}'s cast: {ex.Message}");
                }
            }
            return map;
        }

        /// <summary>The cast's parameters by the game's rule and its stakes: the spell on its actual victim, else a round.</summary>
        private static CastInfo AssessCast(TacticalContext ctx, UnitEntityData enemy, UnitUseAbility use, AbilityData spell)
        {
            var info = new CastInfo { Spell = spell };
            var p = CombatMath.ParamsOf(spell);
            if (p != null)
            {
                info.SpellLevel = p.SpellLevel;
                info.Concentration = p.Concentration;
            }
            UnitEntityData victim = null;
            var target = use.Target;
            if (target?.Unit != null && ctx.Allies.Contains(target.Unit)) victim = target.Unit;
            else if (target != null)
            {
                // Aimed at a point or at someone else: the party member nearest where it lands, if the area reaches it.
                var profile = AbilityProfile.Get(spell);
                float reach = profile != null && profile.IsAoE ? profile.AoERadius : 0f;
                Vector3 at = target.Unit != null ? target.Unit.Position : target.Point;
                float best = float.MaxValue;
                foreach (var ally in ctx.Allies)
                {
                    float d = Vector3.Distance(ally.Position, at);
                    if (d <= reach + ally.Corpulence && d < best) { best = d; victim = ally; }
                }
            }
            float harm = victim != null ? EnemyThreat.CastHarm(enemy, spell, victim, ctx.Allies) : 0f;
            info.Read = harm > 0f;
            info.Victim = victim?.CharacterName;
            info.Stakes = DisruptRules.Stakes(harm, EnemyThreat.RoundWorth(enemy));
            return info;
        }

        private static void NoteCast(UnitEntityData enemy, CastInfo info)
        {
            if (s_DisruptFight != CombatMemory.Fight)
            {
                s_DisruptFight = CombatMemory.Fight;
                s_DisruptLines = 0;
            }
            if (s_DisruptLines >= MaxDisruptLinesPerFight) return;
            s_DisruptLines++;
            Main.Log($"[Disrupt] {enemy.CharacterName} is casting {info.Spell.Name} (level {info.SpellLevel}, concentration " +
                     $"{info.Concentration:+0;-0}, goes off in {info.SecondsLeft:F1} s); breaking it is worth {info.Stakes:F1} " +
                     (info.Read ? $"(the spell on {info.Victim})" : "(its round)") +
                     (s_DisruptLines == MaxDisruptLinesPerFight ? " (further [Disrupt] lines this fight are not logged)" : ""));
        }

        /// <summary>
        /// What up to two hits on a casting enemy are worth for breaking its cast: each lands with its chance and breaks the cast
        /// by the concentration rule, if it lands in time. <paramref name="chance"/>: that the cast is broken.
        /// </summary>
        private static float DisruptBonus(CastInfo cast, float hit1, float damage1, float at1, float hit2, float damage2, float at2,
                                          out float chance)
        {
            float still = 1f;
            if (hit1 > 0f && DisruptRules.InTime(at1, cast.SecondsLeft))
                still = DisruptRules.StillCasting(still, hit1, DisruptRules.FailChance(cast.SpellLevel, damage1, cast.Concentration));
            if (hit2 > 0f && DisruptRules.InTime(at2, cast.SecondsLeft))
                still = DisruptRules.StillCasting(still, hit2, DisruptRules.FailChance(cast.SpellLevel, damage2, cast.Concentration));
            chance = 1f - still;
            return DisruptRules.Value(still, cast.Stakes);
        }

        /// <summary>A weapon attack's bonus for breaking <paramref name="enemy"/>'s cast: its first one or two attacks.</summary>
        private static float WeaponDisruptBonus(TacticalContext ctx, UnitEntityData enemy, WeaponAttackEstimate est, bool full,
                                                float walkSeconds, out string tag)
        {
            tag = "";
            float bonus = WeaponDisruptValue(ctx, enemy, est, full, walkSeconds, out float chance);
            if (bonus > 0f)
            {
                var cast = CastingFor(ctx, enemy);
                tag = $" [breaks {cast.Spell.Name} {chance:P0}" +
                      (cast.OthersHits > 0 ? $" more, after {cast.OthersHits} allied hit{(cast.OthersHits == 1 ? "" : "s")} already ordered]" : "]");
            }
            return bonus;
        }

        /// <summary>The same quote without formatting a label for every attack-mode alternative.</summary>
        private static float WeaponDisruptValue(TacticalContext ctx, UnitEntityData enemy, WeaponAttackEstimate est, bool full,
                                                float walkSeconds, out float chance)
        {
            chance = 0f;
            var cast = CastingFor(ctx, enemy);
            if (cast == null || est == null) return 0f;
            float distance = ctx.Distance(enemy);
            // ★ coverage3 review: the attack starts after its walk and the native wait for its standard action.
            walkSeconds = DisruptRules.OrderedStartSeconds(walkSeconds, ReadySeconds(ctx.Unit, null));
            // ★ F2: the second attack follows on the native schedule (6 s / N apart unless chained), not a fixed swing.
            // ★ coverage2 review (R4): a bow's shots one animation apart (measured), not 0.7 s.
            bool bow = ProspectiveChainedAttacks(est.SecondWeapon);
            float spacing = DisruptRules.SwingSpacing(est.Attacks, bow, bow: bow);
            float at1 = DisruptRules.WeaponHitSeconds(0, walkSeconds, est.IsRanged, distance, spacing);
            bool hasSecond = full && est.Attacks > 1 && est.SecondWeapon?.Blueprint != null;
            float at2 = DisruptRules.WeaponHitSeconds(1, walkSeconds, est.SecondWeapon?.Blueprint?.IsRanged == true, distance, spacing);
            float own = DisruptBonus(cast, est.FirstHitChance, est.FirstPerHit, at1,
                                     hasSecond ? est.SecondHitChance : 0f, est.SecondPerHit, at2, out chance);
            return AfterOrderedHits(ctx, enemy, cast, own, ref chance);
        }

        /// <summary>
        /// ★ F2 (docs/REVIEW_FOLLOWUPS_PLAN.md, B-R1): this action's own break value, given the hits allies have already been
        /// ordered to make on the caster. Breaks are independent, so it adds others-survive × our-break-chance × stakes
        /// (DisruptRules.MarginalValue); <paramref name="chance"/> becomes that added chance. Nothing is reserved for actions
        /// not yet chosen.
        /// </summary>
        private static float AfterOrderedHits(TacticalContext ctx, UnitEntityData enemy, CastInfo cast, float own, ref float chance)
        {
            if (!(own > 0f)) return own;
            float others = OthersStill(ctx, enemy, cast);
            if (others >= 1f) return own;
            float value = DisruptRules.MarginalValue(1f - chance, others, cast.Stakes);
            chance *= others;
            return value;
        }

        /// <summary>
        /// ★ F2: the chance <paramref name="enemy"/>'s cast survives the weapon hits allies have already been ordered to make on
        /// it - their running or approaching UnitAttack - each by its routine estimate, the concentration rule and the
        /// native attack schedule, counting only hits in time (at most two per ally). Once per cast and decision.
        /// </summary>
        private static float OthersStill(TacticalContext ctx, UnitEntityData enemy, CastInfo cast)
        {
            if (cast.OthersRead) return cast.OthersStill;
            cast.OthersRead = true;
            float still = 1f;
            int hits = 0;
            foreach (var ally in ctx.Allies)
            {
                if (ally == null || ally == ctx.Unit) continue;
                try
                {
                    if (!(ally.Commands?.Standard is UnitAttack attack) || attack.IsFinished || attack.TargetUnit != enemy) continue;
                    if (!TryOrderedDelay(ally, attack, out float cooldown)) continue;
                    if (!TryAttackWindow(attack, out float window)) continue;   // no timing proof: do not discount our response
                    var est = CombatMath.EstimateWeaponAttack(ally, enemy, attack.IsAttackFull);
                    if (est == null) continue;
                    int attacks = attack.AllAttacks.Count;
                    float distance = Vector3.Distance(ally.Position, enemy.Position);
                    int next = attack.IsStarted ? attack.GetAttackIndex() : 0;
                    float walk = attack.IsStarted ? 0f : DisruptRules.OrderedStartSeconds(
                        BurstRules.WalkSeconds(HorizontalDistance(ally.Position, enemy.Position), attack.ApproachRadius,
                                               ally.CurrentSpeedMps), cooldown);
                    for (int k = next, counted = 0; k < attacks && counted < DisruptRules.MaxHits; k++, counted++)
                    {
                        var pending = attack.AllAttacks[k];
                        var pendingWeapon = pending?.Weapon;
                        if (pending == null || !CombatMath.TryOrderedHit(est, pendingWeapon, pending.AttackBonusPenalty,
                                                                         out float hit, out float damage)) continue;
                        bool ranged = pendingWeapon.Blueprint.IsRanged;
                        bool chained = OrderedChainedAttacks(attack, k);
                        float spacing = DisruptRules.SwingSpacing(attacks, chained, window,
                                                                  bow: ProspectiveChainedAttacks(pending?.Hand?.Weapon));
                        float at = attack.IsStarted
                            ? DisruptRules.RunningHitSeconds(k, next, spacing, attack.TimeSinceStart, chained, ranged, distance)
                            : DisruptRules.WeaponHitSeconds(k, walk, ranged, distance, spacing) - DisruptRules.CommandLatencySeconds;
                        if (!DisruptRules.InTime(at, cast.SecondsLeft)) continue;
                        if (!(hit > 0f)) continue;
                        still = DisruptRules.StillCasting(still, hit, DisruptRules.FailChance(cast.SpellLevel, damage, cast.Concentration));
                        hits++;
                    }
                }
                catch (Exception ex)
                {
                    Main.Verbose($"[Disrupt] reading {ally.CharacterName}'s ordered attack: {ex.Message}");
                }
            }
            cast.OthersStill = still;
            cast.OthersHits = hits;
            return still;
        }

        /// <summary>Cheap read-only native start gates for a queued attack, with the ordinary standard cooldown allowed to elapse
        /// during its approach. A running routine has paid its action: its own cooldown must not delay later iteratives.
        /// Unknown blockers (including a separate running move) prove no timely allied hit.</summary>
        private static bool TryOrderedDelay(UnitEntityData ally, UnitAttack attack, out float delay)
        {
            delay = 0f;
            if (!ally.IsInGame || ally.Descriptor?.State == null || !ally.Descriptor.State.CanAct) return false;
            if (attack.IsStarted) return true;
            if (!attack.CanStart || ally.Descriptor.State.HasCondition(UnitCondition.Nauseated)) return false;
            if (ally.AreHandsBusyWithAnimation && !attack.DontWaitForHands) return false;
            if (attack.AwaitMovementFinish && ally.View.MovementAgent.IsReallyMoving) return false;
            if (Kingmaker.Game.Instance.HandsEquipmentController.IsUpdateScheduledFor(ally)) return false;
            if (attack.Type == UnitCommand.CommandType.Standard &&
                ally.Descriptor.State.HasCondition(UnitCondition.CantUseStandardActions)) return false;
            if (!ally.IsInCombat || attack.IsIgnoreCooldown) return true;
            var state = ally.CombatState;
            if (state == null || !state.CanActInCombat) return false;
            if (!state.HasCooldownForCommand(attack)) return true;
            if (ally.Commands.GetCommand(UnitCommand.CommandType.Move)?.IsRunning == true) return false;
            delay = state.Cooldown.StandardAction;
            return delay > 0f && !float.IsInfinity(delay);
        }

        /// <summary>
        /// Before an attack command exists, only the bow shortcut is known. A sequence table is not a selected sequence:
        /// TryToReorder checks matching actions/styles/count and draws a random acceptance roll. Do not probe that roll.
        /// Other prospective attacks keep the ordinary native window as an explicit timing estimate.
        /// </summary>
        private static bool ProspectiveChainedAttacks(Kingmaker.Items.ItemEntityWeapon weapon)
        {
            try
            {
                return weapon != null && weapon.GetAnimationStyle() == WeaponAnimationStyle.Bow;
            }
            catch { return false; }
        }

        /// <summary>The actual OnTick shortcut at this read: bow, or both adjacent attacks selected as sequential with
        /// the previous animation released. Merely having sequences, or one selected attack, is insufficient.</summary>
        private static bool OrderedChainedAttacks(UnitAttack attack, int index)
        {
            var attacks = attack.AllAttacks;
            if (index < 0 || index >= attacks.Count) return false;
            var current = attacks[index];
            if (current == null) return false;
            if (ProspectiveChainedAttacks(current.Hand?.Weapon)) return true;
            if (index == 0 || !current.IsSequential) return false;
            var previous = attacks[index - 1];
            return previous != null && previous.IsSequential && previous.AnimationHandle?.IsReleased == true;
        }

        /// <summary>
        /// A damaging ability's bonus for breaking <paramref name="enemy"/>'s cast: one damage packet, landing after the walk to
        /// <paramref name="castOrigin"/>, the ability's own cast time and its delivery lag.
        /// </summary>
        private static float AbilityDisruptBonus(TacticalContext ctx, AbilityData ability, AbilityProfile profile, UnitEntityData enemy,
                                                 Vector3 castOrigin, float hitChance, float damage)
        {
            var cast = CastingFor(ctx, enemy);
            if (cast == null || !(damage > 0f)) return 0f;
            float walk = HorizontalDistance(ctx.Unit.Position, castOrigin) / Math.Max(BurstRules.MinWalkSpeed, ctx.Unit.CurrentSpeedMps);
            float lag = profile.DeliverDelaySeconds + FlightSecondsTo(profile, castOrigin, enemy.Position);
            // ★ coverage3 review: the command starts after its walk and the native wait for its action, whichever is later.
            walk = DisruptRules.OrderedStartSeconds(walk, ReadySeconds(ctx.Unit, ability));
            float at = DisruptRules.AbilityHitSeconds(walk, CastSecondsOf(ability, true), lag);
            float own = DisruptBonus(cast, hitChance, damage, at, 0f, 0f, float.MaxValue, out float chance);
            return AfterOrderedHits(ctx, enemy, cast, own, ref chance);   // ★ F2
        }
    }
}
