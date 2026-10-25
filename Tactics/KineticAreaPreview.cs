// ★ v0.3.22: use the game's actual area shapes without spawning an area entity, effects, or particles.
using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.Utility;
using Kingmaker.View.MapObjects;
using UnityEngine;

namespace CompanionAI_Pathfinder.Tactics
{
    internal sealed class KineticAreaPreview : IDisposable
    {
        [ThreadStatic] private static KineticAreaPreview current;
        private readonly KineticAreaPreview previous;
        private readonly AbilityProfile profile;
        private readonly List<GameObject> objects = new List<GameObject>();
        private readonly List<Kingmaker.View.MapObjects.SriptZones.IScriptZoneShape> shapes =
            new List<Kingmaker.View.MapObjects.SriptZones.IScriptZoneShape>();
        private bool valid = true;

        private KineticAreaPreview(AbilityData ability, AbilityProfile p, TargetWrapper aim)
        {
            profile = p;
            previous = current;
            current = this;
            try
            {
                foreach (var area in p.SpawnedAreas)
                {
                    if (area.Shape == Kingmaker.UnitLogic.Abilities.Blueprints.AreaEffectShape.AllArea) continue;
                    var obj = new GameObject("AI kinetic area shape preview") { hideFlags = HideFlags.HideAndDontSave };
                    objects.Add(obj);
                    obj.SetActive(false); // no view OnEnable/updates, registration, particles, or area lifecycle
                    obj.transform.position = aim.Point;
                    obj.transform.rotation = Quaternion.Euler(0f, aim.Orientation, 0f);
                    var view = obj.AddComponent<AreaEffectView>();
                    AccessTools.Method(typeof(AreaEffectView), "InitShape").Invoke(view, new object[] { area, aim });
                    if (view.Shape == null) { valid = false; break; }
                    shapes.Add(view.Shape);
                }
            }
            catch (Exception ex)
            {
                valid = false;
                Main.Verbose($"[Kinetic] native area shape unavailable: {ex.Message}");
            }
        }

        internal static KineticAreaPreview Create(AbilityData ability, AbilityProfile p, TargetWrapper aim) =>
            aim != null && p.SpawnedAreas.Any() && (p.IsKineticEffect ||
                TacticalEngine.IsOwnBlast(ability.Caster.Unit.Get<Kingmaker.UnitLogic.Class.Kineticist.UnitPartKineticist>(), ability))
                ? new KineticAreaPreview(ability, p, aim) : null;

        internal static bool Reaches(AbilityProfile p, UnitEntityData unit) =>
            current?.profile != p || current.valid && current.shapes.All(s => s.Contains(unit.Position, unit.Corpulence));

        internal static float StayTicks(AbilityProfile p, UnitEntityData target, float horizon)
        {
            if (current == null || !current.valid || !current.profile.SpawnedAreas.SequenceEqual(p.SpawnedAreas)) return 1f;
            if (!target.Descriptor.State.CanMove || CombatMath.IsDisabled(target)) return horizon;
            if (!NativeActionCost.TrySeconds(target, Kingmaker.UnitLogic.Commands.Base.UnitCommand.CommandType.Move,
                                            false, out float seconds) || target.CurrentSpeedMps <= 0f) return 1f;
            float stride = target.CurrentSpeedMps * seconds;
            float nearest = float.MaxValue;
            // Geometry sampling only: the game's shape decides containment and dimensions at every sample.
            for (int i = 0; i < 8; i++)
            {
                var direction = Quaternion.Euler(0f, i * 45f, 0f) * Vector3.forward;
                for (int moves = 1; moves < Math.Ceiling(horizon); moves++)
                    if (current.shapes.All(s => !s.Contains(target.Position + direction * stride * moves, target.Corpulence)))
                    { nearest = Math.Min(nearest, moves); break; }
            }
            return Math.Min(horizon, nearest);
        }

        public void Dispose()
        {
            current = previous;
            foreach (var obj in objects) UnityEngine.Object.DestroyImmediate(obj);
        }
    }
}
