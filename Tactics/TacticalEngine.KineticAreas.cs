// ★ v0.3.22: lasting blast damage uses the same short horizon as lasting control, bounded by lifetime and escape.
using System;
using System.Collections.Generic;
using System.Linq;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic.Class.Kineticist;
using Kingmaker.Utility;
using UnityEngine;

namespace CompanionAI_Pathfinder.Tactics
{
    public static partial class TacticalEngine
    {
        private static IEnumerable<(UnitEntityData Target, TargetWrapper Aim)> KineticAims(TacticalContext ctx,
            AbilityData ability, AbilityProfile profile, UnitEntityData target, Vector3? summon)
        {
            var original = summon.HasValue ? new TargetWrapper(summon.Value) : AbilityProfile.TargetOn(ability, target);
            yield return (target, original);
            if (original == null || original.Unit != null || !IsOwnBlast(ctx.Unit.Get<UnitPartKineticist>(), ability) ||
                !profile.SpawnedAreas.Any(a => a.Shape == AreaEffectShape.Wall)) yield break;
            // The native wall's length is on its local X axis. Try alignments through the aim and other enemies;
            // every orientation is scored by the same native shape/target/friendly-fire path.
            var angles = new HashSet<int> { (int)(original.Orientation * 10f) };
            foreach (var other in ctx.Enemies.Where(e => e != target))
            {
                var direction = other.Position - original.Point;
                direction.y = 0f;
                if (direction.sqrMagnitude <= 0f) continue;
                float angle = Quaternion.LookRotation(direction).eulerAngles.y + 90f;
                if (angles.Add((int)(angle * 10f))) yield return (target, new TargetWrapper(original.Point, angle, null));
            }
        }

        private static float KineticAreaTicks(TacticalContext ctx, AbilityData ability, AbilityProfile profile, UnitEntityData target)
        {
            if (!profile.PeriodicAreas.Any() || (!profile.IsKineticEffect && !IsOwnBlast(ctx.Unit.Get<UnitPartKineticist>(), ability)))
                return 1f;
            var context = CombatMath.CreateContext(ability, AbilityProfile.CurrentAim ?? new TargetWrapper(target));
            float lifetime = profile.AreaDurations.Values.Select(d => DurationRounds(d, ability, context))
                .Where(d => d > 0f).DefaultIfEmpty(1f).Min();
            float horizon = Math.Max(1f, Math.Min(lifetime, Math.Min(ctx.ExpectedRounds, 3f)));
            // Mobile targets are assumed to leave by the shortest native-shape exit. Do not grant a whole-fight
            // stream of damage merely because they are standing inside at placement time.
            return KineticAreaPreview.StayTicks(profile, target, horizon);
        }
    }
}
