// Compiled exit-volume math and native membership routes. No actor, transform, collider or physics query executes.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CompanionAI_Pathfinder.Analysis;
using CompanionAI_Pathfinder.Tactics;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.View.MapObjects.SriptZones;
using UnityEngine;

namespace ProfileAudit
{
    internal static class AreaBoundsCheck
    {
        private sealed class PlainShape : IScriptZoneShape
        {
            internal float Received;
            public bool Contains(Vector3 point, float corpulence) { Received = corpulence; return false; }
            public Bounds GetBounds() => default;
            public Vector3 Center() => default;
        }

        internal static List<string> Run(out int failures)
        {
            int checks = 0, bad = 0; var lines = new List<string>();
            void Check(bool ok, string why) { checks++; if (!ok) { bad++; lines.Add("AREA-BOUNDS FAILED: " + why); } }
            try
            {
                const BindingFlags all = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                var owner = typeof(AoeDangerAnalyzer);
                var volume = owner.GetNestedType("ExitVolume", all);
                object Make(Type[] parameters, params object[] args) => volume.GetConstructor(all, null, parameters, null).Invoke(args);
                bool Overlaps(object v, Vector3 p, float body, float corpulence = 0.1f, float clearance = 0f, Vector3 offset = default) =>
                    (bool)volume.GetMethod("Overlaps", all).Invoke(v, new object[] { p, offset, body, corpulence, clearance });
                var capsule = Make(new[] { typeof(AreaEffectEntityData), typeof(Vector3), typeof(float), typeof(float) },
                    null, Vector3.zero, 100f, 1.524f);
                Check(Overlaps(capsule, new Vector3(2f, 0f, 0f), 0.6f), "actual sphere radius wins over smaller Corpulence");
                Check(!Overlaps(capsule, new Vector3(2.2f, 0f, 0f), 0.6f), "clear body surface is outside capsule");
                Check(Overlaps(capsule, new Vector3(0f, 99f, 0f), 0.4f), "native capsule segment uses Height, not Height/2");
                Check(!Overlaps(capsule, new Vector3(0f, 102f, 0f), 0.4f), "native capsule has rounded finite ends");
                Check(Overlaps(capsule, new Vector3(2f, 0f, 0f), 0.4f, clearance: 0.15f), "arrival tolerance enlarges actual sphere");
                Check(Overlaps(capsule, new Vector3(2.5f, 0f, 0f), 0.4f, offset: new Vector3(-1f, 0f, 0f)),
                    "local collider-center offset follows projected body placement");
                Check(Overlaps(capsule, new Vector3(float.NaN, 0, 0), 0.4f), "unknown endpoint cannot prove exit");
                var box = Make(new[] { typeof(AreaEffectEntityData), typeof(Vector3), typeof(Vector3), typeof(Quaternion) },
                    null, new Vector3(10f, 0f, -5f), new Vector3(1f, 2f, 3f), new Quaternion(0f, -0.70710677f, 0f, 0.70710677f));
                Check(Overlaps(box, new Vector3(13.6f, 0f, -5f), 0.8f), "rotated native box uses world rotation and centre");
                Check(!Overlaps(box, new Vector3(10f, 0f, -3.1f), 0.8f), "rotated narrow axis is still narrow");
                Check(!Overlaps(box, new Vector3(13.6f, 0f, -3.4f), 0.8f), "sphere/box corner uses distance, not expanded cube");
                var shape = new PlainShape();
                var other = Make(new[] { typeof(AreaEffectEntityData), typeof(IScriptZoneShape) }, null, shape);
                Check(!Overlaps(other, Vector3.zero, 10f, 0.4f, 0.15f) && Math.Abs(shape.Received - 0.55f) < 0.0001f,
                    "other native shapes retain Contains with Corpulence and stopping clearance");
                var read = owner.GetMethod("ReadExitSpace", all);
                // Do not invoke ReadExitSpace, even with a null actor: JIT binding of its Unity ECalls needs Unity.
                var members = SaveBoostCheck.ReferencedMembers(read).ToArray();
                foreach (string name in new[] { "get_SphereBoundsCollider", "get_bounds", "get_center", "TransformVector", "get_lossyScale", "get_rotation" })
                    Check(members.Any(m => m.Name == name), "production snapshot reads native " + name);
                foreach (string name in new[] { "Height", "Radius", "Bounds" })
                    Check(members.Any(m => m.Name == name), "production snapshot reads native shape field " + name);
                Check(!members.Any(m => m.Name.StartsWith("Overlap", StringComparison.Ordinal) || m.Name == "AddComponent" ||
                    m.Name.StartsWith("set_", StringComparison.Ordinal) || m.Name == ".ctor" &&
                    (m.DeclaringType == typeof(UnitEntityData) || m.DeclaringType?.Namespace == "UnityEngine")),
                    "snapshot neither queries overlaps nor creates/mutates actors, transforms or colliders");
                bool Calls(MethodInfo method, string target) => SaveBoostCheck.ReferencedMembers(method).Any(m => m.Name == target);
                Check(Calls(owner.GetMethod("FindExit", all), "ReadExitSpace"), "search snapshots native geometry once before hints");
                Check(Calls(owner.GetMethod("TryProjectExit", all), "IsOutside"), "projected endpoint uses the snapshot");
                var callbacks = SaveBoostCheck.ReferencedMembers(owner.GetMethod("ExitPathCheck", all)).OfType<MethodInfo>();
                Check(callbacks.Any(m => Calls(m, "ReadExitSpace") && Calls(m, "ValidateExitPath")),
                    "selected preflight and issue callback takes fresh geometry before the route check");
                Check(Calls(owner.GetMethod("NoteExitBounds", all), "FirstThisFight"), "read-only diagnostic is bounded per fight");
                var native = typeof(UnitEntityData).Assembly;
                var helper = native.GetType("Kingmaker.Controllers.Optimization.EntityBoundsHelper");
                var find = helper.GetMethod("FindUnitsInShape", all);
                Check(Calls(find, "OverlapCapsule") && Calls(find, "OverlapBox"), "supplied native shape helper uses capsule and box queries");
                Check(Calls(typeof(AreaEffectEntityData).GetMethod("UpdateUnits", all), "FindUnitsInShape"),
                    "supplied native area update obtains the queried bounds list");
                Check(Calls(typeof(AreaEffectEntityData).GetMethod("ShouldUnitBeInside", all), "Contains"),
                    "supplied native membership retains Contains for its other-shape branch (branch types inspected separately)");
            }
            catch (Exception ex) { bad++; lines.Add("AREA-BOUNDS FAILED: " + ex.GetBaseException()); }
            failures = bad;
            lines.Insert(0, $"AREA-BOUNDS {checks} checks, failures {bad}; compiled volume math, native read/callback/membership routes; no actor, collider, physics query or Unity timing execution");
            return lines;
        }
    }
}
