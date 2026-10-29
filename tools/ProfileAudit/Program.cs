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
                Console.Error.WriteLine("Usage: ProfileAudit [--allow-failures N] [--optional-provider TabletopTweaks-Core.dll] <dump folders...> <report.txt>");
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
            if (failed > 0)
            {
                string partial = $"PARTIAL: loaded {loaded.Count}, failed {failed}, allowed {options.AllowFailures}. Failed blueprints:";
                File.WriteAllText(output, partial + Environment.NewLine + string.Join(Environment.NewLine, loadErrors) +
                    Environment.NewLine + Environment.NewLine + File.ReadAllText(output));
                Console.WriteLine(partial);
            }
            if (providerLines.Count > 0)
                File.WriteAllText(output, string.Join(Environment.NewLine, providerLines) + Environment.NewLine + Environment.NewLine + File.ReadAllText(output));
            return 0;
        }
    }
}
