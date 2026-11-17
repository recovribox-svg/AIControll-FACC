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
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Commands;
using Kingmaker.UnitLogic.Commands.Base;
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
        }

        // Per running cast: its spell, parameters and stakes are read once (weak: the command goes when it ends).
        private static readonly ConditionalWeakTable<UnitCommand, CastInfo> CastInfos = new ConditionalWeakTable<UnitCommand, CastInfo>();
        private static int s_DisruptFight = int.MinValue, s_DisruptLines;
        private const int MaxDisruptLinesPerFight = 64;

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
            var cast = CastingFor(ctx, enemy);
            if (cast == null || est == null) return 0f;
            float distance = ctx.Distance(enemy);
            float at1 = DisruptRules.WeaponHitSeconds(0, walkSeconds, est.IsRanged, distance);
            float at2 = DisruptRules.WeaponHitSeconds(1, walkSeconds, est.IsRanged, distance);
            float bonus = DisruptBonus(cast, est.FirstHitChance, est.FirstPerHit, at1,
                                       full && est.Attacks > 1 ? est.SecondHitChance : 0f, est.SecondPerHit, at2, out float chance);
            if (bonus > 0f) tag = $" [breaks {cast.Spell.Name} {chance:P0}]";
            return bonus;
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
            float at = DisruptRules.AbilityHitSeconds(walk, CastSecondsOf(ability, true), lag);
            return DisruptBonus(cast, hitChance, damage, at, 0f, 0f, float.MaxValue, out _);
        }
    }
}
