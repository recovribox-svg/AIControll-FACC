using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using CompanionAI_Pathfinder.Tactics;
using Kingmaker.Blueprints;
using Kingmaker.Enums;
using Kingmaker.UnitLogic.Buffs.Blueprints;
using Kingmaker.UnitLogic.Parts;

namespace ProfileAudit
{
    internal static class RevealValueCheck
    {
        internal static List<string> Run(IEnumerable<SimpleBlueprint> loaded, out int failures)
        {
            int checks = 0, bad = 0;
            var lines = new List<string>();
            var flags = BindingFlags.NonPublic | BindingFlags.Static;
            var classify = typeof(TacticalEngine).GetMethod("OnlyRevealUtility", flags);
            var preview = typeof(TacticalEngine).GetMethod("WithDisabledConcealment", flags);
            void Check(bool ok, string why) { checks++; if (!ok) { bad++; lines.Add("REVEAL-VALUE FAILED: " + why); } }
            var faerie = loaded.OfType<BlueprintBuff>().SingleOrDefault(b => b.AssetGuid.ToString() == "cc383a9eaae4d2b45a925d442b367b54");
            if (faerie == null) { failures = 0; return new List<string> { "REVEAL-VALUE SKIPPED: FaerieFireBuff not in this input scope" }; }
            try
            {
                Check(classify != null && (bool)classify.Invoke(null, new object[] { faerie }), "actual Faerie Fire is fully read utility");
                Check(!(bool)classify.Invoke(null, new object[] { null }), "absent buff is not classified");
                var mixed = new BlueprintBuff { ComponentsArray = faerie.ComponentsArray.Concat(new BlueprintComponent[] {
                    new Kingmaker.UnitLogic.FactLogic.AddCondition { Condition = Kingmaker.UnitLogic.UnitCondition.Blindness } }).ToArray() };
                Check(!(bool)classify.Invoke(null, new object[] { mixed }), "a mixed blindness/reveal buff keeps ordinary control valuation");
                var part = (UnitPartConcealment)RuntimeHelpers.GetUninitializedObject(typeof(UnitPartConcealment));
                var count = typeof(UnitPartConcealment).GetField("m_Disable", BindingFlags.Instance | BindingFlags.NonPublic);
                foreach (int initial in new[] { 0, 3 })
                {
                    count.SetValue(part, initial);
                    bool read = false;
                    preview.Invoke(null, new object[] { part, new Func<Concealment>(() => {
                        read = (int)count.GetValue(part) == initial + 1; return Concealment.None; }) });
                    Check(read && (int)count.GetValue(part) == initial, "native counter retain/release restored from " + initial);
                    try { preview.Invoke(null, new object[] { part, new Func<Concealment>(() => throw new InvalidOperationException("probe")) }); }
                    catch (TargetInvocationException) { }
                    Check((int)count.GetValue(part) == initial, "native counter restored after a query throws from " + initial);
                }
            }
            catch (Exception ex) { bad++; lines.Add("REVEAL-VALUE FAILED: " + ex.GetBaseException()); }
            failures = bad;
            lines.Insert(0, $"REVEAL-VALUE {checks} checks, failures {bad}; native buff/counter layouts, no actor setup or native concealment query executed");
            return lines;
        }
    }
}
