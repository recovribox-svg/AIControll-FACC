// Read metadata and IL; never load a supplied assembly into the CLR or invoke a mod initializer.
using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BlueprintAssemblyIndex;

internal record Creation(string Owner, string Method, int Offset, string Factory, string TypeHint, string[] IdentifierCandidates, string[] ComponentTypeArguments);
internal record Registered(string Type, string BaseType, string? TypeId);
internal record Report(string File, string Assembly, string Mvid, string Sha256, Registered[] Types, Creation[] CreationSites);

internal static class Program
{
    private static readonly Regex Identifier = new("^[A-Za-z][A-Za-z0-9_.-]{2,99}$", RegexOptions.CultureInvariant);
    private static readonly Regex GuidLiteral = new("^[a-fA-F0-9]{8}-?[a-fA-F0-9]{4}-?[a-fA-F0-9]{4}-?[a-fA-F0-9]{4}-?[a-fA-F0-9]{12}$", RegexOptions.CultureInvariant);

    private static IEnumerable<TypeDefinition> All(IEnumerable<TypeDefinition> source) =>
        source.SelectMany(t => new[] { t }.Concat(All(t.NestedTypes)));

    private static string? Hint(MethodReference method)
    {
        if (method.Name.Contains("CreateBlueprint", StringComparison.Ordinal))
        {
            if (method is GenericInstanceMethod g && g.GenericArguments.FirstOrDefault()?.Name.StartsWith("Blueprint", StringComparison.Ordinal) == true)
                return g.GenericArguments[0].Name;
            if (method.ReturnType.Name.StartsWith("Blueprint", StringComparison.Ordinal)) return method.ReturnType.Name;
        }
        if (method.Name == "New" && method.DeclaringType.Name.EndsWith("Configurator", StringComparison.Ordinal))
            return method.DeclaringType.Name; // Builder hint, not a reconstructed native blueprint.
        return null;
    }

    internal static Report Inspect(string path)
    {
        using var module = ModuleDefinition.ReadModule(path);
        var types = All(module.Types).OrderBy(t => t.FullName, StringComparer.Ordinal).ToArray();
        var registrations = types.Where(t => !t.Name.StartsWith('<')).Select(t => new Registered(t.FullName,
            t.BaseType?.FullName ?? "", t.CustomAttributes.FirstOrDefault(a => a.AttributeType.Name == "TypeIdAttribute")
                ?.ConstructorArguments.FirstOrDefault().Value?.ToString())).ToArray();
        var sites = new List<Creation>();
        foreach (var type in types)
        foreach (var method in type.Methods.Where(m => m.HasBody))
        {
            var instructions = method.Body.Instructions;
            var components = instructions.Select(i => i.Operand).OfType<GenericInstanceMethod>()
                .Where(m => m.Name.StartsWith("AddComponent", StringComparison.Ordinal))
                .SelectMany(m => m.GenericArguments.Select(t => t.FullName)).Distinct().OrderBy(x => x, StringComparer.Ordinal).ToArray();
            for (int i = 0; i < instructions.Count; i++)
            {
                var instruction = instructions[i];
                if (instruction.OpCode.Code is not (Code.Call or Code.Callvirt) || instruction.Operand is not MethodReference target) continue;
                var hint = Hint(target);
                if (hint == null) continue;
                var strings = instructions.Skip(Math.Max(0, i - 24)).Take(Math.Min(i, 24))
                    .Where(x => x.OpCode.Code == Code.Ldstr).Select(x => (string)x.Operand)
                    .Where(x => Identifier.IsMatch(x) || GuidLiteral.IsMatch(x)).Distinct().ToArray();
                sites.Add(new Creation(type.FullName, method.Name, instruction.Offset, target.FullName, hint, strings, components));
            }
        }
        return new Report(Path.GetFileName(path), module.Assembly.FullName, module.Mvid.ToString(),
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant(), registrations, sites.ToArray());
    }

    private static int Main(string[] args)
    {
        if (args.Length == 1 && args[0] == "--self-test")
        {
            var report = Inspect(typeof(Program).Assembly.Location);
            var positive = report.CreationSites.Where(s => s.Owner.EndsWith("SyntheticFixture", StringComparison.Ordinal)).ToArray();
            bool created = positive.Any(s => s.TypeHint == "BlueprintCharacterClass" && s.IdentifierCandidates.Contains("FixtureClass"));
            bool component = positive.Any(s => s.ComponentTypeArguments.Any(x => x.EndsWith("FixtureComponent", StringComparison.Ordinal)));
            bool noLookup = positive.All(s => !s.Factory.Contains("GetBlueprint", StringComparison.Ordinal));
            bool registration = report.Types.Any(t => t.Type.EndsWith("FixtureComponent", StringComparison.Ordinal) && t.TypeId == "11111111111111111111111111111111");
            Console.WriteLine($"ASSEMBLY-INDEX self-test: creation={created}, typed-component={component}, lookup-exclusion={noLookup}, type-id={registration}");
            return created && component && noLookup && registration ? 0 : 1;
        }
        if (args.Length < 2) { Console.Error.WriteLine("Usage: AssemblyIndex <output.json> <assembly.dll> [...] | --self-test"); return 2; }
        var output = Path.GetFullPath(args[0]);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!Path.GetExtension(output).Equals(".json", StringComparison.OrdinalIgnoreCase) ||
            args.Skip(1).Any(p => Path.GetFullPath(p).Equals(output, comparison)))
        { Console.Error.WriteLine("Output must be a JSON report, separate from the input assemblies."); return 2; }
        var reports = args.Skip(1).OrderBy(x => Path.GetFileName(x), StringComparer.Ordinal).Select(Inspect).ToArray();
        var result = new { SchemaVersion = 1, Scope = "Read-only IL construction hints and type identity, not executed/generated blueprints or enabled-patch/actor/compatibility evidence. Literal candidates use a bounded lookback; components are typed arguments in the same method, not proof of assignment to that object.", Assemblies = reports };
        File.WriteAllText(output, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }) + "\n");
        Console.WriteLine($"ASSEMBLY-INDEX: {reports.Length} assemblies, {reports.Sum(r => r.CreationSites.Length)} construction sites");
        return 0;
    }
}

// Synthetic IL inputs used only by the metadata self-test. They are never invoked by the inspector.
[AttributeUsage(AttributeTargets.Class)]
internal sealed class TypeIdAttribute(string value) : Attribute { public string Value { get; } = value; }
internal sealed class BlueprintCharacterClass { }
[TypeId("11111111111111111111111111111111")]
internal sealed class FixtureComponent { }
internal static class SyntheticFixture
{
    internal static void Build()
    {
        _ = CreateBlueprint<BlueprintCharacterClass>("FixtureClass");
        AddComponent<FixtureComponent>();
        _ = GetBlueprint<BlueprintCharacterClass>("ExistingClass");
    }
    private static T CreateBlueprint<T>(string name) where T : new() => new();
    private static void AddComponent<T>() { }
    private static T GetBlueprint<T>(string name) where T : new() => new();
}
