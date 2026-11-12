// Constant debuff amounts, actual exported parts and compiled lazy binding. No actor/context factory runs here.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using CompanionAI_Pathfinder.Analysis;
using CompanionAI_Pathfinder.Tactics;
using Kingmaker.Blueprints;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic.Buffs.Blueprints;
using Kingmaker.UnitLogic.Mechanics;

namespace ProfileAudit
{
    internal static class StaticDebuffCheck
    {
        internal static List<string> Run(IEnumerable<SimpleBlueprint> loaded, out int failures)
        {
            int bad = 0, checks = 0; var lines = new List<string>();
            void Check(bool ok, string why) { checks++; if (!ok) { bad++; lines.Add("STATIC-DEBUFF FAILED: " + why); } }
            try
            {
                const BindingFlags hidden = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
                var needs = (Func<BuffPart, bool>)typeof(TacticalEngine).GetMethod("BuffAmountNeedsContext", hidden)
                    .CreateDelegate(typeof(Func<BuffPart, bool>));
                var amount = typeof(TacticalEngine).GetMethods(hidden).Single(m => m.Name == "PartAmount" && m.GetParameters().Length == 5);
                var opaque = (MechanicsContext)RuntimeHelpers.GetUninitializedObject(typeof(MechanicsContext));
                foreach (var part in new[] { new BuffPart { Fixed = -2 },
                    new BuffPart { Value = new ContextValue { ValueType = ContextValueType.Simple, Value = -3 }, Multiplier = 2, Fixed = 1 },
                    new BuffPart { Fixed = 1, Conditional = true } })
                {
                    Check(!needs(part), "fixed/simple amount requires no source context");
                    var before = amount.Invoke(null, new object[] { part, null, opaque, null, 7 });
                    var after = amount.Invoke(null, new object[] { part, null, null, null, 1 });
                    Check((float)before == (float)after, "actual amount is identical without source parameters");
                }
                foreach (var part in new[] {
                    new BuffPart { Value = new ContextValue { ValueType = ContextValueType.Rank } },
                    new BuffPart { Value = new ContextValue { ValueType = ContextValueType.Shared } },
                    new BuffPart { Dice = new ContextDiceValue() }, new BuffPart { Pool = new ContextValue() },
                    new BuffPart { FormulaBonusByCasterLevel = true } })
                    Check(needs(part), "rank/shared/dice/pool/caster-level amount retains source binding");
                Check(!needs(new BuffPart { PerRecipientLevel = true }), "recipient-level amount still uses live recipient, not source context");
                Check(!needs(new BuffPart { ScaleByBaseAttack = true }), "base-attack scaling still uses live recipient, not source context");
                foreach (string name in new[] { "BaneBuff", "CrushingDespairBuff" })
                {
                    var buff = loaded.OfType<BlueprintBuff>().FirstOrDefault(b => b.name == name);
                    if (buff == null) { lines.Add("STATIC-DEBUFF fixture outside scope: " + name); continue; }
                    var parts = BuffPartsReader.Read(buff).Parts;
                    Check(parts.Count > 0 && parts.All(p => !needs(p)), "actual " + name + " numerical parts are context independent");
                }
                var method = typeof(TacticalEngine).GetMethod("DebuffShareOf", hidden);
                Check(SaveBoostCheck.ReferencedMembers(method).Any(m => m.DeclaringType == typeof(TacticalEngine) && m.Name == "BuffAmountNeedsContext"),
                    "production debuff appraisal uses the lazy source classifier");
                var simple = new BuffPart { Fixed = -2 }; // exclude fixture allocation from the measured reads
                long beforeBytes = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 10000; i++) needs(simple);
                Check(GC.GetAllocatedBytesForCurrentThread() - beforeBytes == 0, "10000 static metadata reads allocate zero bytes offline");
            }
            catch (Exception ex) { bad++; lines.Add("STATIC-DEBUFF FAILED: " + ex.GetBaseException()); }
            failures = bad;
            lines.Insert(0, $"STATIC-DEBUFF {checks} checks, failures {bad}; actual parts/scalar amounts, no actor, context factory or Unity timing execution");
            return lines;
        }
    }
}
