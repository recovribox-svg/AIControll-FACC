// review2: execute the context-elision predicate, native constants and production amount/duration readers.
// Real exported abilities plus inert context/action layouts; no actor setup, native cast, RNG or Unity timing.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using CompanionAI_Pathfinder.Analysis;
using CompanionAI_Pathfinder.Tactics;
using Kingmaker.Blueprints;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic.Buffs.Blueprints;
using Kingmaker.UnitLogic.Mechanics;
using Kingmaker.UnitLogic.Mechanics.Actions;

namespace ProfileAudit
{
    internal static class BuffContextCheck
    {
        private sealed class UninspectedApply : ContextActionApplyBuff { }
        private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

        internal static List<string> Run(IEnumerable<SimpleBlueprint> loaded, out int failures)
        {
            int checks = 0, bad = 0;
            var lines = new List<string>();
            void Check(bool ok, string why) { checks++; if (!ok) { bad++; lines.Add("BUFF-CONTEXT FAILED: " + why); } }
            try
            {
                var engine = typeof(TacticalEngine);
                var predicate = engine.GetMethod("CanSkipBuffContext", All);
                var skip = (Func<AbilityProfile, List<BlueprintBuff>, List<BuffPart>, HashSet<string>, bool>)predicate.CreateDelegate(
                    typeof(Func<AbilityProfile, List<BlueprintBuff>, List<BuffPart>, HashSet<string>, bool>));
                var amounts = engine.GetMethods(All).Single(m => m.Name == "PartAmount" && m.GetParameters().Length == 6);
                var duration = engine.GetMethod("EffectiveRounds", All);
                var native = loaded.OfType<BlueprintAbility>().Where(b =>
                    b.name.StartsWith("ProfaneAscensionAbility", StringComparison.Ordinal) &&
                    b.name != "ProfaneAscensionAbility1" && b.name != "ProfaneAscensionAbility2").ToArray();
                Check(native.Length == 12, "all twelve exported Profane Invocation variants are present");
                foreach (var b in native)
                {
                    var p = AbilityProfile.Get(b);
                    var buffs = p.TargetBuffs;
                    var parts = buffs.SelectMany(x => BuffPartsReader.Read(x).Parts).ToList();
                    var unread = new HashSet<string>(buffs.SelectMany(x => BuffPartsReader.Read(x).Unread));
                    Check(skip(p, buffs, parts, unread), "actual permanent constant stat appraisal omits an unused context: " + b.name);
                    bool exact = parts.Count == 1;
                    foreach (var part in parts)
                    {
                        object[] low = { part, null, null, null, 1, null };
                        object[] high = { part, null, null, null, 20, null };
                        exact &= (float)amounts.Invoke(null, low) == 2f && (float)amounts.Invoke(null, high) == 2f;
                        if (part.Value != null) exact &= part.Value.Calculate((MechanicsContext)null) * part.Multiplier + part.Fixed == 2;
                    }
                    exact &= buffs.All(buff => (float)duration.Invoke(null, new object[] { null, p, buff, null }) == 100f);
                    Check(exact, "production amounts and permanent duration retain native +2 without caster/target/context: " + b.name);
                }
                var profile = AbilityProfile.Get(native[0]);
                var targets = profile.TargetBuffs;
                var constant = new List<BuffPart> { new BuffPart { Kind = BuffPartKind.Strength, Fixed = 2 } };
                var empty = new HashSet<string>();
                Check(!skip(profile, targets, constant, new HashSet<string> { "unread" }), "unread effects keep the original context route");
                Check(!skip(profile, targets, new List<BuffPart>(), empty), "empty appraisals do not establish constant numerical effects");
                Check(!skip(null, targets, constant, empty) && !skip(profile, null, constant, empty), "missing metadata proves no fast path");
                Check(!skip(profile, new List<BlueprintBuff> { null }, constant, empty), "missing application keeps the original route");
                bool Part(BuffPart p) => skip(profile, targets, new List<BuffPart> { p }, empty);
                Check(!Part(new BuffPart { Kind = BuffPartKind.Strength, Value = new ContextValue { ValueType = ContextValueType.Rank } }),
                    "ranked stat amounts retain native context and ranks");
                Check(!Part(new BuffPart { Kind = BuffPartKind.Strength, Dice = new ContextDiceValue() }), "dice-valued parts retain context");
                Check(!Part(new BuffPart { Kind = BuffPartKind.Strength, Pool = new ContextValue { Value = 2 } }), "pooled values retain context");
                Check(!Part(new BuffPart { Kind = BuffPartKind.TempHp, FormulaBonusByCasterLevel = true }), "caster-level and other part kinds retain context");
                var timed = loaded.OfType<BlueprintAbility>().First(b => b.name == "BullsStrength");
                var timedProfile = AbilityProfile.Get(timed);
                Check(!skip(timedProfile, timedProfile.TargetBuffs, constant, empty), "actual timed stat spell retains duration context");
                var weapon = loaded.OfType<BlueprintAbility>().Select(AbilityProfile.Get).First(p => p.WeaponEnchants.Count > 0);
                Check(!skip(weapon, weapon.TargetBuffs, constant, empty), "weapon equipment carriers keep context");

                // Change only a private profile clone, never the actual export or a live actor/action.
                var clone = (AbilityProfile)typeof(object).GetMethod("MemberwiseClone", All).Invoke(profile, null);
                var derived = (UninspectedApply)RuntimeHelpers.GetUninitializedObject(typeof(UninspectedApply));
                derived.Permanent = true;
                typeof(AbilityProfile).GetField("m_Applications", All).SetValue(clone,
                    new Dictionary<BlueprintBuff, ContextActionApplyBuff> { [targets[0]] = derived });
                Check(!skip(clone, targets, constant, empty), "uninspected native action subclass does not inherit context elision");
                var body = SaveBoostCheck.ReferencedMembers(engine.GetMethod("AppraiseBuff", All)).ToArray();
                Check(body.Any(m => m.Name == "CanSkipBuffContext") && body.Any(m => m.Name == "CreateContext") &&
                      body.Any(m => m.Name == "AppraiseParts"), "production selects the narrow fast path and keeps the same appraisal kernel/fallback");
                var read = SaveBoostCheck.ReferencedMembers(predicate).ToArray();
                Check(!read.Any(m => m.Name == "CreateContext" || m.Name == "Trigger" || m.Name == "RunAction" || m.Name == "get_AllUnits"),
                    "eligibility adds no context, native rule, action or world query");
                for (int i = 0; i < 100; i++) skip(profile, targets, constant, empty);
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 10000; i++) skip(profile, targets, constant, empty);
                long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                Check(allocated == 0, "warm constant-stat eligibility allocates zero bytes in the offline host");
                lines.Add($"BUFF-CONTEXT warm predicate: 10000 calls, {allocated} bytes; no native context constructed; not a Unity benchmark");
            }
            catch (Exception ex) { bad++; lines.Add("BUFF-CONTEXT FAILED: " + ex.GetBaseException()); }
            failures = bad;
            lines.Insert(0, $"BUFF-CONTEXT {checks} checks, failures {bad}; actual permanent stat graphs and production scalar/layout readers; live actor/cast/Unity execution NOT TESTED");
            return lines;
        }
    }
}
