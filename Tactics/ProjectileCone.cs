// ★ test1 scoring fixes: native cone recipient queries, scoped to one real scored aim.
// No geometry formula, angle search, Harmony hook, actor mutation or persistent mechanics-context cache.
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
    internal static class ProjectileCone
    {
        private delegate bool ConeTarget(AbilityDeliverProjectile projectile, AbilityExecutionContext context,
            UnitEntityData caster, UnitEntityData recipient, Vector3 launchPosition, Vector2 direction, float distance);
        private delegate float ProjectileRange(AbilityDeliverProjectile projectile, AbilityExecutionContext context, bool reach);

        // Only blueprint components and API delegates survive an aim. Null caches a non-cone blueprint.
        private static readonly Dictionary<BlueprintAbility, AbilityDeliverProjectile> Components =
            new Dictionary<BlueprintAbility, AbilityDeliverProjectile>();
        private static readonly HashSet<string> ReportedFailures = new HashSet<string>();
        private static ConeTarget s_Target;
        private static ProjectileRange s_Range;
        private static bool s_BindingsSought;

        public static bool IsCone(BlueprintAbility blueprint) => Component(blueprint) != null;

        private static AbilityDeliverProjectile Component(BlueprintAbility blueprint)
        {
            if (blueprint == null) return null;
            if (Components.TryGetValue(blueprint, out var component)) return component;
            component = blueprint.GetComponent<AbilityDeliverProjectile>();
            if (component?.Type != AbilityProjectileType.Cone) component = null;
            Components[blueprint] = component;
            return component;
        }

        private static bool BindQueries()
        {
            if (s_BindingsSought) return s_Target != null && s_Range != null;
            s_BindingsSought = true;
            try
            {
                var type = typeof(AbilityDeliverProjectile);
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var target = type.GetMethod("WouldTargetUnitCone", flags, null, new[]
                {
                    typeof(AbilityExecutionContext), typeof(UnitEntityData), typeof(UnitEntityData),
                    typeof(Vector3), typeof(Vector2), typeof(float)
                }, null);
                var range = type.GetMethod("GetProjectileRange", flags, null,
                    new[] { typeof(AbilityExecutionContext), typeof(bool) }, null);
                if (target == null || range == null || target.ContainsGenericParameters || range.ContainsGenericParameters)
                    throw new MissingMethodException("Native nongeneric cone/range query was not found");
                // These are ordinary read queries. Binding them does not replace or intercept any game method.
                s_Target = (ConeTarget)Delegate.CreateDelegate(typeof(ConeTarget), target);
                s_Range = (ProjectileRange)Delegate.CreateDelegate(typeof(ProjectileRange), range);
            }
            catch (Exception ex)
            {
                s_Target = null;
                s_Range = null;
                Report("bindings", ex);
            }
            return s_Target != null && s_Range != null;
        }

        /// <summary>
        /// One real context for this aim, reused only while querying its recipient set. The native unit query owns
        /// Selective filtering, caster exclusion, obstacle/corpulence checks and cone edge/range geometry.
        /// </summary>
        public static bool TryCreate(AbilityData ability, TargetWrapper actualAim, Vector3 plannedGroundOrigin, out Query query)
        {
            query = null;
            var blueprint = ability?.Blueprint;
            try
            {
                var component = Component(blueprint);
                if (component == null || actualAim == null || ability.Caster?.Unit == null || !BindQueries()) return false;
                // Controlled projectiles can launch from their existing projectile, and subclasses may replace the
                // ordinary unit selector. Neither can safely be approximated by an ordinary caster-origin cone.
                if (component.GetType() != typeof(AbilityDeliverProjectile) || component.ControlledProjectileHolderBuff != null)
                    throw new NotSupportedException("Custom or controlled cone delivery has no ordinary launch origin");
                var caster = ability.Caster.Unit;
                Vector3 eye = plannedGroundOrigin + (caster.EyePosition - caster.Position);
                Vector3 direction = actualAim.Point - eye;
                direction.y = 0f;
                if (!Finite(eye) || !Finite(direction) || !(direction.sqrMagnitude > 0f))
                    throw new InvalidOperationException("Cone aim/origin has no finite direction");
                direction.Normalize();
                Vector3 launch = eye + direction * caster.Corpulence;
                var context = CombatMath.CreateContext(ability, actualAim);
                if (context == null) throw new InvalidOperationException("Native cone context could not be created");
                float distance = s_Range(component, context, context.HasMetamagic(Metamagic.Reach));
                if (!(distance > 0f) || float.IsInfinity(distance) || !Finite(launch))
                    throw new InvalidOperationException("Native cone range/origin is invalid");
                query = new Query(component, context, caster, plannedGroundOrigin, launch,
                    new Vector2(direction.x, direction.z), distance, blueprint.AssetGuid.ToString());
                return true;
            }
            catch (Exception ex)
            {
                Report(blueprint?.AssetGuid.ToString() ?? "unknown", ex);
                return false;
            }
        }

        private static bool Finite(Vector3 point) =>
            !float.IsNaN(point.x) && !float.IsInfinity(point.x) &&
            !float.IsNaN(point.y) && !float.IsInfinity(point.y) &&
            !float.IsNaN(point.z) && !float.IsInfinity(point.z);

        private static void Report(string key, Exception ex)
        {
            if (ReportedFailures.Add(key + "|" + ex.GetType().Name))
                Main.Warning($"[Cone] native recipient query unavailable ({key}, {ex.GetType().Name}); cone candidate omitted");
        }

        internal sealed class Query
        {
            private readonly AbilityDeliverProjectile m_Projectile;
            private readonly AbilityExecutionContext m_Context;
            private readonly UnitEntityData m_Caster;
            private readonly Vector3 m_Launch;
            private readonly Vector2 m_Direction;
            private readonly string m_BlueprintKey;

            /// <summary>The planned ground casting position, before the native eye/body launch offset.</summary>
            public Vector3 Origin { get; }
            public float Range { get; }
            public bool IsValid { get; private set; } = true;

            internal Query(AbilityDeliverProjectile projectile, AbilityExecutionContext context, UnitEntityData caster,
                Vector3 origin, Vector3 launch, Vector2 direction, float range, string blueprintKey)
            {
                m_Projectile = projectile;
                m_Context = context;
                m_Caster = caster;
                Origin = origin;
                m_Launch = launch;
                m_Direction = direction;
                Range = range;
                m_BlueprintKey = blueprintKey;
            }

            /// <summary>
            /// Includes unconscious recipients like native delivery. Any native failure invalidates this whole aim;
            /// callers must not keep an earlier enemy result after an ally query fails.
            /// </summary>
            public bool Reaches(UnitEntityData recipient)
            {
                if (!IsValid) throw new InvalidOperationException("Cone recipient query was already invalidated");
                if (recipient == null || ReferenceEquals(recipient, m_Caster)) return false;
                try
                {
                    if (recipient.Descriptor?.State == null)
                        throw new InvalidOperationException("Cone recipient state is unavailable");
                    if (recipient.Descriptor.State.IsDead) return false;
                    return s_Target(m_Projectile, m_Context, m_Caster, recipient, m_Launch, m_Direction, Range);
                }
                catch (Exception ex)
                {
                    IsValid = false;
                    Report(m_BlueprintKey, ex);
                    throw new InvalidOperationException("Native cone recipient query failed; discard this aim", ex);
                }
            }

            /// <summary>All-or-none selection; a failed ally/geometry query cannot silently disappear from the result.</summary>
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
