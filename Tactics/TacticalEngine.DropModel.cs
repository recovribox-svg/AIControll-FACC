// ★ v0.3.30 competence3 (performance, competence2 logs): StandingDropChance read, for every enemy, every ally's hit weight,
// concealment and reach - and PartyShift asks it for every ally at every spot a walk is priced at, so one spot cost
// allies × enemies × allies reads. With the author's eleven-member party against 10-14 enemies that was ~1,500 reads a spot:
// real-time decisions went from a median of 25-35 ms (five members, v0.3.29) to 80-90 ms, Aid Another's touch approach
// alone 70-115 ms a decision (2.6 ms before). Everything there that does not depend on where the walker or the ally stands
// is now read once per decision (this table); a spot only re-places the two units that move. Same numbers as before.
using System;
using System.Collections.Generic;
using System.Linq;
using Kingmaker.Controllers.Combat;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic;
using UnityEngine;

namespace CompanionAI_Pathfinder.Tactics
{
    public static partial class TacticalEngine
    {
        /// <summary>One enemy's side of the drop model, for the allies of <see cref="DropTable.Allies"/> where they stand now.</summary>
        internal sealed class DropRow
        {
            public UnitEntityData Enemy;
            public bool Skip;
            public CombatMath.SwarmSpace Swarm;
            public bool Ranged;
            public float Zone, Reach = -1f;
            /// <summary>Per ally: how easily this enemy hits it (ValueModel.HitWeight past concealment and mirror images).</summary>
            public float[] Weight;
            public bool[] InReach, InZone;
            public float WeightSum, ReachSum, ZoneSum;
            /// <summary>CombatMath.Threat × the fight's danger scale.</summary>
            public float Threat;
        }

        internal sealed class DropTable
        {
            public List<UnitEntityData> Allies;
            public int AllyCount, EnemyCount;
            public Dictionary<UnitEntityData, int> Index;
            public float[] Corpulence;
            public DropRow[] Rows;

            public int IndexOf(UnitEntityData unit) => unit != null && Index.TryGetValue(unit, out int i) ? i : -1;
        }

        /// <summary>The decision's drop table, built on first use (and again if its lists changed meanwhile).</summary>
        private static DropTable DropTableOf(TacticalContext ctx)
        {
            var table = ctx.DropTable;
            if (table != null && ReferenceEquals(table.Allies, ctx.Allies) && table.AllyCount == ctx.Allies.Count &&
                table.EnemyCount == ctx.Enemies.Count)
                return table;
            int n = ctx.Allies.Count;
            table = new DropTable
            {
                Allies = ctx.Allies, AllyCount = n, EnemyCount = ctx.Enemies.Count,
                Index = new Dictionary<UnitEntityData, int>(n), Corpulence = new float[n], Rows = new DropRow[ctx.Enemies.Count],
            };
            var prone = new bool[n];
            var armor = new int[n];
            for (int i = 0; i < n; i++)
            {
                var a = ctx.Allies[i];
                if (a != null && !table.Index.ContainsKey(a)) table.Index[a] = i;
                try { table.Corpulence[i] = a?.Corpulence ?? 0f; } catch { table.Corpulence[i] = 0f; }
                try { prone[i] = a?.Descriptor?.State?.Prone?.Active ?? false; } catch { prone[i] = false; }
                try { armor[i] = a?.Stats.AC.ModifiedValue ?? 0; } catch { armor[i] = int.MinValue; }
            }
            for (int e = 0; e < ctx.Enemies.Count; e++)
            {
                var enemy = ctx.Enemies[e];
                var row = new DropRow { Enemy = enemy };
                table.Rows[e] = row;
                // It attacks nobody before it acts again.
                if (enemy == null || CombatMath.IsDisabled(enemy)) { row.Skip = true; continue; }
                // ★ v0.3.28 test 15 (author-approved): a swarm strikes everyone in its space, it picks nobody.
                row.Swarm = CombatMath.SwarmSpaceOf(enemy);
                if (row.Swarm != null) continue;
                int attack = 0;
                try
                {
                    row.Ranged = (enemy.Body?.PrimaryHand?.MaybeWeapon?.Blueprint?.IsRanged ?? false) ||
                                 (enemy.Descriptor?.Spellbooks?.Any() ?? false);
                    row.Zone = row.Ranged ? 20f : enemy.CurrentSpeedMps * 3f + 2f + enemy.Corpulence;
                    var weapon = row.Ranged ? null : enemy.GetThreatHandMelee()?.Weapon;
                    if (weapon != null) row.Reach = Math.Max(1f, weapon.AttackRange.Meters) + enemy.Corpulence + 0.5f;
                    attack = (enemy.Stats.BaseAttackBonus?.ModifiedValue ?? 0) +
                             Math.Max(enemy.Stats.Strength?.Bonus ?? 0, enemy.Stats.Dexterity?.Bonus ?? 0);
                }
                catch
                {
                    row.Ranged = false;
                    row.Zone = 11f;
                }
                row.Weight = new float[n];
                row.InReach = new bool[n];
                row.InZone = new bool[n];
                for (int i = 0; i < n; i++)
                {
                    var a = ctx.Allies[i];
                    float w;
                    // ★ v0.3.28 test 3: past concealment and mirror images (CombatMath.LandsPastDefences), and +4 in melee
                    // against a prone unit (the game's RuleCalculateAC; the weapon estimate reads that rule itself).
                    try
                    {
                        if (armor[i] == int.MinValue) throw new InvalidOperationException();
                        w = ValueModel.HitWeight(attack + (!row.Ranged && prone[i] ? 4 : 0), armor[i]) * CombatMath.LandsPastDefences(enemy, a);
                    }
                    catch { w = 0.5f; }
                    row.Weight[i] = w;
                    row.WeightSum += w;
                    if (a == null) continue;
                    var p = a.Position;
                    if (row.Reach > 0f && HorizontalDistance(enemy.Position, p) <= row.Reach + table.Corpulence[i])
                    {
                        row.InReach[i] = true;
                        row.ReachSum += w;
                    }
                    if (Vector3.Distance(enemy.Position, p) <= row.Zone + table.Corpulence[i])
                    {
                        row.InZone[i] = true;
                        row.ZoneSum += w;
                    }
                }
                row.Threat = CombatMath.Threat(enemy) * ctx.DangerScale;
            }
            ctx.DropTable = table;
            return table;
        }

        /// <summary>
        /// The weights of the enemy's reach and wider zone with ally <paramref name="i"/> moved to <paramref name="p"/>
        /// (it is taken out where it stands and put in again there). Whether it is in either, there.
        /// </summary>
        private static void Replace(DropTable table, DropRow row, int i, Vector3 p, ref float reachSum, ref float zoneSum,
                                    out bool inReach, out bool inZone)
        {
            inReach = inZone = false;
            if (i < 0) return;
            float w = row.Weight[i];
            if (row.InReach[i]) reachSum -= w;
            if (row.InZone[i]) zoneSum -= w;
            inReach = row.Reach > 0f && HorizontalDistance(row.Enemy.Position, p) <= row.Reach + table.Corpulence[i];
            inZone = Vector3.Distance(row.Enemy.Position, p) <= row.Zone + table.Corpulence[i];
            if (inReach) reachSum += w;
            if (inZone) zoneSum += w;
        }

        /// <summary>A sum of hit weights that only lost its members is empty, whatever the float rounding left of it.</summary>
        private static float Settled(float sum) => sum < 1e-6f ? 0f : sum;
    }
}
