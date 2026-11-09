// ★ v0.3.29 test 2 (author-approved, R3): the chance of going down at a spot counts what melee enemies whose full attack
// reaches it really strike - their hits, damage per hit and critical hits (RiskMath.FullAttackDropChance) - next to the
// engine's exp(-HP / danger) model, and takes the larger. The exponential model works from a mean: the enemy's Threat
// scaled by the fight's calibration (ValueModel.DangerScale, 0.25-0.67 by the end of the test-15 fights). A heavy hitter's
// slam does not shrink with that scale: late in a fight a 14-damage slam on a 17-HP companion read about 10 % where it is
// near 25 %, and near 50 % with a mongrel's 5 on top. Only melee enemies whose full attack lands there (in reach, or a 5-foot
// step of it: StrikeShareAt), each read once a round per party member (EstimateWeaponAttack, the game's attack rules).
using System;
using System.Collections.Generic;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic;
using UnityEngine;

namespace CompanionAI_Pathfinder.Tactics
{
    public static partial class TacticalEngine
    {
        /// <summary>A melee enemy's full attack on one party member, as a drop-chance reading needs it.</summary>
        private sealed class StrikeProfile
        {
            public int Attacks;
            public float Hit, Crit, PerHit, CritMultiplier, Expected;
        }

        private static readonly Dictionary<(UnitEntityData, UnitEntityData), (int fight, int round, StrikeProfile profile)> s_Strikes =
            new Dictionary<(UnitEntityData, UnitEntityData), (int, int, StrikeProfile)>();

        /// <summary>The game's attack rules run for <see cref="StrikeOf"/> this decision, and how long they took (the
        /// slow-decision line: what R3 costs).</summary>
        private static int s_StrikeReads;
        private static long s_StrikeReadTicks;

        /// <summary>
        /// <paramref name="enemy"/>'s melee full attack on <paramref name="victim"/>, once a round per pair (the game's
        /// attack rules run once, not for every spot priced); null for a ranged weapon or no attack.
        /// </summary>
        private static StrikeProfile StrikeOf(UnitEntityData enemy, UnitEntityData victim)
        {
            int fight = CombatMemory.Fight, round = CombatMemory.Round;
            var key = (enemy, victim);
            if (s_Strikes.TryGetValue(key, out var known) && known.fight == fight && known.round == round) return known.profile;
            StrikeProfile profile = null;
            long started = System.Diagnostics.Stopwatch.GetTimestamp();
            try { profile = StrikeProfileOf(CombatMath.EstimateWeaponAttack(enemy, victim, full: true)); }
            catch { }
            s_StrikeReads++;
            s_StrikeReadTicks += System.Diagnostics.Stopwatch.GetTimestamp() - started;
            if (s_Strikes.Count > 512) s_Strikes.Clear();
            s_Strikes[key] = (fight, round, profile);
            return profile;
        }

        /// <summary>
        /// The routine as n copies of its first attack: what the later attacks fall short of it (iterative attacks hit less
        /// often) lowers the hit chance, what they add (sneak attack, a heavier natural weapon) raises the damage per hit.
        /// </summary>
        private static StrikeProfile StrikeProfileOf(WeaponAttackEstimate est)
        {
            if (est == null || est.IsRanged || est.Attacks <= 0 || !(est.FirstPerHit > 0f) || !(est.FirstHitChance > 0f)) return null;
            float m = Math.Max(1f, est.CritMultiplier);
            float first = est.FirstPerHit * (est.FirstHitChance + (m - 1f) * est.FirstCritChance);
            float scale = first > 0f ? est.ExpectedDamage / (est.Attacks * first) : 1f;
            float hitScale = Mathf.Clamp(scale, 0.2f, 1f), damageScale = Mathf.Clamp(scale, 1f, 3f);
            float hit = Math.Min(0.95f, est.FirstHitChance * hitScale);
            return new StrikeProfile
            {
                Attacks = est.Attacks,
                Hit = hit,
                Crit = Math.Min(hit, est.FirstCritChance * hitScale),
                PerHit = est.FirstPerHit * damageScale,
                CritMultiplier = m,
                Expected = Math.Max(0f, est.ExpectedDamage)
            };
        }

        /// <summary>
        /// The drop chance of each melee enemy in <paramref name="reaching"/> (full attack on <paramref name="who"/>, chosen
        /// with its chance) from its real hits, against the HP left after what the others there are expected to deal first
        /// (two hitters of 5 and 14 on 17 HP). Indexed like <paramref name="reaching"/>; 0 where no profile reads.
        /// </summary>
        private static float[] HeavyHitDrops(List<(UnitEntityData enemy, float chance)> reaching, UnitEntityData who, float hp)
        {
            var drops = new float[reaching.Count];
            if (reaching.Count == 0) return drops;
            float expectedAll = 0f;
            for (int i = 0; i < reaching.Count; i++)
            {
                var p = StrikeOf(reaching[i].enemy, who);
                if (p != null) expectedAll += reaching[i].chance * p.Expected;
            }
            for (int i = 0; i < reaching.Count; i++)
            {
                var p = StrikeOf(reaching[i].enemy, who);   // read above: the round's cache
                if (p == null) continue;
                float others = expectedAll - reaching[i].chance * p.Expected;
                drops[i] = RiskMath.FullAttackDropChance(p.Attacks, p.Hit, p.Crit, p.PerHit, p.CritMultiplier, Math.Max(1f, hp - others));
            }
            return drops;
        }

        /// <summary>The enemy's melee full attack lands on <paramref name="who"/> at <paramref name="at"/> (in its reach, or a
        /// free 5-foot step of it: StrikeShareAt); not for a ranged weapon, a disabled enemy or a swarm.</summary>
        private static bool FullStrikeLands(UnitEntityData enemy, UnitEntityData who, Vector3 at)
        {
            if (CombatMath.IsDisabled(enemy) || CombatMath.SwarmSpaceOf(enemy) != null) return false;
            try
            {
                if (enemy.GetThreatHandMelee()?.Weapon == null) return false;
                if (enemy.Body?.PrimaryHand?.MaybeWeapon?.Blueprint?.IsRanged ?? false) return false;
            }
            catch { return false; }
            return StrikeShareAt(enemy, who, at) >= 0.999f;
        }

        /// <summary>
        /// The chance <paramref name="who"/> goes down before its next turn standing at <paramref name="at"/> by the
        /// IncomingDamage model (exposure, withdrawal and step-out prices): exp(-HP / danger), or what the melee enemies whose
        /// full attack reaches it strike from their real hits, whichever is larger - each enemy's share of it split among the
        /// party members in its reach, as IncomingParts splits its danger.
        /// </summary>
        private static float SpotDrop(TacticalContext ctx, UnitEntityData who, Vector3 at)
        {
            float hp = Math.Max(1f, CombatMath.HpBeforeDown(who));
            float drop = RiskMath.RoundDropChance(hp, IncomingDamage(ctx, who, at));
            try
            {
                List<(UnitEntityData, float)> reaching = null;
                foreach (var enemy in ctx.Enemies)
                {
                    if (!FullStrikeLands(enemy, who, at)) continue;
                    int sharedBy = 1;
                    foreach (var a in ctx.Allies)
                        if (a != who && a?.Descriptor?.State?.IsConscious == true && Threatens(enemy, a, a.Position)) sharedBy++;
                    (reaching ?? (reaching = new List<(UnitEntityData, float)>())).Add((enemy, 1f / sharedBy));
                }
                if (reaching == null) return drop;
                var drops = HeavyHitDrops(reaching, who, hp);
                double stays = 1d;
                for (int i = 0; i < drops.Length; i++) stays *= 1d - reaching[i].Item2 * drops[i];
                return Math.Max(drop, (float)(1d - stays));
            }
            catch (Exception ex)
            {
                Main.Verbose($"[Tactics] heavy-hit drop {who.CharacterName}: {ex.Message}");
                return drop;
            }
        }
    }
}
