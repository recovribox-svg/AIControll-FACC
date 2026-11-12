// AreaEffectEntityData uses EntityBoundsHelper's capsule/box physics query for cylinders and boxes.
// Forecast that query using read-only snapshots; never move/create a collider or run an overlap query per hint.
using System;
using System.Collections.Generic;
using CompanionAI_Pathfinder.Tactics;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.View.MapObjects.SriptZones;
using UnityEngine;

namespace CompanionAI_Pathfinder.Analysis
{
    public static partial class AoeDangerAnalyzer
    {
        private readonly struct ExitVolume
        {
            internal readonly AreaEffectEntityData Area;
            internal readonly Vector3 Center, HalfExtents;
            internal readonly float HalfHeight, Radius;
            private readonly Quaternion WorldToBox;
            private readonly IScriptZoneShape Shape;
            private readonly int Kind; // 0 = native Contains, 1 = world-up capsule, 2 = oriented box.

            internal ExitVolume(AreaEffectEntityData area, Vector3 center, float halfHeight, float radius)
            { this = default; Area = area; Center = center; HalfHeight = halfHeight; Radius = radius; Kind = 1; }
            internal ExitVolume(AreaEffectEntityData area, Vector3 center, Vector3 halfExtents, Quaternion inverse)
            { this = default; Area = area; Center = center; HalfExtents = halfExtents; WorldToBox = inverse; Kind = 2; }
            internal ExitVolume(AreaEffectEntityData area, IScriptZoneShape shape)
            { this = default; Area = area; Shape = shape; }

            internal bool Overlaps(Vector3 position, Vector3 bodyOffset, float body, float corpulence, float clearance)
            {
                if (!FiniteExitPoint(position)) return true;
                if (Kind == 0) return Shape == null || Shape.Contains(position, corpulence + clearance);
                Vector3 relative = position + bodyOffset - Center;
                if (Kind == 1)
                    return AreaExitGeometry.CapsuleOverlaps(relative.x, relative.y, relative.z, HalfHeight, Radius, body + clearance);
                var local = WorldToBox * relative;
                return AreaExitGeometry.BoxOverlaps(local.x, local.y, local.z, HalfExtents.x, HalfExtents.y, HalfExtents.z, body + clearance);
            }

            internal string Description => Kind == 1 ? $"capsule center={Center} half-height={HalfHeight:F2} radius={Radius:F2}"
                : Kind == 2 ? $"box center={Center} extents={HalfExtents}" : "native shape.Contains";
        }

        private sealed class ExitSpace
        {
            internal readonly List<ExitVolume> Volumes = new List<ExitVolume>();
            internal Vector3 BodyOffset, ObservedBodyCenter;
            internal float BodyRadius, Corpulence;

            internal bool IsOutside(Vector3 point, float clearance)
            {
                foreach (var volume in Volumes)
                    if (volume.Overlaps(point, BodyOffset, BodyRadius, Corpulence, clearance)) return false;
                return true;
            }
        }

        private static ExitSpace ReadExitSpace(UnitEntityData unit, AreasInside inside)
        {
            try
            {
                if (unit == null || inside == null || !AreaExitGeometry.Finite(unit.Corpulence) || unit.Corpulence < 0f) return null;
                var result = new ExitSpace { Corpulence = unit.Corpulence };
                foreach (var area in inside.Areas)
                {
                    if (!IsLive(area)) continue;
                    if (area.Blueprint?.Shape == AreaEffectShape.AllArea) return null;
                    var shape = area.View?.Shape;
                    if (shape == null) return null;
                    if (shape is ScriptZoneCylinder || shape is ScriptZoneBox)
                    {
                        if (!(result.BodyRadius > 0f))
                        {
                            var sphere = unit.View?.SphereBoundsCollider;
                            if (sphere == null) return null;
                            // Collider.bounds is the actual native world AABB of this sphere, including its scale.
                            // Read it rather than guessing that native Corpulence equals the physics radius.
                            var bounds = sphere.bounds;
                            var extent = bounds.extents;
                            result.BodyRadius = Math.Max(extent.x, Math.Max(extent.y, extent.z));
                            result.BodyOffset = sphere.transform.TransformVector(sphere.center);
                            result.ObservedBodyCenter = bounds.center;
                            if (!(result.BodyRadius > 0f) || !AreaExitGeometry.Finite(result.BodyRadius) ||
                                !FiniteExitPoint(result.BodyOffset) || !FiniteExitPoint(result.ObservedBodyCenter)) return null;
                        }
                        if (shape is ScriptZoneCylinder cylinder)
                        {
                            // Exact EntityBoundsHelper query: world-up capsule; horizontal mean scale, including height.
                            // The cylinder's visual rotation is not the orientation of this native capsule.
                            var scale = cylinder.transform.lossyScale;
                            float mean = (scale.x + scale.z) / 2f;
                            float height = cylinder.Height * mean, radius = cylinder.Radius * mean;
                            Vector3 center = shape.Center();
                            if (!(mean > 0f) || !AreaExitGeometry.Finite(height) || !AreaExitGeometry.Finite(radius) ||
                                height < 0f || radius < 0f || !FiniteExitPoint(center)) return null;
                            result.Volumes.Add(new ExitVolume(area, center, height, radius));
                        }
                        else
                        {
                            var box = (ScriptZoneBox)shape;
                            var extents = box.Bounds.extents;
                            extents.Scale(box.transform.lossyScale);
                            Vector3 center = shape.Center();
                            var rotation = box.transform.rotation;
                            float norm = rotation.x * rotation.x + rotation.y * rotation.y + rotation.z * rotation.z + rotation.w * rotation.w;
                            if (!FiniteExitPoint(center) || !FiniteExitPoint(extents) ||
                                extents.x < 0f || extents.y < 0f || extents.z < 0f ||
                                !AreaExitGeometry.Finite(rotation.x) || !AreaExitGeometry.Finite(rotation.y) ||
                                !AreaExitGeometry.Finite(rotation.z) || !AreaExitGeometry.Finite(rotation.w) ||
                                !AreaExitGeometry.Finite(norm) || norm < 0.5f || norm > 1.5f)
                                return null;
                            result.Volumes.Add(new ExitVolume(area, center, extents, Quaternion.Inverse(rotation)));
                        }
                    }
                    else result.Volumes.Add(new ExitVolume(area, shape)); // Native ShouldUnitBeInside uses Contains here.
                }
                return result;
            }
            catch { return null; } // No escape credit from unreadable native geometry.
        }

        private static void NoteExitBounds(UnitEntityData unit, ExitSpace space)
        {
            foreach (var volume in space.Volumes)
            {
                bool current = volume.Overlaps(unit.Position, space.BodyOffset, space.BodyRadius, space.Corpulence, 0f);
                if (!CombatMemory.FirstThisFight($"exit-bounds:{unit.UniqueId}:{volume.Area.UniqueId}:{current}")) continue;
                string body = space.BodyRadius > 0f
                    ? $"sphere radius={space.BodyRadius:F2}, corpulence={space.Corpulence:F2}, " +
                      $"sphere local-center world offset={space.BodyOffset}, observed sphere center={space.ObservedBodyCenter}"
                    : $"native shape.Contains with logical corpulence={space.Corpulence:F2}; physics sphere not queried";
                Main.Log($"[AreaExit] {unit.CharacterName}: registered inside {volume.Area.Blueprint?.name}[{volume.Area.UniqueId}]; " +
                    $"geometry at current position={(current ? "overlaps" : "clear")}; {volume.Description}; " +
                    $"{body}; unit position={unit.Position}; " +
                    "read-only snapshot, no actor/collider mutation; geometry can change after issue");
            }
        }
    }
}
