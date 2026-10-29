// Recommendation A: the mod's own TabletopTweaks-Core binding (Tactics/ModdedDamageReduction) against a real TTT DLL, offline.
// The unit tests cover absent and mismatched providers on emitted assemblies; this checks the real API shape.
using System;
using System.Collections.Generic;
using System.Reflection;
using CompanionAI_Pathfinder.Tactics;
using Kingmaker.Enums.Damage;
using Kingmaker.RuleSystem;
using Kingmaker.RuleSystem.Rules.Damage;

namespace ProfileAudit
{
    public static class OptionalProviderCheck
    {
        private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        /// <summary>Report lines; throws when the provider does not bind or a bound call misbehaves.</summary>
        public static List<string> Run(string providerDll)
        {
            var assembly = Assembly.LoadFrom(providerDll);
            var name = assembly.GetName();
            if (name.Name != "TabletopTweaks-Core")
                throw new InvalidOperationException("not TabletopTweaks-Core: " + name.Name);
            // TTT keeps its assembly version at 1.0.0.0; its UMM Info.json next to the DLL carries the mod version.
            var info = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(providerDll)), "Info.json");
            var version = System.IO.File.Exists(info)
                ? System.Text.RegularExpressions.Regex.Match(System.IO.File.ReadAllText(info), "\"Version\"\\s*:\\s*\"([^\"]+)\"").Groups[1].Value
                : "unknown";
            var lines = new List<string> { $"optional provider: {name.Name} {version} (assembly {name.Version})" };

            // Bind exactly as the game would: the mod's lazy binding, after the provider assembly is loaded.
            var owner = typeof(TacticalEngine).Assembly.GetType("CompanionAI_Pathfinder.Tactics.ModdedDamageReduction", true);
            var lazy = owner.GetField("Api", Any)?.GetValue(null) ?? throw new MissingFieldException(owner.FullName, "Api");
            var binding = lazy.GetType().GetProperty("Value").GetValue(lazy)
                ?? throw new InvalidOperationException("ModdedDamageReduction did not bind (the game log names the reason)");
            object Delegate(string field) => binding.GetType().GetField(field, Any)?.GetValue(binding)
                ?? throw new InvalidOperationException("binding has no " + field + " delegate");
            var initialize = (Action<object>)Delegate("Initialize");
            Delegate("Get");
            Delegate("IsImmune");
            var estimate = (Func<object, float, BaseDamage, Kingmaker.Items.ItemEntityWeapon, float>)Delegate("Estimate");
            lines.Add("optional provider: bound Get<TTUnitPartDamageReduction>, TryInitialize, IsImmune, EstimateDamage");

            // A fresh part has no sources: TTT's own estimate must return the amount unchanged. IsImmune reads the owner
            // unit's state and Get needs a unit, so both stay for the game session.
            var partType = assembly.GetType("TabletopTweaks.Core.NewUnitParts.TTUnitPartDamageReduction", true);
            var part = Activator.CreateInstance(partType);
            initialize(part);
            var damage = new DamageTypeDescription { Type = DamageType.Energy, Energy = DamageEnergyType.Fire }
                .CreateDamage(new DiceFormula(0, DiceType.Zero), 0);
            float remaining = estimate(part, 10f, damage, null);
            if (remaining != 10f) throw new InvalidOperationException($"empty part estimated 10 -> {remaining}");
            lines.Add("optional provider: empty part TryInitialize + EstimateDamage(10 fire) = 10 (TTT's own code)");
            return lines;
        }
    }
}
