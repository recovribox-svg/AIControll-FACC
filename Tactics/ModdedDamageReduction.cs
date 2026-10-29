// v0.3.22 follow-up: optional read-only preview of an already installed TTT resistance part.
using System;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Kingmaker.EntitySystem;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.Enums.Damage;
using Kingmaker.Items;
using Kingmaker.RuleSystem;
using Kingmaker.RuleSystem.Rules.Damage;
using Kingmaker.UnitLogic;

namespace CompanionAI_Pathfinder.Tactics
{
    internal static class ModdedDamageReduction
    {
        private const string AssemblyName = "TabletopTweaks-Core";
        private const string PartName = "TabletopTweaks.Core.NewUnitParts.TTUnitPartDamageReduction";

        // Bind lazily during appraisal, after normal mod loading. Only API metadata/delegates survive this call;
        // no actor, installed part, context, damage object or preview result is cached across appraisals.
        private static readonly Lazy<Binding> Api = new Lazy<Binding>(Bind);

        private sealed class Binding
        {
            internal Func<UnitEntityData, object> Get;
            internal Action<object> Initialize;
            internal Func<object, DamageEnergyType, bool> IsImmune;
            internal Func<object, float, BaseDamage, ItemEntityWeapon, float> Estimate;
        }

        /// <summary>
        /// Used only when the ordinary vanilla reduction part is absent. Ask the actual optional provider's
        /// preview; never create a part, execute damage, spend a pool, reset chunks or copy its stacking math.
        /// False means unavailable/unknown, so the caller retains its existing observed/raw fallback.
        /// </summary>
        internal static bool TryEstimate(UnitEntityData target, DamageTypeDescription description,
            float amount, out float remaining, out bool immune)
        {
            remaining = amount;
            immune = false;
            if (target == null || description == null || !Finite(amount) || amount < 0f) return false;
            try
            {
                var api = Api.Value;
                if (api == null) return false;
                var part = api.Get(target); // Closed native Get<T>, not Ensure/Add/new; it cannot install anything.
                if (part == null) return false;
                // TTT exposes its own lazy initialization; AllSources is a display API and is not used here.
                api.Initialize(part);
                immune = description.Type == DamageType.Energy && api.IsImmune(part, description.Energy);
                if (immune) remaining = 0f;
                else
                {
                    // Allocate the same inert typed preview packet as the vanilla path only after finding a real part.
                    // Unprotected/non-TTT targets do not pay for a packet that no provider would inspect.
                    var damage = description.CreateDamage(new DiceFormula(0, DiceType.Zero), 0);
                    if (damage == null) return false;
                    remaining = api.Estimate(part, amount, damage, null);
                }
                if (!Finite(remaining) || remaining < 0f)
                    throw new InvalidOperationException("provider returned an invalid remaining-damage preview");
                return true;
            }
            catch (Exception ex)
            {
                remaining = amount;
                immune = false;
                Report("read|" + ex.GetType().Name,
                    $"[Math] TTT damage-reduction preview unavailable ({ex.GetType().Name}: {ex.Message}); retaining existing fallback");
                return false;
            }
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        private static Binding Bind()
        {
            try
            {
                // Recommendation A: the exact-shape lookup is OptionalApiBinding (unit-tested on synthetic assemblies).
                var methods = OptionalApiBinding.Find(AppDomain.CurrentDomain.GetAssemblies(), AssemblyName, PartName,
                    typeof(OldStyleUnitPart), out var type,
                    new OptionalApiBinding.MethodShape("TryInitialize", typeof(void)),
                    new OptionalApiBinding.MethodShape("IsImmune", typeof(bool), typeof(DamageEnergyType)),
                    new OptionalApiBinding.MethodShape("EstimateDamage", typeof(float), typeof(float), typeof(BaseDamage), typeof(ItemEntityWeapon)));
                if (methods == null) return null; // Optional mod absent: no load, requirement or warning.
                var get = typeof(EntityDataBase).GetMethods(BindingFlags.Instance | BindingFlags.Public)
                    .Single(m => m.Name == "Get" && m.IsGenericMethodDefinition &&
                        m.GetGenericArguments().Length == 1 && m.GetParameters().Length == 0)
                    .MakeGenericMethod(type);
                if (get.ReturnType != type) throw new MissingMemberException("native Get<T> return type");

                var unit = Expression.Parameter(typeof(UnitEntityData), "unit");
                return new Binding
                {
                    Get = Expression.Lambda<Func<UnitEntityData, object>>(Expression.Convert(Expression.Call(unit, get), typeof(object)), unit).Compile(),
                    Initialize = OptionalApiBinding.Action(type, methods[0]),
                    IsImmune = OptionalApiBinding.Func<DamageEnergyType, bool>(type, methods[1]),
                    Estimate = OptionalApiBinding.Func<float, BaseDamage, ItemEntityWeapon, float>(type, methods[2])
                };
            }
            catch (Exception ex)
            {
                Report("binding", $"[Math] TTT damage-reduction preview API unavailable ({ex.GetType().Name}: {ex.Message}); retaining existing fallback");
                return null;
            }
        }

        private static void Report(string key, string text)
        {
            try { AbilityDiscovery.ReportOnce("ttt-reduction|" + key, text); }
            catch { } // A missing logger must not turn an optional compatibility read into a scoring failure.
        }
    }
}
