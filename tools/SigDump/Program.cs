using Mono.Cecil;
// Prints every TypeRef (with valuetype flag) and MemberRef (full signature) whose scope is not the given assembly itself.
var m = ModuleDefinition.ReadModule(args[0]);
foreach (var t in m.GetTypes()) foreach (var me in t.Methods) if (me.HasBody) foreach (var i in me.Body.Instructions) { _ = i.Operand; }
static string Scope(TypeReference t){ while (t.DeclaringType!=null) t=t.DeclaringType; return t.Scope?.Name; }
static string Sig(TypeReference t) => (t is TypeSpecification ? "" : (t.IsValueType ? "valuetype " : "class ")) + t.FullName;
var lines = new SortedSet<string>();
foreach (var mr in m.GetMemberReferences()) {
  var dt = mr.DeclaringType is GenericInstanceType g ? g.ElementType : mr.DeclaringType;
  var sc = Scope(dt); if (sc==null || sc=="mscorlib" || sc.StartsWith("System")) continue;
  if (mr is MethodReference mm) lines.Add($"{sc} M {Sig(mm.ReturnType)} {mr.DeclaringType.FullName}::{mm.Name}({string.Join(",", mm.Parameters.Select(p=>Sig(p.ParameterType)))}) this={mm.HasThis}");
  else if (mr is FieldReference f) lines.Add($"{sc} F {Sig(f.FieldType)} {mr.DeclaringType.FullName}::{f.Name}");
}
foreach (var l in lines) Console.WriteLine(l);
