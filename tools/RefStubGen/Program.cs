// Generates compile-only reference stubs for assemblies referenced by a source assembly (Assembly-CSharp),
// reconstructed from the source's TypeRef / MemberRef metadata. Usage: stubgen <source.dll> <outDir> <asmName>...
using Mono.Cecil;
using Mono.Cecil.Cil;

var src = ModuleDefinition.ReadModule(args[0]);
var outDir = args[1];
var targets = new HashSet<string>(args.Skip(2));

// Force reading every signature/body so Cecil marks TypeReference.IsValueType, and record static field usage.
var staticFields = new HashSet<string>();
var interfaces = new HashSet<string>();
var baseOfDefs = new HashSet<string>();
foreach (var t in src.GetTypes()) {
  foreach (var i in t.Interfaces) interfaces.Add(Key(i.InterfaceType));
  if (t.BaseType != null) baseOfDefs.Add(Key(t.BaseType));
  foreach (var f in t.Fields) _ = f.FieldType;
  foreach (var m in t.Methods) {
    _ = m.ReturnType; foreach (var p in m.Parameters) _ = p.ParameterType;
    if (!m.HasBody) continue;
    foreach (var ins in m.Body.Instructions) {
      if (ins.Operand is FieldReference fr && (ins.OpCode == OpCodes.Ldsfld || ins.OpCode == OpCodes.Stsfld || ins.OpCode == OpCodes.Ldsflda))
        staticFields.Add(Key(fr.DeclaringType) + "::" + fr.Name);
      if (ins.Operand is MethodReference mr && mr.DeclaringType != null) { _ = mr.ReturnType; }
      if (ins.Operand is TypeReference tr && (ins.OpCode == OpCodes.Constrained)) { }
    }
    foreach (var v in m.Body.Variables) _ = v.VariableType;
  }
}

static TypeReference Elem(TypeReference t) => t is GenericInstanceType g ? g.ElementType : t;
static string Key(TypeReference t) { t = Elem(t); return t.DeclaringType != null ? Key(t.DeclaringType) + "/" + t.Name : t.Namespace + "." + t.Name; }
static string ScopeName(TypeReference t) { t = Elem(t); while (t.DeclaringType != null) t = t.DeclaringType; return t.Scope?.Name; }

// Collect types per target assembly
var refsByAsm = new Dictionary<string, Dictionary<string, TypeReference>>();
var valueTypes = new HashSet<string>();
void AddType(TypeReference t) {
  if (!(t is TypeSpecification) && !(t is GenericParameter) && t.IsValueType) valueTypes.Add(Key(t));
  t = Elem(t);
  if (t is TypeSpecification || t is GenericParameter) return;
  var sn = ScopeName(t);
  if (sn == null || !targets.Contains(sn)) return;
  if (t.DeclaringType != null) AddType(t.DeclaringType);
  if (!refsByAsm.TryGetValue(sn, out var d)) refsByAsm[sn] = d = new();
  var k = Key(t);
  if (!d.ContainsKey(k) || (t.IsValueType && !d[k].IsValueType)) d[k] = t;
}
foreach (var t in src.GetTypeReferences()) AddType(t);
var memberRefs = src.GetMemberReferences().ToList();
void Walk(TypeReference t) {
  switch (t) {
    case GenericInstanceType g: if (g.IsValueType) valueTypes.Add(Key(g.ElementType)); AddType(g.ElementType); foreach (var a in g.GenericArguments) Walk(a); break;
    case TypeSpecification s: Walk(s.ElementType); break;
    case GenericParameter: break;
    default: AddType(t); break;
  }
}
foreach (var t in src.GetTypes()) {
  foreach (var f in t.Fields) Walk(f.FieldType);
  foreach (var m in t.Methods) {
    Walk(m.ReturnType); foreach (var p in m.Parameters) Walk(p.ParameterType);
    if (!m.HasBody) continue;
    foreach (var v in m.Body.Variables) Walk(v.VariableType);
    foreach (var ins in m.Body.Instructions) if (ins.Operand is TypeReference tr) Walk(tr);
  }
}
foreach (var mr in memberRefs) {
  Walk(mr.DeclaringType);
  if (mr is MethodReference m) { Walk(m.ReturnType); foreach (var p in m.Parameters) Walk(p.ParameterType); }
  if (mr is FieldReference f) Walk(f.FieldType);
}

var asmRefVersions = src.AssemblyReferences.ToDictionary(a => a.Name, a => a);
var modules = new Dictionary<string, ModuleDefinition>();
var defs = new Dictionary<string, TypeDefinition>(); // "asm|key"
var structDefs = new HashSet<TypeDefinition>();
foreach (var asm in targets) {
  var v = asmRefVersions.TryGetValue(asm, out var ar) ? ar.Version : new Version(0, 0, 0, 0);
  var ad = AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition(asm, v), asm, new ModuleParameters { Kind = ModuleKind.Dll, Runtime = TargetRuntime.Net_4_0 });
  if (ar?.PublicKeyToken?.Length > 0) { /* cannot sign; token mismatch is acceptable for compile-only stubs */ }
  modules[asm] = ad.MainModule;
}
var corlib = src.TypeSystem.Object.Scope as AssemblyNameReference;

// Create type definitions (outer first)
foreach (var (asm, types) in refsByAsm) {
  var mod = modules[asm];
  foreach (var (k, t) in types.OrderBy(p => p.Key.Count(c => c == '/'))) {
    bool isIface = interfaces.Contains(k);
    bool isValueType = !isIface && (t.IsValueType || valueTypes.Contains(k));
    var attrs = TypeAttributes.Public;
    if (t.DeclaringType != null) attrs = TypeAttributes.NestedPublic;
    TypeDefinition td;
    if (isIface) td = new TypeDefinition(t.DeclaringType == null ? t.Namespace : "", t.Name, attrs | TypeAttributes.Interface | TypeAttributes.Abstract);
    else td = new TypeDefinition(t.DeclaringType == null ? t.Namespace : "", t.Name, attrs | (isValueType ? TypeAttributes.Sealed | TypeAttributes.SequentialLayout : 0) | TypeAttributes.BeforeFieldInit);
    int arity = t.Name.Contains('`') ? int.Parse(t.Name.Substring(t.Name.IndexOf('`') + 1)) : 0;
    // nested types inherit outer generic params in metadata
    int outerArity = 0; for (var d = t.DeclaringType; d != null; d = d.DeclaringType) if (d.Name.Contains('`')) outerArity += int.Parse(d.Name.Substring(d.Name.IndexOf('`') + 1));
    for (int i = 0; i < arity + outerArity; i++) td.GenericParameters.Add(new GenericParameter("T" + i, td));
    defs[asm + "|" + k] = td;
    if (isValueType) { structDefs.Add(td); td.BaseType = new TypeReference("System", "ValueType", mod, mod.TypeSystem.CoreLibrary); }
    if (t.DeclaringType != null) defs[asm + "|" + Key(t.DeclaringType)].NestedTypes.Add(td); else mod.Types.Add(td);
  }
}

TypeReference Import(TypeReference t, ModuleDefinition mod, TypeDefinition ownerType, MethodDefinition ownerMethod) {
  switch (t) {
    case GenericParameter gp:
      if (gp.Type == GenericParameterType.Method && ownerMethod != null) return ownerMethod.GenericParameters[gp.Position];
      if (ownerType != null && gp.Position < ownerType.GenericParameters.Count) return ownerType.GenericParameters[gp.Position];
      return mod.TypeSystem.Object;
    case GenericInstanceType g: { var git = new GenericInstanceType(Import(g.ElementType, mod, ownerType, ownerMethod)); foreach (var a in g.GenericArguments) git.GenericArguments.Add(Import(a, mod, ownerType, ownerMethod)); return git; }
    case ArrayType a: return new ArrayType(Import(a.ElementType, mod, ownerType, ownerMethod), a.Rank);
    case ByReferenceType b: return new ByReferenceType(Import(b.ElementType, mod, ownerType, ownerMethod));
    case PointerType p: return new PointerType(Import(p.ElementType, mod, ownerType, ownerMethod));
    case RequiredModifierType r: return Import(r.ElementType, mod, ownerType, ownerMethod);
    case OptionalModifierType o: return Import(o.ElementType, mod, ownerType, ownerMethod);
    case SentinelType s: return Import(s.ElementType, mod, ownerType, ownerMethod);
    case PinnedType pt: return Import(pt.ElementType, mod, ownerType, ownerMethod);
  }
  if (t.Namespace == "System" && t.DeclaringType == null && (t.Scope?.Name == "mscorlib" || t.Scope?.Name == "netstandard")) {
    var ts = mod.TypeSystem;
    switch (t.Name) {
      case "Void": return ts.Void; case "Boolean": return ts.Boolean; case "Char": return ts.Char; case "SByte": return ts.SByte;
      case "Byte": return ts.Byte; case "Int16": return ts.Int16; case "UInt16": return ts.UInt16; case "Int32": return ts.Int32;
      case "UInt32": return ts.UInt32; case "Int64": return ts.Int64; case "UInt64": return ts.UInt64; case "Single": return ts.Single;
      case "Double": return ts.Double; case "String": return ts.String; case "Object": return ts.Object; case "IntPtr": return ts.IntPtr;
      case "UIntPtr": return ts.UIntPtr; case "TypedReference": return ts.TypedReference;
    }
  }
  var sn = ScopeName(t);
  if (sn != null && targets.Contains(sn) && defs.TryGetValue(sn + "|" + Key(t), out var def)) {
    if (def.Module == mod) return def;
    return mod.ImportReference(def);
  }
  if (t.Scope is ModuleDefinition) { // type defined in the source assembly itself
    var r = new TypeReference(t.Namespace, t.Name, mod, new AssemblyNameReference(src.Assembly.Name.Name, src.Assembly.Name.Version), t.IsValueType);
    return r;
  }
  if (t.DeclaringType != null) {
    var outer = Import(t.DeclaringType, mod, null, null);
    return new TypeReference("", t.Name, mod, outer.Scope, t.IsValueType) { DeclaringType = outer };
  }
  var scope = t.Scope as AssemblyNameReference;
  AssemblyNameReference newScope = scope == null ? corlib : mod.AssemblyReferences.FirstOrDefault(x => x.Name == scope.Name);
  if (newScope == null) { newScope = new AssemblyNameReference(scope.Name, scope.Version) { PublicKeyToken = scope.PublicKeyToken }; mod.AssemblyReferences.Add(newScope); }
  return new TypeReference(t.Namespace, t.Name, mod, newScope, t.IsValueType);
}

void ThrowBody(MethodDefinition md) {
  md.Body = new MethodBody(md); var il = md.Body.GetILProcessor();
  il.Emit(OpCodes.Ldnull); il.Emit(OpCodes.Throw);
}

var seen = new HashSet<string>();
foreach (var mr in memberRefs) {
  var dt = Elem(mr.DeclaringType);
  var sn = ScopeName(dt);
  if (sn == null || !targets.Contains(sn)) continue;
  if (!defs.TryGetValue(sn + "|" + Key(dt), out var td)) continue;
  var mod = td.Module;
  if (mr is FieldReference fr) {
    if (!seen.Add(td.FullName + "::F" + fr.Name)) continue;
    bool isStatic = staticFields.Contains(Key(dt) + "::" + fr.Name) || td.IsInterface;
    var fd = new FieldDefinition(fr.Name, FieldAttributes.Public | (isStatic ? FieldAttributes.Static : 0), Import(fr.FieldType, mod, td, null));
    td.Fields.Add(fd);
  } else if (mr is MethodReference m) {
    var sig = td.FullName + "::" + m.Name + "`" + m.GenericParameters.Count + "(" + string.Join(",", m.Parameters.Select(p => p.ParameterType.FullName)) + ")";
    if (!seen.Add(sig)) continue;
    var attrs = MethodAttributes.Public | MethodAttributes.HideBySig;
    if (!m.HasThis) attrs |= MethodAttributes.Static;
    bool ctor = m.Name == ".ctor" || m.Name == ".cctor";
    if (ctor) attrs |= MethodAttributes.SpecialName | MethodAttributes.RTSpecialName;
    if (m.Name.StartsWith("get_") || m.Name.StartsWith("set_") || m.Name.StartsWith("add_") || m.Name.StartsWith("remove_") || m.Name.StartsWith("op_")) attrs |= MethodAttributes.SpecialName;
    if (td.IsInterface && m.HasThis) attrs |= MethodAttributes.Virtual | MethodAttributes.Abstract | MethodAttributes.NewSlot;
    else if (m.HasThis && !ctor && !structDefs.Contains(td)) attrs |= MethodAttributes.Virtual | MethodAttributes.NewSlot;
    var md = new MethodDefinition(m.Name, attrs, mod.TypeSystem.Void);
    foreach (var gp in m.GenericParameters) md.GenericParameters.Add(new GenericParameter(gp.Name ?? "M", md));
    for (int i = md.GenericParameters.Count; i < m.GenericParameters.Count; i++) md.GenericParameters.Add(new GenericParameter("M" + i, md));
    md.ReturnType = Import(m.ReturnType, mod, td, md);
    int pi = 0;
    foreach (var p in m.Parameters) md.Parameters.Add(new ParameterDefinition(p.Name ?? ("p" + pi), ParameterAttributes.None, Import(p.ParameterType, mod, td, md))); 
    td.Methods.Add(md);
    if (!md.IsAbstract) ThrowBody(md);
  }
}
// Value types need no default ctor; classes get a protected ctor so derivation compiles.
foreach (var td in defs.Values) {
  if (!td.IsInterface && !structDefs.Contains(td) && !td.Methods.Any(x => x.IsConstructor && !x.IsStatic)) {
    var c = new MethodDefinition(".ctor", MethodAttributes.Family | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName, td.Module.TypeSystem.Void);
    td.Methods.Add(c); ThrowBody(c);
  }
  if (!td.IsInterface) {
    td.BaseType = structDefs.Contains(td) ? new TypeReference("System", "ValueType", td.Module, td.Module.TypeSystem.CoreLibrary) : td.Module.TypeSystem.Object;
  }
  // synthesize properties from accessors
  foreach (var g in td.Methods.Where(x => x.Name.StartsWith("get_") || x.Name.StartsWith("set_")).GroupBy(x => x.Name.Substring(4)).ToList()) {
    var getter = g.FirstOrDefault(x => x.Name.StartsWith("get_") && x.Parameters.Count == 0);
    var setter = g.FirstOrDefault(x => x.Name.StartsWith("set_") && x.Parameters.Count == 1);
    if (getter == null && setter == null) continue;
    var ptype = getter?.ReturnType ?? setter.Parameters[0].ParameterType;
    var pd = new PropertyDefinition(g.Key, PropertyAttributes.None, ptype) { GetMethod = getter, SetMethod = setter };
    if (!td.Properties.Any(p => p.Name == pd.Name)) td.Properties.Add(pd);
  }
}
var enums = new Dictionary<string, (string, long)[]> {
  ["Newtonsoft.Json|Newtonsoft.Json.Formatting"] = new[] { ("None", 0L), ("Indented", 1L) },
  // A* Pathfinding Project 4.x; Assembly-CSharp compares CompleteState against 2 (Complete) in AiAction.ScorePath.
  ["AstarPathfindingProject|Pathfinding.PathCompleteState"] = new[] { ("NotCalculated", 0L), ("Error", 1L), ("Complete", 2L), ("Partial", 3L) },
};
foreach (var (k, vals) in enums) {
  if (!defs.TryGetValue(k, out var td)) { var asm = k.Split('|')[0]; var full = k.Split('|')[1]; var ns = full.Substring(0, full.LastIndexOf('.'));
    td = new TypeDefinition(ns, full.Substring(ns.Length + 1), TypeAttributes.Public | TypeAttributes.Sealed); modules[asm].Types.Add(td); defs[k] = td; }
  td.Methods.Clear(); td.Fields.Clear(); td.Properties.Clear();
  td.BaseType = new TypeReference("System", "Enum", td.Module, td.Module.TypeSystem.CoreLibrary); td.IsSequentialLayout = false;
  td.Fields.Add(new FieldDefinition("value__", FieldAttributes.Public | FieldAttributes.SpecialName | FieldAttributes.RTSpecialName, td.Module.TypeSystem.Int32));
  foreach (var (n, v) in vals) td.Fields.Add(new FieldDefinition(n, FieldAttributes.Public | FieldAttributes.Static | FieldAttributes.Literal | FieldAttributes.HasDefault, td) { Constant = (int)v });
}
// Known class hierarchy facts that MemberRefs cannot express (A* Pathfinding Project public API).
var bases = new Dictionary<string, string> { ["AstarPathfindingProject|Pathfinding.ABPath"] = "AstarPathfindingProject|Pathfinding.Path" };
foreach (var (k, b) in bases) if (defs.TryGetValue(k, out var td) && defs.TryGetValue(b, out var bd)) td.BaseType = bd;
Directory.CreateDirectory(outDir);
foreach (var (asm, mod) in modules) { mod.Assembly.Write(Path.Combine(outDir, asm + ".dll")); Console.WriteLine($"{asm}: {mod.Types.Count} top-level types"); }
