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

        internal static MethodInfo Method(Type type, string name, params Type[] args)
        {
            var found = type.GetMethods(Hidden).Where(m => m.Name == name &&
                            (args.Length == 0 || m.GetParameters().Select(p => p.ParameterType).SequenceEqual(args))).ToList();
            if (found.Count != 1) throw new MissingMethodException(type.FullName, $"{name} ({found.Count} matches)");
            return found[0];
        }

        private static List<MemberInfo> Body(MethodBase method) => SaveBoostCheck.ReferencedMembers(method).ToList();

        /// <summary>A method's body and the bodies of its lambdas and local functions (compiler-generated, named after it).</summary>
        internal static List<MemberInfo> BodyWithLambdas(Type type, string name)
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
                var abilityInit = Body(typeof(AbilityExecutionContext).GetConstructors(BindingFlags.Instance | BindingFlags.Public)
                    .Single(c => c.GetParameters().Length == 5));
                Check(Calls(abilityInit, typeof(AbilityData), "get_SpellSchool"),
                      "an ability's context takes the ability's school (a fusion's own)");
                Check(abilityInit.Any(m => m is FieldInfo && m.DeclaringType == typeof(AbilityData) && m.Name == "Blueprint") &&
                      abilityInit.Any(m => m is ConstructorInfo && m.DeclaringType == typeof(MechanicsContext)) &&
                      !Calls(abilityInit, typeof(AbilityData), "get_SpellDescriptor") &&
                      !Calls(abilityInit, typeof(MechanicsContext), "set_SpellDescriptor"),
                      "native casts inherit the blueprint descriptor; they do not replace it with AbilityData's fused descriptor union");

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
                var filter = Method(reader, "Filter");
                List<BlueprintBuff> Filter(UnitPartBuffSuppress part, List<BlueprintBuff> source, out BlueprintBuff first)
                {
                    object[] args = { part, source, SpellDescriptor.None, SpellSchool.Transmutation, null };
                    var result = (List<BlueprintBuff>)filter.Invoke(null, args);
                    first = (BlueprintBuff)args[4];
                    return result;
                }
                var shapePart = Fill(beastShape);
                var unchanged = new List<BlueprintBuff> { haste };
                Check(ReferenceEquals(Filter(shapePart, unchanged, out var none), unchanged) && none == null,
                      "C1 list filter retains the original list when nothing is suppressed");
                var mixed = new List<BlueprintBuff> { haste, enlarge, haste, enlarge };
                var filtered = Filter(shapePart, mixed, out var first);
                Check(first == enlarge && filtered.SequenceEqual(new[] { haste, haste }) && mixed.Count == 4,
                      "C1 list filter removes every suppressed buff, preserves order and leaves its input untouched");
                Check(Filter(shapePart, new List<BlueprintBuff> { enlarge, enlarge }, out first).Count == 0 && first == enlarge,
                      "C1 list filter identifies an entirely suppressed cast and its first reason");
                shapePart.PrepareExceptionForSuppression(enlarge);
                var exempt = new List<BlueprintBuff> { enlarge, haste };
                Check(ReferenceEquals(Filter(shapePart, exempt, out first), exempt) && first == null,
                      "C1 list filter honors native global exclusions without allocating a copy");
                var poisonHex = Named("WitchHexMajorAmelioratingPoisonSuppressBuff");
                var poison = buffs.FirstOrDefault(b => ((b.GetComponent<SpellDescriptorComponent>()?.Descriptor.Value ?? SpellDescriptor.None) &
                                                        SpellDescriptor.Poison) != 0);
                Check(poisonHex != null && poison != null && Reads(Fill(poisonHex), poison, SpellDescriptor.None, SpellSchool.None),
                      $"the major Ameliorating hex suppresses a poison ({poison?.name}) (C2: not worth curing while it lasts)");

                // --- the rule ---
                Check(SuppressionRules.Suppressed(true, true, true, true) == false && SuppressionRules.Suppressed(false, false, true, false),
                      "pure rule: the exclusion list wins; any one of list, descriptor or school suppresses");
                Check(SuppressionRules.ActiveShare(true, 5f, 10f, 3f) == 0f && SuppressionRules.ActiveShare(true, 20f, 10f, 30f) == 0.5f &&
                      SuppressionRules.ActiveShare(true, float.PositiveInfinity, float.PositiveInfinity, 3f) == 0f &&
                      SuppressionRules.ActiveShare(true, float.NaN, 10f, 3f) == 1f && SuppressionRules.ActiveShare(false, 1f, 10f, 3f) == 1f,
                      "pure rule (F3): a suppressed buff counts the share of its rounds it acts within the horizon; unknown keeps the old value");
                Check(SuppressionRules.ActiveShare(true, 61f / 6f, 60f / 6f, 3f) == 0f,
                      "F3: Codex's C-R1 case - 61 s left under 60 s of suppression earns nothing within an 18 s horizon");
                Check(SuppressionRules.ActiveShare(true, 0f, float.NaN, 3f) == 0f &&
                      SuppressionRules.ActiveShare(true, 5f, float.NaN, 0f) == 0f &&
                      SuppressionRules.ActiveShare(true, 5f, float.NaN, 3f) == 1f,
                      "F3: unknown suppression retains value only when there is still a positive buff lifetime and horizon");
                Check(SuppressionRules.ActiveRounds(10f, 4f, 6f) == 2f && SuppressionRules.ActiveRounds(10f, float.PositiveInfinity, 6f) == 0f &&
                      SuppressionRules.ActiveRounds(3f, 0f, 6f) == 3f,
                      "pure rule: the rounds a buff acts are its rounds left in the horizon less the suppressed ones");

                // --- compiled routes ---
                var engine = typeof(TacticalEngine);
                Check(Calls(BodyWithLambdas(engine, "ScoreAbility"), reader, "FilterOnArrival") &&
                      Calls(BodyWithLambdas(engine, "ScoreAbility"), reader, "SuppressorName"),
                      "C1: an ally buff is not valued on a recipient that would suppress it, and the reason is logged");
                Check(Calls(BodyWithLambdas(engine, "ToggleValue"), reader, "OnArrival"), "C1: a toggle's grants are not valued on a suppressing bearer");
                var toggleValue = BodyWithLambdas(engine, "ToggleValue");
                Check(Calls(toggleValue, reader, "ContextDescriptor") && Calls(toggleValue, reader, "ContextSchool") &&
                      Calls(toggleValue, typeof(MechanicsContext), "get_SpellDescriptor") &&
                      Calls(toggleValue, typeof(MechanicsContext), "get_SpellSchool"),
                      "C1: aura grants inherit the toggle context through the area instead of assuming an empty parent");
                // AreaEffectsController.Spawn's CloneFor call was inspected in the supplied native IL. Reflecting its
                // entire body also resolves unrelated scene types from firstpass, which these inputs do not include.
                // Test the child-context contract directly; do not turn an absent optional scene assembly into a skip.
                Check(Body(Method(typeof(MechanicsContext), "CloneFor")).Any(m =>
                          m is ConstructorInfo && m.DeclaringType == typeof(MechanicsContext)),
                      "native context cloning constructs a child context for the supplied blueprint");
                var plainGrant = buffs.First(b => b.GetComponent<SpellDescriptorComponent>() == null &&
                    b.GetComponent<SpellComponent>() == null && b.GetComponent<Kingmaker.UnitLogic.Buffs.Components.AddSpellSchool>() == null);
                var inheritedPart = new UnitPartBuffSuppress();
                inheritedPart.PrepareForSuppression(SpellDescriptor.MindAffecting);
                Check(!Reads(inheritedPart, plainGrant, SpellDescriptor.None, SpellSchool.None) &&
                      Reads(inheritedPart, plainGrant, SpellDescriptor.MindAffecting, SpellSchool.None),
                      "a plain grant can be suppressed solely by its inherited context; an empty parent changes the answer");
                Check(Calls(Body(Method(typeof(RealTimeController), "IsWorthKeepingUp")), reader, "OnArrival"),
                      "C1: no out-of-combat upkeep for a buff the unit would suppress");
                Check(Calls(BodyWithLambdas(typeof(AbilityProfile), "RemovedActiveShare"), reader, "ActiveShare") &&
                      Calls(BodyWithLambdas(engine, "ScoreAbility"), typeof(AbilityProfile), "RemovedActiveShare"),
                      "C2/F3: a cleanse earns the share of the horizon a removed harmful buff acts after its suppressor ends");
                Check(Calls(Body(Method(engine, "HasSomethingToDispel")), reader, "ActiveShare"),
                      "C2/F3: the dispel fallback skips buffs suppressed for the rest of the horizon");
                // ★ coverage4: the full overload carries the rule (the short one forwards to it).
                var dispelValue = Body(engine.GetMethods(Hidden).Where(m => m.Name == "DispelValue")
                                             .OrderByDescending(m => m.GetParameters().Length).First());
                Check(Calls(dispelValue, reader, "SuppressedSeconds") && Calls(dispelValue, typeof(SuppressionRules), "ActiveRounds"),
                      "C2: a dispel's worth counts only the rounds a buff acts after its suppressor ends");
                Check(Calls(Body(Method(reader, "ActiveShare")), typeof(SuppressionRules), "ActiveShare") &&
                      Calls(Body(Method(reader, "ActiveShare")), reader, "SuppressedSeconds") &&
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
