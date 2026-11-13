// Native estimate records and reference identities only; no actor setup, rules, body mutation or Unity timing.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using CompanionAI_Pathfinder.Tactics;
using Kingmaker.EntitySystem.Entities;

namespace ProfileAudit
{
    internal static class ShifterReferenceCheck
    {
        internal static List<string> Run(out int failures)
        {
            int checks = 0, bad = 0; var lines = new List<string>();
            void Check(bool ok, string why) { checks++; if (!ok) { bad++; lines.Add("FORM-REFERENCE FAILED: " + why); } }
            try
            {
                const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
                var engine = typeof(TacticalEngine);
                var offType = engine.GetNestedType("Offence", all);
                var targetField = offType.GetField("WeaponTarget", all);
                var quoteField = offType.GetField("WeaponQuote", all);
                var legacyEnemy = offType.GetField("Enemy", all);
                var method = engine.GetMethod("TryShifterReference", all);
                var off = Activator.CreateInstance(offType, true);
                var immune = (UnitEntityData)RuntimeHelpers.GetUninitializedObject(typeof(UnitEntityData));
                var other = (UnitEntityData)RuntimeHelpers.GetUninitializedObject(typeof(UnitEntityData));
                var quote = new WeaponAttackEstimate { ExpectedDamage = 0f, IsRanged = true };
                targetField.SetValue(off, immune); quoteField.SetValue(off, quote); legacyEnemy.SetValue(off, other);
                object[] a = { off, null, null };
                bool Read(object value) { a = new object[] { value, null, null }; return (bool)method.Invoke(null, a); }
                Check(Read(off), "known zero-damage native quote remains a usable comparison");
                Check(ReferenceEquals(a[1], immune), "zero baseline preserves the actual assessed target");
                Check(!ReferenceEquals(a[1], other), "another profitable/first enemy cannot replace the reference");
                Check(ReferenceEquals(a[2], quote), "exact existing native estimate is reused without another forecast");
                Check(((WeaponAttackEstimate)a[2]).IsRanged, "zero-damage ranged baseline retains native weapon style");
                quote.ExpectedDamage = 12f;
                Check(Read(off) && ((WeaponAttackEstimate)a[2]).ExpectedDamage == 12f && ReferenceEquals(a[1], immune),
                    "positive baseline uses the same snapshot/target contract");
                quoteField.SetValue(off, null);
                Check(!Read(off), "unreadable current native routine does not become an invented zero");
                quoteField.SetValue(off, quote); targetField.SetValue(off, null);
                Check(!Read(off), "known damage without its assessed target cannot be paired");
                Check(!Read(null), "absent offence snapshot cannot be appraised");
                Check(!Read(off) && legacyEnemy.GetValue(off) == other, "legacy profitable enemy alone supplies no fallback target");
                var producer = SaveBoostCheck.ReferencedMembers(engine.GetMethod("OffenceOf", all)).ToList();
                int target = producer.FindIndex(m => m.Name == "WeaponTarget");
                int estimate = producer.FindIndex(m => m.Name == "WeaponQuote");
                int positive = producer.FindIndex(m => m.Name == "ExpectedDamage");
                Check(target >= 0 && estimate > target && positive > estimate,
                    "compiled producer records target/estimate before its positive-damage branch");
                var form = SaveBoostCheck.ReferencedMembers(engine.GetMethod("AppraiseShifterForm", all)).ToList();
                Check(form.Any(m => m.Name == "TryShifterReference") && !form.Any(m => m.Name == "FirstOrDefault"),
                    "new-form appraisal uses the pair and has no first-enemy fallback");
                var fury = SaveBoostCheck.ReferencedMembers(engine.GetMethod("AddShifterFuryCandidates", all)).ToList();
                Check(fury.Any(m => m.Name == "Enemy") && !fury.Any(m => m.Name == "TryShifterReference"),
                    "existing positive-routine Fury admission stays unchanged");
                Check(SaveBoostCheck.ReferencedMembers(method).OfType<MethodBase>().All(m => m.DeclaringType == typeof(object)),
                    "pair lookup calls no native rule, actor getter, search or estimator");
            }
            catch (Exception ex) { bad++; lines.Add("FORM-REFERENCE FAILED: " + ex.GetBaseException()); }
            failures = bad;
            lines.Insert(0, $"FORM-REFERENCE {checks} checks, failures {bad}; native estimate records/reference identities and compiled routes; no actor setup, rule, body mutation or Unity timing execution");
            return lines;
        }
    }
}
