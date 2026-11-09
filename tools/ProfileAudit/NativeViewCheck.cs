using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using CompanionAI_Pathfinder.Tactics;
using Kingmaker.EntitySystem.Entities;

namespace ProfileAudit
{
    internal static class NativeViewCheck
    {
        internal static List<string> Run(out int failures)
        {
            int bad = 0, checks = 0;
            var lines = new List<string>();
            var reader = typeof(CombatMath).GetMethod("ReadRoutine", BindingFlags.NonPublic | BindingFlags.Static);
            var pending = (UnitEntityData)RuntimeHelpers.GetUninitializedObject(typeof(UnitEntityData));
            // No entity constructor, geometry lookup, attack rule, command or game singleton is initialized.
            foreach (bool full in new[] { false, true })
            foreach (var pair in new[] { (pending, pending), ((UnitEntityData)null, pending), (pending, (UnitEntityData)null) })
            {
                checks++;
                try
                {
                    if (reader == null || reader.Invoke(null, new object[] { pair.Item1, pair.Item2, full }) != null)
                    { bad++; lines.Add("NATIVE-VIEW FAILED: an incomplete actor pair supplied an attack routine"); }
                }
                catch (Exception ex)
                { bad++; lines.Add("NATIVE-VIEW FAILED: " + ex.GetBaseException()); }
            }
            lines.Insert(0, $"NATIVE-VIEW {checks} checks, failures {bad}; uninitialized viewless entities, no actor setup or native commands");
            failures = bad;
            return lines;
        }
    }
}
