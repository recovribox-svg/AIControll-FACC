using System;
using System.Reflection;
using System.Reflection.Emit;
using CompanionAI_Pathfinder.Tactics;
using Xunit;
using Shape = CompanionAI_Pathfinder.Tactics.OptionalApiBinding.MethodShape;

namespace CompanionAI.Tests
{
    // Stand-ins for the game types ModdedDamageReduction passes (OldStyleUnitPart, DamageEnergyType, BaseDamage,
    // ItemEntityWeapon). The synthetic "TabletopTweaks-Core" assemblies below are emitted at test time; no TTT or game DLL.
    public class FakePartBase { }
    public enum FakeEnergy { Fire, Cold }
    public class FakeDamage { }
    public class FakeWeapon { }

    /// <summary>Recommendation A: the optional-provider binding from PR #13 (Tactics/OptionalApiBinding.cs).</summary>
    public class OptionalApiBindingTests
    {
        private const string AssemblyName = "TabletopTweaks-Core";
        private const string PartName = "TabletopTweaks.Core.NewUnitParts.TTUnitPartDamageReduction";

        private static readonly Shape[] Expected =
        {
            new Shape("TryInitialize", typeof(void)),
            new Shape("IsImmune", typeof(bool), typeof(FakeEnergy)),
            new Shape("EstimateDamage", typeof(float), typeof(float), typeof(FakeDamage), typeof(FakeWeapon))
        };

        /// <summary>How the emitted provider differs from the API the mod expects.</summary>
        public sealed class Variant
        {
            public string AssemblyName = OptionalApiBindingTests.AssemblyName;
            public string TypeName = PartName;
            public TypeAttributes Visibility = TypeAttributes.Public;
            public bool Abstract;
            public Type Base = typeof(FakePartBase);
            public Type EstimateResult = typeof(float);
            public Type EstimateAmount = typeof(float);
            public bool EstimateStatic;
            public bool EstimateNonPublic;
            public bool EstimateGeneric;
            public bool OmitImmunity;
        }

        // Emits: class TTUnitPartDamageReduction : FakePartBase { void TryInitialize() { Initialized = true; }
        //        bool IsImmune(FakeEnergy e) => e == Fire; float EstimateDamage(float a, FakeDamage d, FakeWeapon w) => a * 0.5f; }
        private static Assembly Emit(Variant v)
        {
            var asm = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName(v.AssemblyName), AssemblyBuilderAccess.Run);
            var module = asm.DefineDynamicModule(v.AssemblyName);
            var attributes = v.Visibility | TypeAttributes.Class | (v.Abstract ? TypeAttributes.Abstract : 0);
            var type = module.DefineType(v.TypeName, attributes, v.Base);
            var initialized = type.DefineField("Initialized", typeof(bool), FieldAttributes.Public);
            type.DefineDefaultConstructor(MethodAttributes.Public);

            var init = type.DefineMethod("TryInitialize", MethodAttributes.Public, typeof(void), Type.EmptyTypes).GetILGenerator();
            init.Emit(OpCodes.Ldarg_0);
            init.Emit(OpCodes.Ldc_I4_1);
            init.Emit(OpCodes.Stfld, initialized);
            init.Emit(OpCodes.Ret);

            if (!v.OmitImmunity)
            {
                var immune = type.DefineMethod("IsImmune", MethodAttributes.Public, typeof(bool), new[] { typeof(FakeEnergy) }).GetILGenerator();
                immune.Emit(OpCodes.Ldarg_1);
                immune.Emit(OpCodes.Ldc_I4, (int)FakeEnergy.Fire);
                immune.Emit(OpCodes.Ceq);
                immune.Emit(OpCodes.Ret);
            }

            var estimateAttributes = (v.EstimateNonPublic ? MethodAttributes.Assembly : MethodAttributes.Public) |
                                     (v.EstimateStatic ? MethodAttributes.Static : 0);
            var estimate = type.DefineMethod("EstimateDamage", estimateAttributes, v.EstimateResult,
                new[] { v.EstimateAmount, typeof(FakeDamage), typeof(FakeWeapon) });
            if (v.EstimateGeneric) estimate.DefineGenericParameters("T");
            var il = estimate.GetILGenerator();
            if (v.EstimateResult == typeof(float) && v.EstimateAmount == typeof(float))
            {
                il.Emit(v.EstimateStatic ? OpCodes.Ldarg_0 : OpCodes.Ldarg_1);
                il.Emit(OpCodes.Ldc_R4, 0.5f);
                il.Emit(OpCodes.Mul);
            }
            else if (v.EstimateResult == typeof(double)) il.Emit(OpCodes.Ldc_R8, 0.0);
            else if (v.EstimateResult == typeof(float)) il.Emit(OpCodes.Ldc_R4, 0f);
            il.Emit(OpCodes.Ret);

            type.CreateType();
            return asm;
        }

        private static MethodInfo[] Find(out Type type, params Assembly[] loaded) =>
            OptionalApiBinding.Find(loaded, AssemblyName, PartName, typeof(FakePartBase), out type, Expected);

        [Fact]
        public void ProviderAbsent_BindsNothingAndDoesNotThrow()
        {
            Assert.Null(Find(out var type, typeof(OptionalApiBindingTests).Assembly, typeof(object).Assembly));
            Assert.Null(type);
            Assert.Null(Find(out type));
            Assert.Null(OptionalApiBinding.Find(null, AssemblyName, PartName, typeof(FakePartBase), out type, Expected));
        }

        [Fact]
        public void SameTypeInAnotherAssembly_IsNotTheProvider() =>
            Assert.Null(Find(out _, Emit(new Variant { AssemblyName = "SomeOtherMod" })));

        [Fact]
        public void MatchingProvider_BindsTheExactMethodsInOrder()
        {
            var methods = Find(out var type, typeof(object).Assembly, Emit(new Variant()));
            Assert.NotNull(methods);
            Assert.Equal(PartName, type.FullName);
            Assert.Equal(new[] { "TryInitialize", "IsImmune", "EstimateDamage" }, Array.ConvertAll(methods, m => m.Name));
        }

        [Fact]
        public void MatchingProvider_DelegatesCallTheProvidersOwnMethods()
        {
            var methods = Find(out var type, Emit(new Variant()));
            var part = Activator.CreateInstance(type);

            OptionalApiBinding.Action(type, methods[0])(part);
            Assert.True((bool)type.GetField("Initialized").GetValue(part));

            var immune = OptionalApiBinding.Func<FakeEnergy, bool>(type, methods[1]);
            Assert.True(immune(part, FakeEnergy.Fire));
            Assert.False(immune(part, FakeEnergy.Cold));

            var estimate = OptionalApiBinding.Func<float, FakeDamage, FakeWeapon, float>(type, methods[2]);
            Assert.Equal(5f, estimate(part, 10f, new FakeDamage(), null));
        }

        public static TheoryData<string, Variant> Mismatches() => new TheoryData<string, Variant>
        {
            { "type renamed", new Variant { TypeName = "TabletopTweaks.Core.NewUnitParts.TTUnitPartResistance" } },
            { "type not public", new Variant { Visibility = TypeAttributes.NotPublic } },
            { "type abstract", new Variant { Abstract = true } },
            { "not a unit part", new Variant { Base = typeof(object) } },
            { "method missing", new Variant { OmitImmunity = true } },
            { "result type changed", new Variant { EstimateResult = typeof(double) } },
            { "argument type changed", new Variant { EstimateAmount = typeof(double) } },
            { "method static", new Variant { EstimateStatic = true } },
            { "method not public", new Variant { EstimateNonPublic = true } },
            { "method generic", new Variant { EstimateGeneric = true } }
        };

        [Theory]
        [MemberData(nameof(Mismatches))]
        public void MismatchedProvider_IsReportedNotBound(string change, Variant variant)
        {
            var assembly = Emit(variant);
            Type type = null;
            var ex = Record.Exception(() => Find(out type, assembly));
            Assert.True(ex is MissingMemberException, change + ": expected MissingMemberException, got " + (ex?.GetType().Name ?? "none"));
            Assert.Null(type);
        }
    }
}
