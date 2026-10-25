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
            var output = args[args.Length - 1];
            bool kineticChecks = args.Take(args.Length - 1).Contains("--kinetic-checks");
            var roots = args.Take(args.Length - 1).Where(a => a != "--kinetic-checks").ToList();
            RegisterTypes();
            var loaded = new Dictionary<BlueprintGuid, SimpleBlueprint>();
            int failed = 0;
            var firstErrors = new List<string>();
            foreach (var root in roots)
                foreach (var file in Directory.EnumerateFiles(root, "*.jbp", SearchOption.AllDirectories))
                {
                    if (trace) { Console.WriteLine("load " + file); Console.Out.Flush(); }
                    try
                    {
                        var w = BlueprintJsonWrapper.Load(file);
                        if (w?.Data == null) continue;
                        loaded[w.Data.AssetGuid] = w.Data;
                        Audit.Paths[w.Data] = Path.GetRelativePath(root, file);
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        if (firstErrors.Count < 2) firstErrors.Add($"{Path.GetFileName(file)}: {ex}");
                    }
                    if (loaded.Count == 200) Console.WriteLine("200 loaded...");
                }
            Console.WriteLine($"loaded {loaded.Count}, failed {failed}");
            foreach (var e in firstErrors) Console.WriteLine("  " + e);
            // ★ v0.3.22: failed deserialization is not a zero-ability success. In particular a missing Unity
            // dependency can prevent almost the whole dump from loading while Audit.Run reports zero errors.
            // Replace any previous report so it cannot be mistaken for evidence from this incomplete run.
            if (failed > 0 || loaded.Count == 0)
            {
                var incomplete = $"INCOMPLETE: loaded {loaded.Count}, failed {failed}; no ability verdicts generated.";
                File.WriteAllText(output, incomplete + Environment.NewLine + string.Join(Environment.NewLine, firstErrors));
                Console.Error.WriteLine(incomplete);
                return 1;
            }
            foreach (var kv in loaded) ResourcesLibrary.BlueprintsCache.AddCachedBlueprint(kv.Key, kv.Value);
            foreach (var bp in loaded.Values)
                try { bp.OnEnable(); } catch { }
            if (kineticChecks) KineticistChecks.Run(loaded.Values.ToList());
            Audit.Run(loaded.Values.ToList(), output);
            return 0;
        }
    }
}
