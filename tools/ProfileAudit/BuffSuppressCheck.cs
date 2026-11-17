// ★ suppressed buffs (docs/BURST_AIM_AND_TACTICS_PLAN.md §5, slice C): a buff the bearer's form or another effect suppresses
// does nothing. Native contracts on the real assembly, the mod's reader on every exported suppressor (a part filled by the
// game's own public PrepareForSuppression calls, as SuppressBuffs.OnActivate does, without ApplyChanges), and the compiled
// routes, lambda bodies included. Actor-free: no Game/Player, unit, buff instance, context or Unity object.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CompanionAI_Pathfinder.GameInterface;
using CompanionAI_Pathfinder.Tactics;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Classes.Spells;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Buffs;
using Kingmaker.UnitLogic.Buffs.Blueprints;
using Kingmaker.UnitLogic.FactLogic;
using Kingmaker.UnitLogic.Mechanics;
using Kingmaker.UnitLogic.Parts;

namespace ProfileAudit
{
    internal static class BuffSuppressCheck
    {
        private const BindingFlags Hidden = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic |
                                            BindingFlags.DeclaredOnly;

        private static MethodInfo Method(Type type, string name, params Type[] args)
        {
            var found = type.GetMethods(Hidden).Where(m => m.Name == name &&
                            (args.Length == 0 || m.GetParameters().Select(p => p.ParameterType).SequenceEqual(args))).ToList();
            if (found.Count != 1) throw new MissingMethodException(type.FullName, $"{name} ({found.Count} matches)");
            return found[0];
        }

        private static List<MemberInfo> Body(MethodBase method) => SaveBoostCheck.ReferencedMembers(method).ToList();

        /// <summary>A method's body and the bodies of its lambdas and local functions (compiler-generated, named after it).</summary>
        private static List<MemberInfo> BodyWithLambdas(Type type, string name)
        {
            var members = Body(Method(type, name));
            string prefix = "<" + name + ">";
            foreach (var holder in new[] { type }.Concat(type.GetNestedTypes(BindingFlags.NonPublic | BindingFlags.Public)))
                foreach (var m in holder.GetMethods(Hidden).Where(m => m.Name.StartsWith(prefix, StringComparison.Ordinal)))
                    members.AddRange(Body(m));
            return members;
        }

        private static bool Calls(IEnumerable<MemberInfo> body, Type type, string name) =>
            body.Any(m => m.DeclaringType == type && m.Name == name);

        internal static List<string> Run(IEnumerable<SimpleBlueprint> loaded, out int failures)
        {
            int checks = 0, bad = 0;
            var lines = new List<string>();
            void Check(bool ok, string why) { checks++; if (!ok) { bad++; lines.Add("BUFF-SUPPRESS FAILED: " + why); } }
            try
            {
                var part = typeof(UnitPartBuffSuppress);
                // --- native contracts ---
                var isSuppressed = BodyWithLambdas(part, "IsSuppressed");   // the descriptor test runs in a lambda
                Check(new[] { "m_ExcludeBuffs", "m_Buffs", "m_SpellDescriptors", "m_SpellSchools" }.All(f => isSuppressed.Any(m => m.Name == f)) &&
                      Calls(isSuppressed, typeof(MechanicsContext), "get_SpellDescriptor") &&
                      Calls(isSuppressed, typeof(MechanicsContext), "get_SpellSchool"),
                      "IsSuppressed reads the exclusion list, the blueprint list, the descriptor flags and the school of the buff's context");
                Check(Calls(Body(Method(typeof(Buff), "OnAttach")), part, "IsSuppressed"),
                      "every buff asks the part as it is attached (a recast of a suppressed buff is suppressed too)");
                Check(Calls(Body(Method(typeof(Buff), "get_IsEnabled")), typeof(Buff), "get_IsSuppressed"),
                      "a suppressed buff is not enabled");
                var release = Body(Method(typeof(SuppressBuffs), "OnDeactivate"));
                Check(Calls(release, part, "ApplyChanges") && release.Any(m => m.DeclaringType == part && m.Name == "PrepareForRelease"),
                      "the suppressor's end releases its buffs (the game turns them back on itself)");
                var ctor = typeof(MechanicsContext).GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .Single(c => c.GetParameters().Length == 5);
                var contextInit = Body(ctor);
                Check(contextInit.Any(m => m is MethodInfo mi && mi.IsGenericMethod && mi.Name == "GetComponent" &&
                                           mi.GetGenericArguments()[0] == typeof(SpellDescriptorComponent)) &&
                      contextInit.Any(m => m is MethodInfo mi && mi.IsGenericMethod && mi.Name == "GetComponent" &&
                                           mi.GetGenericArguments()[0] == typeof(Kingmaker.UnitLogic.Buffs.Components.AddSpellSchool)),
                      "a context's descriptor adds its blueprint's component to the parent's; its school falls back to AddSpellSchool");
                Check(Calls(Body(typeof(AbilityExecutionContext).GetConstructors(BindingFlags.Instance | BindingFlags.Public)
                                     .Single(c => c.GetParameters().Length == 5)), typeof(AbilityData), "get_SpellSchool"),
                      "an ability's context takes the ability's school (a fusion's own)");

                // --- the reader on real suppressors ---
                var reader = typeof(TacticalEngine).Assembly.GetType("CompanionAI_Pathfinder.Tactics.BuffSuppression") ??
                             throw new TypeLoadException("BuffSuppression");
                Check((bool)reader.GetProperty("Readable", Hidden).GetValue(null), "the part's four private sets are readable by name and type");
                var suppresses = Method(reader, "Suppresses");
                var descriptorOf = Method(reader, "ContextDescriptor");
                var schoolOf = Method(reader, "ContextSchool");
                bool Reads(UnitPartBuffSuppress p, BlueprintBuff buff, SpellDescriptor parent, SpellSchool parentSchool) =>
                    (bool)suppresses.Invoke(null, new object[] { p, buff,
                        (SpellDescriptor)descriptorOf.Invoke(null, new object[] { buff, parent }),
                        (SpellSchool)schoolOf.Invoke(null, new object[] { buff, parentSchool }) });
                UnitPartBuffSuppress Fill(BlueprintScriptableObject suppressor)
                {
                    var p = new UnitPartBuffSuppress();
                    foreach (var c in suppressor.GetComponents<SuppressBuffs>())
                    {
                        if (c.Descriptor != SpellDescriptor.None) p.PrepareForSuppression(c.Descriptor);
                        if (c.Schools != null && c.Schools.Length > 0) p.PrepareForSuppression(c.Schools);
                        foreach (var b in c.Buffs) if (b != null) p.PrepareForSuppression(b);
                        foreach (var b in c.ExcludedBuffs) if (b != null) p.PrepareExceptionForSuppression(b);
                    }
                    return p;
                }

                var all = loaded.OfType<BlueprintScriptableObject>().ToList();
                var everyFlag = Enum.GetValues(typeof(SpellDescriptor)).Cast<SpellDescriptor>().Aggregate(SpellDescriptor.None, (a, f) => a | f);
                var buffs = all.OfType<BlueprintBuff>().ToList();
                var suppressors = all.Where(b => b.GetComponents<SuppressBuffs>().Any()).ToList();
                int listed = 0, listedRead = 0, excluded = 0, excludedRead = 0, byDescriptor = 0, descriptorRead = 0, schools = 0, clean = 0;
                foreach (var s in suppressors)
                {
                    var p = Fill(s);
                    var exclusions = new HashSet<BlueprintBuff>(s.GetComponents<SuppressBuffs>().SelectMany(c => c.ExcludedBuffs).Where(b => b != null));
                    foreach (var b in s.GetComponents<SuppressBuffs>().SelectMany(c => c.Buffs).Where(b => b != null).Distinct())
                    {
                        if (exclusions.Contains(b)) continue;
                        listed++;
                        if (Reads(p, b, SpellDescriptor.None, SpellSchool.None)) listedRead++;
                    }
                    foreach (var b in exclusions)
                    {
                        excluded++;
                        if (!Reads(p, b, everyFlag, SpellSchool.None)) excludedRead++;   // the exclusion wins over every flag
                    }
                    var flags = s.GetComponents<SuppressBuffs>().Aggregate(SpellDescriptor.None, (d, c) => d | c.Descriptor.Value);
                    if (flags != SpellDescriptor.None)
                    {
                        byDescriptor++;
                        // the flag reaches the buff through its own component, or through the ability that applies it
                        var carrier = buffs.FirstOrDefault(b => !exclusions.Contains(b) &&
                            ((b.GetComponent<SpellDescriptorComponent>()?.Descriptor.Value ?? SpellDescriptor.None) & flags) != 0);
                        bool viaOwn = carrier == null || Reads(p, carrier, SpellDescriptor.None, SpellSchool.None);
                        var plain = buffs.First(b => !exclusions.Contains(b) && b.GetComponent<SpellDescriptorComponent>() == null);
                        if (viaOwn && Reads(p, plain, flags, SpellSchool.None)) descriptorRead++;
                    }
                    if (s.GetComponents<SuppressBuffs>().Any(c => c.Schools != null && c.Schools.Length > 0)) schools++;
                    // a buff it does not name, with no descriptor or school, is left alone
                    var names = new HashSet<BlueprintBuff>(s.GetComponents<SuppressBuffs>().SelectMany(c => c.Buffs).Where(b => b != null));
                    var other = buffs.FirstOrDefault(b => !names.Contains(b) && b.GetComponent<SpellDescriptorComponent>() == null &&
                                                          b.GetComponent<SpellComponent>() == null &&
                                                          b.GetComponent<Kingmaker.UnitLogic.Buffs.Components.AddSpellSchool>() == null);
                    if (other != null && !Reads(p, other, SpellDescriptor.None, SpellSchool.None)) clean++;
                }
                lines.Add($"BUFF-SUPPRESS exports: {suppressors.Count} suppressors; {listedRead}/{listed} named buffs, " +
                          $"{excludedRead}/{excluded} exclusions, {descriptorRead}/{byDescriptor} descriptor suppressors read as the game does; " +
                          $"{schools} suppress by school");
                Check(suppressors.Count > 300, $"the exports hold the form and hex suppressors ({suppressors.Count})");
                Check(listed > 0 && listedRead == listed, $"every named buff reads as suppressed ({listedRead}/{listed})");
                Check(excludedRead == excluded, $"every exclusion wins over the flags ({excludedRead}/{excluded})");
                Check(byDescriptor > 0 && descriptorRead == byDescriptor, $"every descriptor suppressor reads its flags ({descriptorRead}/{byDescriptor})");
                Check(clean == suppressors.Count, $"an unrelated buff is never read as suppressed ({clean}/{suppressors.Count})");

                BlueprintScriptableObject Named(string n) => all.FirstOrDefault(b => b.name == n);
                var beastShape = Named("BeastShapeIBuff");
                var enlarge = Named("EnlargePersonBuff") as BlueprintBuff;
                var haste = Named("HasteBuff") as BlueprintBuff;
                Check(beastShape != null && enlarge != null && Reads(Fill(beastShape), enlarge, SpellDescriptor.None, SpellSchool.Transmutation),
                      "Beast Shape I suppresses Enlarge Person (C1: not cast on a wild-shaped ally)");
                Check(beastShape != null && haste != null && !Reads(Fill(beastShape), haste, SpellDescriptor.None, SpellSchool.Transmutation),
                      "Beast Shape I leaves Haste alone");
                var poisonHex = Named("WitchHexMajorAmelioratingPoisonSuppressBuff");
                var poison = buffs.FirstOrDefault(b => ((b.GetComponent<SpellDescriptorComponent>()?.Descriptor.Value ?? SpellDescriptor.None) &
                                                        SpellDescriptor.Poison) != 0);
                Check(poisonHex != null && poison != null && Reads(Fill(poisonHex), poison, SpellDescriptor.None, SpellSchool.None),
                      $"the major Ameliorating hex suppresses a poison ({poison?.name}) (C2: not worth curing while it lasts)");

                // --- the rule ---
                Check(SuppressionRules.Suppressed(true, true, true, true) == false && SuppressionRules.Suppressed(false, false, true, false),
                      "pure rule: the exclusion list wins; any one of list, descriptor or school suppresses");
                Check(!SuppressionRules.StillMatters(true, 30f, 60f) && SuppressionRules.StillMatters(true, 90f, 60f) &&
                      !SuppressionRules.StillMatters(true, float.PositiveInfinity, float.PositiveInfinity) &&
                      SuppressionRules.StillMatters(true, float.NaN, 60f),
                      "pure rule: a suppressed buff matters only if it outlasts its suppressor; unknown keeps the old value");

                // --- compiled routes ---
                var engine = typeof(TacticalEngine);
                Check(Calls(BodyWithLambdas(engine, "ScoreAbility"), reader, "OnArrival") &&
                      Calls(BodyWithLambdas(engine, "ScoreAbility"), reader, "SuppressorName"),
                      "C1: an ally buff is not valued on a recipient that would suppress it, and the reason is logged");
                Check(Calls(BodyWithLambdas(engine, "ToggleValue"), reader, "OnArrival"), "C1: a toggle's grants are not valued on a suppressing bearer");
                Check(Calls(Body(Method(typeof(RealTimeController), "IsWorthKeepingUp")), reader, "OnArrival"),
                      "C1: no out-of-combat upkeep for a buff the unit would suppress");
                Check(Calls(BodyWithLambdas(typeof(AbilityProfile), "WouldRemoveSomething"), reader, "StillMatters"),
                      "C2: a cleanse gains nothing from a harmful buff suppressed for the rest of its time");
                Check(Calls(Body(Method(engine, "HasSomethingToDispel")), reader, "StillMatters") &&
                      Calls(Body(Method(engine, "DispelValue")), reader, "StillMatters"),
                      "C2: dispels skip buffs suppressed for the rest of their time");
                Check(Calls(Body(Method(reader, "StillMatters")), typeof(SuppressionRules), "StillMatters") &&
                      Calls(Body(suppresses), typeof(SuppressionRules), "Suppressed"),
                      "the game side defers to the unit-tested rule");
            }
            catch (Exception ex)
            {
                Check(false, $"{ex.GetType().Name}: {(ex as TargetInvocationException)?.InnerException?.Message ?? ex.Message}");
            }
            lines.Insert(0, $"BUFF-SUPPRESS {checks} checks, failures {bad}; native contracts, the reader on real suppressors and compiled " +
                            "routes; no actor, buff instance, context or Unity object");
            failures = bad;
            return lines;
        }
    }
}
