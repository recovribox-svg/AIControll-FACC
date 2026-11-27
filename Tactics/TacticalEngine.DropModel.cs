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
            /// <summary>
            /// ★ features5 review (performance): what StrikeShareAt and FullStrikeLands read of this enemy on every call -
            /// its melee weapon's reach and body, whether it lies down (turn-based), whether its full attack can land - read
            /// once (<see cref="RowStrikeShare"/>). <see cref="StrikeUnread"/>: the reading failed (a share of 1, no full
            /// attack, as those rules returned on a failed read).
            /// </summary>
            public byte Strike;
            public float MeleeBase;
            public bool StrikeProne, FullOk;
            /// <summary>HeldThisRound and SingleAttackShare as first asked (both once a frame already).</summary>
            public float Held = float.NaN, Single = float.NaN;
            /// <summary>Per ally: its strike share where it stands (NaN until read). PartyShift asks it at every spot priced.</summary>
            public float[] OwnShare;
        }

        private const byte StrikeUnread = 0, StrikeNoWeapon = 1, StrikeMelee = 2;

        // ★ features5 review: drop-chance counters for the slow-decision row (reset per decision). Measurement only.
        private static int s_DropReads, s_PartyReads, s_PartySkips;
        private static long s_DropTicks;

        private static string DropNote()
        {
            if (s_DropReads == 0) return "";
            double f = 1000d / System.Diagnostics.Stopwatch.Frequency;
            return $"; drop chances read {s_DropReads} ({s_DropTicks * f:F1} ms), allies re-read for walks {s_PartyReads} " +
                   $"(unchanged, skipped {s_PartySkips})";
        }

        internal sealed class DropTable
        {
            public List<UnitEntityData> Allies;
            public int AllyCount, EnemyCount;
            public Dictionary<UnitEntityData, int> Index;
            public float[] Corpulence;
            public DropRow[] Rows;
            /// <summary>★ features5 review: a swarm row reads where the walker stands (SwarmDrop): PartyShift reads every ally.</summary>
            public bool HasSwarm;
            /// <summary>★ features5 review: PartyShift's rows whose choice the walk changes, and StandingDropChance's lists.</summary>
            public readonly List<int> Changed = new List<int>();
            public readonly List<(float chance, float drop)> Threats = new List<(float chance, float drop)>();
            public readonly List<(UnitEntityData, float)> Heavy = new List<(UnitEntityData, float)>();
            public readonly List<int> HeavyAt = new List<int>();
            public int Depth;

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
                if (row.Swarm != null) { table.HasSwarm = true; continue; }
                ReadStrike(row, enemy);
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

        /// <summary>
        /// ★ features5 review (performance): the melee data StrikeShareAt and FullStrikeLands read on every call, read once
        /// for the decision. Each read separately and failing as those rules fail: no share read (1), no full attack.
        /// </summary>
        private static void ReadStrike(DropRow row, UnitEntityData enemy)
        {
            try
            {
                var melee = enemy.GetThreatHandMelee()?.Weapon;
                if (melee == null) row.Strike = StrikeNoWeapon;
                else
                {
                    row.MeleeBase = Math.Max(1f, melee.AttackRange.Meters) + enemy.Corpulence;
                    row.StrikeProne = !CombatMemory.IsRealTime && (enemy.Descriptor?.State?.Prone?.Active ?? false);
                    row.Strike = StrikeMelee;
                }
            }
            catch { row.Strike = StrikeUnread; }
            try { row.FullOk = row.Strike == StrikeMelee && !(enemy.Body?.PrimaryHand?.MaybeWeapon?.Blueprint?.IsRanged ?? false); }
            catch { row.FullOk = false; }
        }

        /// <summary>
        /// ★ features5 review: <see cref="StrikeShareAt"/> from the row's data - the same rule (ValueModel.StrikeShare: in
        /// reach or a 5-foot step of it, else one attack after a walk, each unless an area holds it; turn-based, lying down,
        /// one attack in reach). 1 when it cannot be read.
        /// </summary>
        private static float RowStrikeShare(DropRow row, UnitEntityData who, Vector3 at, float whoCorpulence)
        {
            if (row.Strike != StrikeMelee) return 1f;
            try
            {
                float reach = row.MeleeBase + whoCorpulence;
                float distance = HorizontalDistance(row.Enemy.Position, at);
                bool inReach = distance <= reach;
                if (row.StrikeProne) return inReach ? RowSingle(row, who) : 0f;
                if (inReach) return 1f;
                bool inStep = distance <= reach + TurnBased.Controllers.TurnController.MetersOfFiveFootStep;
                if (float.IsNaN(row.Held)) row.Held = HeldThisRound(row.Enemy);
                return ValueModel.StrikeShare(false, inStep, row.Held, RowSingle(row, who));
            }
            catch
            {
                return 1f;
            }
        }

        private static float RowSingle(DropRow row, UnitEntityData who)
        {
            if (float.IsNaN(row.Single)) row.Single = SingleAttackShare(row.Enemy, who);
            return row.Single;
        }

        /// <summary>
        /// ★ features5 review: the ally's strike share where it stands, once per decision (PartyShift asks it at every spot a
        /// walk is priced at: allies × enemies native reads a spot before).
        /// </summary>
        private static float OwnStrikeShare(DropTable table, DropRow row, int index, UnitEntityData who, Vector3 at, float whoCorpulence)
        {
            if (row.OwnShare == null)
            {
                row.OwnShare = new float[table.AllyCount];
                for (int i = 0; i < row.OwnShare.Length; i++) row.OwnShare[i] = float.NaN;
            }
            float share = row.OwnShare[index];
            if (float.IsNaN(share)) row.OwnShare[index] = share = RowStrikeShare(row, who, at, whoCorpulence);
            return share;
        }

        /// <summary>
        /// ★ features5 review (performance): the rows whose choice of target a walk of <paramref name="mover"/> to
        /// <paramref name="to"/> changes - the enemies whose reach or wider zone it leaves or enters. An ally in neither of
        /// those enemies' reach or zone has the same chance to drop before and after (<see cref="StandingDropChance"/> differs
        /// only through those rows' weight sums). Null when a swarm is in the fight: its row reads where the walker stands.
        /// </summary>
        private static List<int> MoverChangedRows(DropTable table, UnitEntityData mover, Vector3 to)
        {
            if (table.HasSwarm) return null;
            var changed = table.Changed;
            changed.Clear();
            int m = table.IndexOf(mover);
            if (m < 0) return changed;
            float body = table.Corpulence[m];
            for (int r = 0; r < table.Rows.Length; r++)
            {
                var row = table.Rows[r];
                if (row.Skip || row.Swarm != null) continue;
                bool inReach = row.Reach > 0f && HorizontalDistance(row.Enemy.Position, to) <= row.Reach + body;
                bool inZone = Vector3.Distance(row.Enemy.Position, to) <= row.Zone + body;
                if (inReach != row.InReach[m] || inZone != row.InZone[m]) changed.Add(r);
            }
            return changed;
        }

        /// <summary>The ally (table index) can be picked by one of the <paramref name="changed"/> rows (in its reach or zone).</summary>
        private static bool ChangeTouches(DropTable table, List<int> changed, int index)
        {
            if (index < 0) return false;
            foreach (int r in changed)
            {
                var row = table.Rows[r];
                if (row.InReach[index] || row.InZone[index]) return true;
            }
            return false;
        }
    }
}
