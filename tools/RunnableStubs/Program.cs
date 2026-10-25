// stubfix <in.dll> <out.dll> [Type::Method ...]
// Without a list: every method of a throw-only reference stub gets a body that does nothing and returns the default.
// With a list: only those methods (string results return ""). For the offline ProfileAudit harness only.
using Mono.Cecil;
using Mono.Cecil.Cil;
var asm = AssemblyDefinition.ReadAssembly(args[0], new ReaderParameters { ReadWrite = false, AssemblyResolver = Resolver(args[0]) });
var only = args.Skip(2).ToHashSet();
int n = 0;
foreach (var type in asm.MainModule.GetTypes())
    foreach (var m in type.Methods)
    {
        var name = $"{type.FullName}::{m.Name}";
        if (only.Count > 0 && !only.Contains(name)) continue;
        // ★ v0.3.22: Unity extern getters (Time.frameCount) have no IL body. Only an explicitly named
        // internal call may acquire one; default reference-stub rewriting still leaves externs alone.
        if (!m.HasBody)
        {
            if (only.Count == 0 || !m.IsInternalCall) continue;
            m.Body = new MethodBody(m);
        }
        // a reference stub can carry an internal call with a placeholder body, which the runtime rejects
        m.ImplAttributes &= ~MethodImplAttributes.InternalCall;
        var body = m.Body;
        body.Instructions.Clear(); body.ExceptionHandlers.Clear(); body.Variables.Clear();
        var il = body.GetILProcessor();
        if (m.IsConstructor && !m.IsStatic)
        {
            var baseType = type.BaseType;
            if (baseType != null && !type.IsValueType)
            {
                var baseCtor = new MethodReference(".ctor", asm.MainModule.TypeSystem.Void, baseType) { HasThis = true };
                il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Call, asm.MainModule.ImportReference(baseCtor));
            }
        }
        else if (m.ReturnType.MetadataType == MetadataType.String) il.Emit(OpCodes.Ldstr, "");
        else if (m.ReturnType.MetadataType != MetadataType.Void)
        {
            if (m.ReturnType.IsValueType || m.ReturnType.IsGenericParameter)
            {
                var v = new VariableDefinition(m.ReturnType); body.Variables.Add(v); body.InitLocals = true;
                il.Emit(OpCodes.Ldloca, v); il.Emit(OpCodes.Initobj, m.ReturnType); il.Emit(OpCodes.Ldloc, v);
            }
            else il.Emit(OpCodes.Ldnull);
        }
        il.Emit(OpCodes.Ret); n++;
        if (only.Count > 0) Console.WriteLine($"  {name}");
    }
asm.Write(args[1]);
Console.WriteLine($"{n} bodies replaced");

static IAssemblyResolver Resolver(string path)
{
    var r = new DefaultAssemblyResolver();
    r.AddSearchDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
    return r;
}
