using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.JsonSystem;

namespace ProfileAudit
{
    internal static class Program
    {
        /// <summary>The game fills its type-by-guid table at startup; here: every TypeId type of the game's assemblies.</summary>
        private static void RegisterTypes()
        {
            var binder = (GuidClassBinder)Json.Serializer.Binder;
            int n = 0;
            foreach (var asm in new[] { typeof(SimpleBlueprint).Assembly })
            {
                Type[] types;
                try { types = asm.GetTypes(); }
                catch (System.Reflection.ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t != null).ToArray(); }
                foreach (var t in types)
                {
                    TypeIdAttribute id;
                    try { id = t.GetCustomAttributes(typeof(TypeIdAttribute), false).Cast<TypeIdAttribute>().SingleOrDefault(); }
                    catch { continue; }
                    if (id == null) continue;
                    binder.AddToCache(t, id.GuidString);
                    n++;
                }
            }
            Console.WriteLine($"types registered: {n}");
            // Display strings, Unity assets (icons, prefabs) and visual curves need the engine; the audit needs none of them.
            var converters = Json.Serializer.Converters;
            for (int i = converters.Count - 1; i >= 0; i--)
            {
                var name = converters[i].GetType().Name;
                if (name == "SharedStringConverter" || name == "UnityObjectConverter" || name == "AnimationCurveConverter" || name == "GradientConverter") converters[i] = new SkipConverter(converters[i]);
            }
        }

        /// <summary>Reads past the value the wrapped converter would turn into a Unity object; null instead.</summary>
        private sealed class SkipConverter : Newtonsoft.Json.JsonConverter
        {
            private readonly Newtonsoft.Json.JsonConverter m_Inner;
            public SkipConverter(Newtonsoft.Json.JsonConverter inner) { m_Inner = inner; }
            public override bool CanConvert(Type objectType) => m_Inner.CanConvert(objectType);
            public override bool CanWrite => false;
            public override object ReadJson(Newtonsoft.Json.JsonReader reader, Type objectType, object existingValue, Newtonsoft.Json.JsonSerializer serializer)
            {
                reader.Skip();
                return null;
            }
            public override void WriteJson(Newtonsoft.Json.JsonWriter writer, object value, Newtonsoft.Json.JsonSerializer serializer) { }
        }

        private static int Main(string[] args)
        {
            // Unity Mod Manager's assembly version differs from the one the mod was built against: load what is there.
            AppDomain.CurrentDomain.AssemblyResolve += (o, e) =>
            {
                var file = Path.Combine(AppContext.BaseDirectory, new System.Reflection.AssemblyName(e.Name).Name + ".dll");
                return File.Exists(file) ? System.Reflection.Assembly.LoadFrom(file) : null;
            };
            AppDomain.CurrentDomain.UnhandledException += (o, e) => Console.WriteLine("UNHANDLED " + e.ExceptionObject);
            bool trace = Environment.GetEnvironmentVariable("PA_TRACE") == "1";
            if (trace)
                AppDomain.CurrentDomain.FirstChanceException += (o, e) =>
                {
                    if (e.Exception is System.Security.SecurityException) Console.WriteLine("FIRST CHANCE at\n" + Environment.StackTrace);
                };
            if (!AuditOptions.TryParse(args, out var options, out var optionError))
            {
                Console.Error.WriteLine(optionError);
                Console.Error.WriteLine("Usage: ProfileAudit [--allow-failures N] [--optional-provider TabletopTweaks-Core.dll] [--blocklist-checks] <dump folders...> <report.txt>");
                return 2;
            }
            var output = options.Output;
            var roots = options.Roots;
            RegisterTypes();
            var providerLines = new List<string>();
            if (options.OptionalProvider != null)
            {
                try { providerLines = OptionalProviderCheck.Run(options.OptionalProvider); }
                catch (Exception ex)
                {
                    var failure = $"INCOMPLETE: optional provider check failed: {ex.GetType().Name}: {ex.Message}";
                    File.WriteAllText(output, failure + Environment.NewLine + ex);
                    Console.Error.WriteLine(failure);
                    return 1;
                }
                foreach (var line in providerLines) Console.WriteLine(line);
                if (roots.Count == 0)
                {
                    File.WriteAllLines(output, providerLines);
                    return 0;
                }
            }
            var loaded = new Dictionary<BlueprintGuid, SimpleBlueprint>();
            int failed = 0;
            var loadErrors = new List<string>();
            foreach (var root in roots)
                foreach (var file in Directory.EnumerateFiles(root, "*.jbp", SearchOption.AllDirectories))
                {
                    if (trace) { Console.WriteLine("load " + file); Console.Out.Flush(); }
                    try
                    {
                        var w = BlueprintJsonWrapper.Load(file);
                        if (w?.Data == null) throw new InvalidDataException("blueprint wrapper contains no data");
                        loaded[w.Data.AssetGuid] = w.Data;
                        Audit.Paths[w.Data] = Path.GetRelativePath(root, file);
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        loadErrors.Add($"{file}: {ex.GetType().Name}: {ex.Message}");
                    }
                    if (loaded.Count == 200) Console.WriteLine("200 loaded...");
                }
            Console.WriteLine($"loaded {loaded.Count}, failed {failed}");
            foreach (var e in loadErrors) Console.WriteLine("  " + e);
            // Explicit small allowances permit unrelated failures, but cannot turn an empty/mass-failed dump
            // into a successful zero-ability audit. Partial reports always retain the full failure inventory.
            if (!options.CanAudit(loaded.Count, failed))
            {
                var incomplete = $"INCOMPLETE: loaded {loaded.Count}, failed {failed}, allowed {options.AllowFailures}; no ability verdicts generated.";
                File.WriteAllText(output, incomplete + Environment.NewLine + string.Join(Environment.NewLine, loadErrors));
                Console.Error.WriteLine(incomplete);
                return 1;
            }
            foreach (var kv in loaded) ResourcesLibrary.BlueprintsCache.AddCachedBlueprint(kv.Key, kv.Value);
            foreach (var bp in loaded.Values)
                try { bp.OnEnable(); } catch { }
            Audit.Run(loaded.Values.ToList(), output);
            // ★ user-ability-blacklist: the mod's matching on these blueprints, at the head of the report.
            int blocklistFailures = 0;
            if (options.BlocklistChecks)
            {
                var blocklistLines = BlocklistCheck.Run(loaded.Values, out blocklistFailures);
                foreach (var line in blocklistLines) Console.WriteLine(line);
                File.WriteAllText(output, string.Join(Environment.NewLine, blocklistLines) + Environment.NewLine + Environment.NewLine + File.ReadAllText(output));
            }
            // ★ v0.3.28: on-hit ability damage (a Shadow's Strength touch) on these blueprints.
            var onHitLines = OnHitCheck.Run(loaded.Values, out int onHitFailures);
            foreach (var line in onHitLines) Console.WriteLine(line);
            File.WriteAllText(output, string.Join(Environment.NewLine, onHitLines) + Environment.NewLine + Environment.NewLine + File.ReadAllText(output));
            blocklistFailures += onHitFailures;
            // ★ v0.3.28 (codex review): the actual layer reader and loaded plain-blast full/half branches.
            var layerLines = RicochetLayerCheck.Run(loaded.Values, out int layerFailures);
            foreach (var line in layerLines) Console.WriteLine(line);
            File.WriteAllText(output, string.Join(Environment.NewLine, layerLines) + Environment.NewLine + Environment.NewLine + File.ReadAllText(output));
            blocklistFailures += layerFailures;
            // ★ v0.3.28 test 6: the rank-increment reader (Enveloping Winds, Shroud of Water).
            var rankLines = RankTalentCheck.Run(loaded.Values, out int rankFailures);
            foreach (var line in rankLines) Console.WriteLine(line);
            File.WriteAllText(output, string.Join(Environment.NewLine, rankLines) + Environment.NewLine + Environment.NewLine + File.ReadAllText(output));
            blocklistFailures += rankFailures;
            // ★ v0.3.28 test 7: Create Quiver found by the quiver buff the kineticist part names.
            var quiverLines = QuiverBindingCheck.Run(loaded.Values, out int quiverFailures);
            foreach (var line in quiverLines) Console.WriteLine(line);
            File.WriteAllText(output, string.Join(Environment.NewLine, quiverLines) + Environment.NewLine + Environment.NewLine + File.ReadAllText(output));
            blocklistFailures += quiverFailures;
            // ★ v0.3.28 test 10: Soul Power's burn relief and Overflow rank wiring.
            var soulLines = SoulPowerCheck.Run(loaded.Values, out int soulFailures);
            foreach (var line in soulLines) Console.WriteLine(line);
            File.WriteAllText(output, string.Join(Environment.NewLine, soulLines) + Environment.NewLine + Environment.NewLine + File.ReadAllText(output));
            blocklistFailures += soulFailures;
            // ★ v0.3.28 test 11: which damage abilities share one execution context across targets.
            var contextLines = DamageContextCheck.Run(loaded.Values, out int contextFailures);
            foreach (var line in contextLines) Console.WriteLine(line);
            File.WriteAllText(output, string.Join(Environment.NewLine, contextLines) + Environment.NewLine + Environment.NewLine + File.ReadAllText(output));
            blocklistFailures += contextFailures;
            // ★ v0.3.28 test 13: ability damage and negative levels read as such.
            var drainLines = DrainCheck.Run(loaded.Values, out int drainFailures);
            foreach (var line in drainLines) Console.WriteLine(line);
            File.WriteAllText(output, string.Join(Environment.NewLine, drainLines) + Environment.NewLine + Environment.NewLine + File.ReadAllText(output));
            blocklistFailures += drainFailures;
            // ★ v0.3.28 test 14: weapon-hit and swarm-round afflictions.
            var afflictionLines = AfflictionCheck.Run(loaded.Values, out int afflictionFailures);
            foreach (var line in afflictionLines) Console.WriteLine(line);
            File.WriteAllText(output, string.Join(Environment.NewLine, afflictionLines) + Environment.NewLine + Environment.NewLine + File.ReadAllText(output));
            blocklistFailures += afflictionFailures;
            // ★ v0.3.28 test 15: swarm spaces.
            var swarmLines = SwarmAreaCheck.Run(loaded.Values, out int swarmFailures);
            foreach (var line in swarmLines) Console.WriteLine(line);
            File.WriteAllText(output, string.Join(Environment.NewLine, swarmLines) + Environment.NewLine + Environment.NewLine + File.ReadAllText(output));
            blocklistFailures += swarmFailures;
            // ★ v0.3.30 test 2: facts whose bonus follows an ability score.
            var statLines = StatLinkCheck.Run(loaded.Values, out int statFailures);
            foreach (var line in statLines) Console.WriteLine(line);
            File.WriteAllText(output, string.Join(Environment.NewLine, statLines) + Environment.NewLine + Environment.NewLine + File.ReadAllText(output));
            blocklistFailures += statFailures;
            // ★ v0.3.30 (competence): preserve each modeled area-grant branch/save path.
            var areaPathLines = AreaGrantPathCheck.Run(out int areaPathFailures);
            foreach (var line in areaPathLines) Console.WriteLine(line);
            File.WriteAllText(output, string.Join(Environment.NewLine, areaPathLines) + Environment.NewLine + Environment.NewLine + File.ReadAllText(output));
            blocklistFailures += areaPathFailures;
            // ★ v0.3.30 (competence): the compiled production route shares the save math exercised by unit tests.
            var saveBoostLines = SaveBoostCheck.Run(out int saveBoostFailures);
            foreach (var line in saveBoostLines) Console.WriteLine(line);
            File.WriteAllText(output, string.Join(Environment.NewLine, saveBoostLines) + Environment.NewLine + Environment.NewLine + File.ReadAllText(output));
            blocklistFailures += saveBoostFailures;
            // ★ v0.3.30 competence: actual native burn-change feature graphs and their marginal consequences.
            var burnLines = BurnConsequenceCheck.Run(loaded.Values, out int burnFailures);
            foreach (var line in burnLines) Console.WriteLine(line);
            File.WriteAllText(output, string.Join(Environment.NewLine, burnLines) + Environment.NewLine + Environment.NewLine + File.ReadAllText(output));
            blocklistFailures += burnFailures;
            // ★ v0.3.30 competence3: buffs that end invisibility (Faerie Fire, Glitterdust).
            var viewLines = NativeViewCheck.Run(out int viewFailures);
            foreach (var line in viewLines) Console.WriteLine(line);
            File.AppendAllText(output, Environment.NewLine + string.Join(Environment.NewLine, viewLines));
            blocklistFailures += viewFailures;
            var revealLines = RevealCheck.Run(loaded.Values, out int revealFailures);
            foreach (var line in revealLines) Console.WriteLine(line);
            File.WriteAllText(output, string.Join(Environment.NewLine, revealLines) + Environment.NewLine + Environment.NewLine + File.ReadAllText(output));
            blocklistFailures += revealFailures;
            // ★ v0.3.30 competence3: cast-time damage bonuses (Throw Anything).
            var castBonusLines = CastBonusCheck.Run(loaded.Values, out int castBonusFailures);
            foreach (var line in castBonusLines) Console.WriteLine(line);
            File.WriteAllText(output, string.Join(Environment.NewLine, castBonusLines) + Environment.NewLine + Environment.NewLine + File.ReadAllText(output));
            blocklistFailures += castBonusFailures;
            if (failed > 0)
            {
                string partial = $"PARTIAL: loaded {loaded.Count}, failed {failed}, allowed {options.AllowFailures}. Failed blueprints:";
                File.WriteAllText(output, partial + Environment.NewLine + string.Join(Environment.NewLine, loadErrors) +
                    Environment.NewLine + Environment.NewLine + File.ReadAllText(output));
                Console.WriteLine(partial);
            }
            if (providerLines.Count > 0)
                File.WriteAllText(output, string.Join(Environment.NewLine, providerLines) + Environment.NewLine + Environment.NewLine + File.ReadAllText(output));
            return blocklistFailures > 0 ? 1 : 0;
        }
    }
}
