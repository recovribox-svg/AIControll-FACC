// ★ v0.3.28 test 12 (author's request: cones that catch frontline allies): a projectile cone aimed straight at an enemy
// caught the allies fighting beside it. The test 11 log: three of Orvath's four Fan of Flames hit Seelah or Camellia (26
// damage to Seelah on the first). A cone can be aimed at a point (AbilityDeliverProjectile delivers towards the point's
// direction from the caster, CorrectPosition), so when the straight aim catches allies a few turned aims are tried, each
// read with the game's own recipient query (ProjectileCone: WouldTargetUnitCone, the 45-degree edges, sight, Selective),
// and the one that keeps the aimed enemy while catching the fewest allies is scored like any other aim; the better of the
// two is offered. Only from where the caster stands: a cone that needs a walk first walks there and is aimed on the next
// decision (MoveFirst), when the spot it starts from is known.
using System;
using System.Collections.Generic;
using System.Linq;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.Utility;
using UnityEngine;

namespace CompanionAI_Pathfinder.Tactics
{
    public static partial class TacticalEngine
    {
        /// <summary>Turns of the cone's axis tried, in degrees (the cone is 90 degrees wide: the aimed enemy stays inside).</summary>
        private static readonly float[] ConeTurns = { 12f, -12f, 24f, -24f, 36f, -36f };

        /// <summary>
        /// A point aim for <paramref name="ability"/> (a projectile cone that can be aimed at the ground) turned away from the
        /// allies its straight aim at <paramref name="target"/> catches, from where the caster stands; null when the straight
        /// aim catches none, no turned aim keeps the target and catches fewer, or the ability cannot be aimed at a point.
        /// </summary>
        private static TargetWrapper ConeAimPastAllies(TacticalContext ctx, AbilityData ability, AbilityProfile profile,
            UnitEntityData target, IReadOnlyCollection<UnitEntityData> directRecipients, out string note)
        {
            note = null;
            var unit = ctx.Unit;
            if (target == null || directRecipients == null || !profile.IsProjectileCone || !ability.CanTargetPoint) return null;
            var allies = ctx.Allies.Concat(ctx.DownedAllies).Where(a => a != unit).Distinct().ToList();
            var caughtNow = allies.Where(directRecipients.Contains).ToList();
            if (caughtNow.Count == 0) return null;
            try
            {
                Vector3 origin = unit.Position;
                Vector3 axis = target.Position - origin;
                axis.y = 0f;
                float along = axis.magnitude;
                if (along < 0.5f) return null;
                axis /= along;
                var pool = ctx.Enemies.Concat(allies).Distinct().ToList();
                TargetWrapper best = null;
                int bestAllies = caughtNow.Count, bestEnemies = -1;
                float bestTurn = 0f;
                List<UnitEntityData> bestSpared = null;
                foreach (float turn in ConeTurns)
                {
                    float rad = turn * Mathf.Deg2Rad, cos = Mathf.Cos(rad), sin = Mathf.Sin(rad);
                    var dir = new Vector3(axis.x * cos + axis.z * sin, 0f, axis.z * cos - axis.x * sin);   // about the vertical
                    var point = new TargetWrapper(origin + dir * Math.Max(1f, Math.Min(along, 3f)));
                    if (!ability.CanTarget(point)) continue;
                    if (!ProjectileCone.TryCreate(ability, point, origin, out var cone) || !cone.TryRecipients(pool, out var reached)) continue;
                    if (!reached.Contains(target)) continue;
                    int caught = reached.Count(allies.Contains);
                    int enemies = reached.Count(ctx.Enemies.Contains);
                    bool better = caught < bestAllies || caught == bestAllies && best != null &&
                                  (enemies > bestEnemies || enemies == bestEnemies && Math.Abs(turn) < Math.Abs(bestTurn));
                    if (!better) continue;
                    best = point;
                    bestAllies = caught;
                    bestEnemies = enemies;
                    bestTurn = turn;
                    bestSpared = caughtNow.Where(a => !reached.Contains(a)).ToList();
                }
                if (best == null) return null;
                note = $"turned {bestTurn:+0;-0} deg past {string.Join(", ", bestSpared.Select(a => a.CharacterName))}";
                return best;
            }
            catch (Exception ex)
            {
                ReportOnce($"cone-aim|{ability.Blueprint.AssetGuid}|{ex.GetType().Name}",
                    $"[Cone] {ability.Name}: turned aims unavailable ({ex.GetType().Name}: {ex.Message}); the straight aim stays");
                return null;
            }
        }
    }
}
