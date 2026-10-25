// ★ v0.3.22: reuse inactive native area views for this decision; no area entities/effects are spawned.
using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.Utility;
using Kingmaker.View.MapObjects;
using Kingmaker.View.MapObjects.SriptZones;
using UnityEngine;
namespace CompanionAI_Pathfinder.Tactics
{
    internal sealed class KineticAreaPreview : IDisposable
    {
        [ThreadStatic] private static KineticAreaPreview current;
        private readonly KineticAreaPreview previous;
        private readonly AbilityProfile profile;
        private readonly TargetWrapper aim;
        private readonly List<Geometry> geometry;
        private readonly Cache owned;
        private bool Valid => geometry.All(g => g.Shape != null);
        internal sealed class Geometry
        {
            internal GameObject Object;
            internal IScriptZoneShape Shape;
        }
        internal sealed class Cache : IDisposable
        {
            // Aim entries reuse the same native geometry for profiles/infusions of that ability and aim. Geometry is
            // additionally pooled by area blueprint, avoiding a GameObject per wall angle or related blast variant.
            private readonly Dictionary<(AbilityProfile, Vector3, float), List<Geometry>> aims =
                new Dictionary<(AbilityProfile, Vector3, float), List<Geometry>>();
            private readonly Dictionary<BlueprintAbilityAreaEffect, Geometry> areas = new Dictionary<BlueprintAbilityAreaEffect, Geometry>();
            internal int Builds => areas.Count;
            internal List<Geometry> Get(AbilityData ability, AbilityProfile p, TargetWrapper aim)
            {
                var key = (p, aim.Point, aim.Orientation);
                if (aims.TryGetValue(key, out var cached)) return cached;
                var result = new List<Geometry>();
                foreach (var area in p.SpawnedAreas)
                {
                    KineticDecisionScope.Check();
                    if (area.Shape == AreaEffectShape.AllArea) continue;
                    if (!areas.TryGetValue(area, out var geometry))
                    {
                        geometry = new Geometry();
                        areas[area] = geometry; // register before creation so exception paths still clean up
                        try
                        {
                            geometry.Object = new GameObject("AI kinetic area shape preview") { hideFlags = HideFlags.HideAndDontSave };
                            geometry.Object.SetActive(false);
                            var view = geometry.Object.AddComponent<AreaEffectView>();
                            AccessTools.Method(typeof(AreaEffectView), "InitShape").Invoke(view, new object[] { area, aim });
                            geometry.Shape = view.Shape;
                        }
                        catch (Exception ex) { Main.Verbose($"[Kinetic] native area shape unavailable: {ex.Message}"); }
                    }
                    result.Add(geometry);
                }
                aims[key] = result;
                return result;
            }
            public void Dispose()
            {
                foreach (var geometry in areas.Values)
                    try { if (geometry.Object != null) UnityEngine.Object.DestroyImmediate(geometry.Object); }
                    catch (Exception ex) { Main.Verbose($"[Kinetic] shape cleanup: {ex.Message}"); }
                areas.Clear(); aims.Clear();
            }
        }
        private KineticAreaPreview(AbilityData ability, AbilityProfile p, TargetWrapper aim)
        {
            profile = p; this.aim = aim;
            var cache = KineticDecisionScope.Current?.Areas ?? (owned = new Cache());
            try { geometry = cache.Get(ability, p, aim); }
            catch { owned?.Dispose(); throw; }
            previous = current; current = this;
            try { SetPose(); }
            catch { current = previous; owned?.Dispose(); throw; }
        }
        private void SetPose()
        {
            foreach (var g in geometry.Where(g => g.Object != null))
            {
                g.Object.transform.position = aim.Point;
                g.Object.transform.rotation = Quaternion.Euler(0f, aim.Orientation, 0f);
            }
        }
        internal static KineticAreaPreview Create(AbilityData ability, AbilityProfile p, TargetWrapper aim) =>
            aim != null && p.SpawnedAreas.Any() && (p.IsKineticEffect ||
                TacticalEngine.IsOwnBlast(ability.Caster.Unit.Get<Kingmaker.UnitLogic.Class.Kineticist.UnitPartKineticist>(), ability))
                ? new KineticAreaPreview(ability, p, aim) : null;
        internal static bool Reaches(AbilityProfile p, UnitEntityData unit) =>
            current?.profile != p || current.Valid && current.geometry.All(g => g.Shape.Contains(unit.Position, unit.Corpulence));
        internal static float StayTicks(AbilityProfile p, UnitEntityData target, float horizon)
        {
            KineticDecisionScope.Check();
            if (current == null || !current.Valid || !current.profile.SpawnedAreas.SequenceEqual(p.SpawnedAreas)) return 1f;
            if (!target.Descriptor.State.CanMove || CombatMath.IsDisabled(target)) return horizon;
            if (!NativeActionCost.TrySeconds(target, Kingmaker.UnitLogic.Commands.Base.UnitCommand.CommandType.Move,
                                            false, out float seconds) || target.CurrentSpeedMps <= 0f) return 1f;
            float stride = target.CurrentSpeedMps * seconds, nearest = float.MaxValue;
            for (int i = 0; i < 8; i++)
            {
                var direction = Quaternion.Euler(0f, i * 45f, 0f) * Vector3.forward;
                for (int moves = 1; moves < Math.Ceiling(horizon); moves++)
                    if (current.geometry.All(g => !g.Shape.Contains(target.Position + direction * stride * moves, target.Corpulence)))
                    { nearest = Math.Min(nearest, moves); break; }
            }
            return Math.Min(horizon, nearest);
        }
        public void Dispose()
        {
            current = previous;
            previous?.SetPose(); // nested infusion/ability scopes may reuse the same pooled geometry at a different aim
            owned?.Dispose();
        }
    }
}
