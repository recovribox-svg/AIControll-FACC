// ★ Fusion coverage C4 (author request; docs/FUSION_COVERAGE_REST.md): native line recipient queries (Lightning Bolt, Acidic
// Spray, Fire Snake, and the fusions delivered by them), scoped to one scored aim, as ProjectileCone is for cones.
// Native contract (AbilityDeliverProjectile, supplied assembly): Deliver casts from the caster's eye, moved forward by its body
// (IsPierceOrCone); the projectile flies to that point plus the direction times Length, at most its MaxRange
// (GetProjectileRange; 0 = no limit); StepBasedDeliver hands every unit that is not dead and not the caster to the payload once,
// when WouldTargetUnitLine holds: CanTargetUnit (Selective) and WouldTargetPointLine - in front of the launch point, before the
// distance plus the body, within half the line's width plus the body to either side, flat. No sight test: a line goes through
// walls unless its projectile follows the terrain (FollowTerrain: stopped by an obstacle), which this reader does not take.
// No geometry is copied: the native predicates are bound and called. No Harmony hook, actor mutation or persistent context cache.
using System;
using System.Collections.Generic;
using System.Reflection;
using Kingmaker.Blueprints;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic.Abilities.Components;
using Kingmaker.Utility;
using UnityEngine;

namespace CompanionAI_Pathfinder.Tactics
{
    /// <summary>A native projectile shape's recipients for one aim: a cone (<see cref="ProjectileCone"/>) or a line.</summary>
    internal interface IProjectileArea
    {
        bool IsValid { get; }
        /// <summary>Would be caught standing at <paramref name="at"/> (a walk not finished yet); never invalidates the aim.</summary>
        bool ReachesAt(UnitEntityData recipient, Vector3 at);
        /// <summary>All-or-none selection; a failed query cannot silently drop a recipient.</summary>
        bool TryRecipients(IEnumerable<UnitEntityData> pool, out List<UnitEntityData> reached);
    }

    internal static class ProjectileArea
    {
        /// <summary>The native recipient query of <paramref name="profile"/>'s projectile shape, for this aim from <paramref name="origin"/>.</summary>
        public static bool TryCreate(AbilityProfile profile, AbilityData ability, TargetWrapper aim, Vector3 origin, out IProjectileArea area)
        {
            area = null;
            if (profile == null) return false;
            if (profile.IsProjectileCone)
            {
                if (!ProjectileCone.TryCreate(ability, aim, origin, out var cone)) return false;
                area = cone;
                return true;
            }
            if (profile.IsProjectileLine)
            {
                if (!ProjectileLine.TryCreate(ability, aim, origin, out var line)) return false;
                area = line;
                return true;
            }
            return false;
        }
    }

    internal static class ProjectileLine
    {
        private delegate bool LineTarget(AbilityDeliverProjectile projectile, AbilityExecutionContext context, UnitEntityData unit,
            Vector3 launchPosition, Vector2 direction, float distance);
        private delegate bool LinePoint(AbilityDeliverProjectile projectile, Vector3 targetPosition, float targetRadius,
            Vector3 launchPosition, Vector2 direction, float distance);
        private delegate float ProjectileRange(AbilityDeliverProjectile projectile, AbilityExecutionContext context, bool reach);
        private delegate bool CanTarget(AbilityDeliverProjectile projectile, AbilityExecutionContext context, UnitEntityData unit);

        // Only blueprint components and API delegates survive an aim. Null caches a blueprint this reader does not take.
        private static readonly Dictionary<BlueprintAbility, AbilityDeliverProjectile> Components =
            new Dictionary<BlueprintAbility, AbilityDeliverProjectile>();
        private static readonly HashSet<string> ReportedFailures = new HashSet<string>();
        private static LineTarget s_Target;
        private static LinePoint s_Point;
        private static ProjectileRange s_Range;
        private static CanTarget s_CanTarget;
        private static bool s_BindingsSought;

        /// <summary>
        /// The line delivery this reader takes: the ordinary native class, one projectile that does not follow the terrain, no
        /// controlled projectile (it launches from where an earlier projectile is) and no projectile count from a rank.
        /// </summary>
        internal static bool Supported(AbilityDeliverProjectile component, out string why)
        {
            why = null;
            if (component == null || component.Type != AbilityProjectileType.Line) { why = "not a line"; return false; }
            if (component.GetType() != typeof(AbilityDeliverProjectile)) { why = "custom line delivery " + component.GetType().Name; return false; }
            if (component.ControlledProjectileHolderBuff != null) { why = "controlled projectile"; return false; }
            if (component.UseMaxProjectilesCount) { why = "projectile count from a rank"; return false; }
            var projectiles = component.Projectiles;
            if (projectiles.Length != 1) { why = $"{projectiles.Length} projectiles"; return false; }
            foreach (var projectile in projectiles)
            {
                if (projectile == null) { why = "projectile blueprint unavailable"; return false; }
                if (projectile.FollowTerrain) { why = "projectile stopped by obstacles (FollowTerrain)"; return false; }
            }
            if (!(component.Length.Meters > 0f) || !(component.LineWidth.Meters > 0f)) { why = "no length or width"; return false; }
            return true;
        }

        public static bool IsLine(BlueprintAbility blueprint) => Component(blueprint) != null;

        /// <summary>The selected delivery owns the footprint (a fusion's deliver spell); cache components, never actors.</summary>
        internal static AbilityDeliverProjectile DeliveryComponent(AbilityData ability, out BlueprintAbility blueprint)
        {
            blueprint = ability?.GetDeliverBlueprint() ?? ability?.Blueprint;
            return Component(blueprint);
        }

        private static AbilityDeliverProjectile Component(BlueprintAbility blueprint)
        {
            if (blueprint == null) return null;
            if (Components.TryGetValue(blueprint, out var component)) return component;
            component = blueprint.GetComponent<AbilityDeliverProjectile>();
            if (!Supported(component, out _)) component = null;
            Components[blueprint] = component;
            return component;
        }

        private static bool BindQueries()
        {
            if (s_BindingsSought) return s_Target != null && s_Point != null && s_Range != null && s_CanTarget != null;
            s_BindingsSought = true;
            try
            {
                var type = typeof(AbilityDeliverProjectile);
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var target = type.GetMethod("WouldTargetUnitLine", flags, null, new[]
                {
                    typeof(AbilityExecutionContext), typeof(UnitEntityData), typeof(Vector3), typeof(Vector2), typeof(float)
                }, null);
                var point = type.GetMethod("WouldTargetPointLine", flags, null, new[]
                {
                    typeof(Vector3), typeof(float), typeof(Vector3), typeof(Vector2), typeof(float)
                }, null);
                var range = type.GetMethod("GetProjectileRange", flags, null,
                    new[] { typeof(AbilityExecutionContext), typeof(bool) }, null);
                var canTarget = type.GetMethod("CanTargetUnit", flags, null,
                    new[] { typeof(AbilityExecutionContext), typeof(UnitEntityData) }, null);
                if (target == null || point == null || range == null || canTarget == null ||
                    target.ContainsGenericParameters || point.ContainsGenericParameters || range.ContainsGenericParameters)
                    throw new MissingMethodException("Native line/range query was not found");
                // Ordinary read queries; binding them does not replace or intercept any game method.
                s_Target = (LineTarget)Delegate.CreateDelegate(typeof(LineTarget), target);
                s_Point = (LinePoint)Delegate.CreateDelegate(typeof(LinePoint), point);
                s_Range = (ProjectileRange)Delegate.CreateDelegate(typeof(ProjectileRange), range);
                s_CanTarget = (CanTarget)Delegate.CreateDelegate(typeof(CanTarget), canTarget);
            }
            catch (Exception ex)
            {
                s_Target = null;
                s_Point = null;
                s_Range = null;
                s_CanTarget = null;
                Report("bindings", ex);
            }
            return s_Target != null && s_Point != null && s_Range != null && s_CanTarget != null;
        }

        /// <summary>
        /// The line's travel: its Length, cut at the projectile's native MaxRange when that is set (Projectile.Tick: a
        /// projectile stops at its target point or once it has passed MaxRange; 0 means no limit).
        /// </summary>
        internal static float Travel(float length, float maxRange) => maxRange > 0f && maxRange < length ? maxRange : length;

        /// <summary>One real context for this aim, reused only while querying its recipients. The native predicates own
        /// Selective, the body and the line's width and length.</summary>
        public static bool TryCreate(AbilityData ability, TargetWrapper actualAim, Vector3 plannedGroundOrigin, out Query query)
        {
            query = null;
            BlueprintAbility blueprint = null;
            try
            {
                var component = DeliveryComponent(ability, out blueprint);
                if (component == null || actualAim == null || ability.Caster?.Unit == null || !BindQueries()) return false;
                var caster = ability.Caster.Unit;
                // Deliver: castPosition = the caster's eye; the direction is flat, from the eye to the aimed point (a unit's
                // position, or a point moved to 0.3 m from the caster along the same direction: CorrectPosition).
                Vector3 eye = plannedGroundOrigin + (caster.EyePosition - caster.Position);
                Vector3 direction = actualAim.Point - eye;
                direction.y = 0f;
                // Aimed at its own caster a line has no direction: natively its point test is never in front (along = 0),
                // so it catches nobody. Not a reader failure.
                if (Finite(direction) && !(direction.sqrMagnitude > 1e-6f)) return false;
                if (!Finite(eye) || !Finite(direction))
                    throw new InvalidOperationException("Line aim/origin has no finite direction");
                direction.Normalize();
                Vector3 launch = eye + direction * caster.Corpulence;
                var context = CombatMath.CreateContext(ability, actualAim);
                if (context == null) throw new InvalidOperationException("Native line context could not be created");
                float maxRange = s_Range(component, context, context.HasMetamagic(Metamagic.Reach));
                float distance = Travel(component.Length.Meters, maxRange);
                if (!(distance > 0f) || float.IsInfinity(distance) || !Finite(launch))
                    throw new InvalidOperationException("Native line length/origin is invalid");
                query = new Query(component, context, caster, plannedGroundOrigin, launch,
                    new Vector2(direction.x, direction.z), distance, component.LineWidth.Meters * 0.5f, blueprint.AssetGuid.ToString());
                return true;
            }
            catch (Exception ex)
            {
                Report(blueprint?.AssetGuid.ToString() ?? "unknown", ex);
                return false;
            }
        }

        /// <summary>The line's dimensions for this ability (half its width, its travel), without an aim; false when unread.</summary>
        internal static bool TryShape(AbilityData ability, out float halfWidth, out float travel)
        {
            halfWidth = travel = 0f;
            try
            {
                var component = DeliveryComponent(ability, out _);
                if (component == null || ability.Caster?.Unit == null || !BindQueries()) return false;
                var context = CombatMath.CreateContext(ability, new TargetWrapper(ability.Caster.Unit));
                if (context == null) return false;
                halfWidth = component.LineWidth.Meters * 0.5f;
                travel = Travel(component.Length.Meters, s_Range(component, context, context.HasMetamagic(Metamagic.Reach)));
                return halfWidth > 0f && travel > 0f && !float.IsInfinity(travel);
            }
            catch { return false; }
        }

        private static bool Finite(Vector3 point) =>
            !float.IsNaN(point.x) && !float.IsInfinity(point.x) &&
            !float.IsNaN(point.y) && !float.IsInfinity(point.y) &&
            !float.IsNaN(point.z) && !float.IsInfinity(point.z);

        private static void Report(string key, Exception ex)
        {
            if (ReportedFailures.Add(key + "|" + ex.GetType().Name))
                Main.Warning($"[Line] native recipient query unavailable ({key}, {ex.GetType().Name}: {ex.Message}); line aim omitted");
        }

        internal sealed class Query : IProjectileArea
        {
            private readonly AbilityDeliverProjectile m_Projectile;
            private readonly AbilityExecutionContext m_Context;
            private readonly UnitEntityData m_Caster;
            private readonly string m_BlueprintKey;

            /// <summary>The planned ground casting position, before the native eye/body launch offset.</summary>
            public Vector3 Origin { get; }
            public Vector3 Launch { get; }
            public Vector2 Direction { get; }
            public float Range { get; }
            public float HalfWidth { get; }
            public bool IsValid { get; private set; } = true;

            internal Query(AbilityDeliverProjectile projectile, AbilityExecutionContext context, UnitEntityData caster, Vector3 origin,
                Vector3 launch, Vector2 direction, float range, float halfWidth, string blueprintKey)
            {
                m_Projectile = projectile;
                m_Context = context;
                m_Caster = caster;
                Origin = origin;
                Launch = launch;
                Direction = direction;
                Range = range;
                HalfWidth = halfWidth;
                m_BlueprintKey = blueprintKey;
            }

            /// <summary>The native target rule alone (Selective): whether the line can affect the unit wherever it stands.</summary>
            public bool CanAffect(UnitEntityData unit)
            {
                if (!IsValid || unit == null || ReferenceEquals(unit, m_Caster)) return false;
                try { return s_CanTarget(m_Projectile, m_Context, unit); }
                catch { return false; }
            }

            /// <summary>Includes unconscious recipients like native delivery. Any native failure invalidates this whole aim.</summary>
            public bool Reaches(UnitEntityData recipient)
            {
                if (!IsValid) throw new InvalidOperationException("Line recipient query was already invalidated");
                if (recipient == null || ReferenceEquals(recipient, m_Caster)) return false;
                try
                {
                    if (recipient.Descriptor?.State == null)
                        throw new InvalidOperationException("Line recipient state is unavailable");
                    if (recipient.Descriptor.State.IsDead) return false;
                    return s_Target(m_Projectile, m_Context, recipient, Launch, Direction, Range);
                }
                catch (Exception ex)
                {
                    IsValid = false;
                    Report(m_BlueprintKey, ex);
                    throw new InvalidOperationException("Native line recipient query failed; discard this aim", ex);
                }
            }

            /// <summary>The native target rule and point test at <paramref name="at"/>, the recipient's eye height carried over.</summary>
            public bool ReachesAt(UnitEntityData recipient, Vector3 at)
            {
                if (!IsValid || recipient == null || ReferenceEquals(recipient, m_Caster)) return false;
                try
                {
                    if (recipient.Descriptor?.State == null || recipient.Descriptor.State.IsDead) return false;
                    Vector3 eye = at + (recipient.EyePosition - recipient.Position);
                    return s_CanTarget(m_Projectile, m_Context, recipient) &&
                           s_Point(m_Projectile, eye, recipient.Corpulence, Launch, Direction, Range);
                }
                catch
                {
                    return false;
                }
            }

            public bool TryRecipients(IEnumerable<UnitEntityData> pool, out List<UnitEntityData> reached)
            {
                reached = null;
                if (!IsValid || pool == null) return false;
                try
                {
                    var selected = new List<UnitEntityData>();
                    var seen = new HashSet<UnitEntityData>();
                    foreach (var unit in pool)
                        if (seen.Add(unit) && Reaches(unit)) selected.Add(unit);
                    reached = selected;
                    return true;
                }
                catch
                {
                    IsValid = false;
                    return false;
                }
            }
        }
    }
}
