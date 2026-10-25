// ★ v0.3.14: does Selective metamagic keep allies out of an ability's effect? Asked of the game, not assumed.
//
// v0.3.7-0.3.13 treated "the ability has Selective" as "allies are safe". The game's rule is narrower and other mods
// change it: an instantaneous burst (AbilityTargetsAround, chains, ricochets) skips allies only when its own target
// filter has no "is ally" condition, and a lasting area (Entangle, Grease, Web) decides once, when it is spawned, in
// AreaEffectEntityData.CheckSelective. Tabletop Tweaks (fix "SelectiveMetamagicNonInstantaneous", on by default)
// postfixes that method to always let lasting areas affect allies, as in the tabletop rules. Orvath cast Entangle with
// Selective active; every companion who walked in had to save and was entangled, while the AI counted them as spared.
//
// For lasting areas this runs the game's own (possibly patched) CheckSelective on a stand-in area entity built from a
// real execution context of the ability, so whatever Tabletop Tweaks or another mod does there is what the AI sees.
// If that cannot be done (game update), allies count as affected: the safe answer.
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using HarmonyLib;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic.Mechanics;
using Kingmaker.Utility;

namespace CompanionAI_Pathfinder.Tactics
{
    public static class SelectiveRule
    {
        private static readonly FieldInfo BlueprintField = AccessTools.Field(typeof(AreaEffectEntityData), "m_Blueprint");
        private static readonly FieldInfo ContextField = AccessTools.Field(typeof(AreaEffectEntityData), "m_Context");
        private static readonly FieldInfo CanAffectAlliesField = AccessTools.Field(typeof(AreaEffectEntityData), "m_CanAffectAllies");
        private static readonly MethodInfo CheckSelectiveMethod = AccessTools.Method(typeof(AreaEffectEntityData), "CheckSelective");

        /// <summary>Per area blueprint: does a Selective cast keep allies out? The answer only depends on the blueprint
        /// and on rule patches loaded at start-up.</summary>
        private static readonly Dictionary<string, bool> SparedByArea = new Dictionary<string, bool>();
        private static readonly HashSet<string> Reported = new HashSet<string>();
        private static bool s_Broken;

        /// <summary>
        /// A lasting area of <paramref name="ability"/> keeps allies out because of Selective (false without Selective,
        /// or when the game, or a mod patching it, lets the area affect allies anyway).
        /// </summary>
        public static bool AreaSparesAllies(AbilityData ability, BlueprintAbilityAreaEffect area)
        {
            if (ability == null || area == null) return false;
            if (!ability.HasMetamagic(Kingmaker.UnitLogic.Abilities.Metamagic.Selective)) return false;
            string key = area.AssetGuid.ToString();
            if (SparedByArea.TryGetValue(key, out bool spared)) return spared;
            spared = AskGame(ability, area);
            SparedByArea[key] = spared;
            if (!spared && Reported.Add(key))
                Main.Log($"[Selective] {ability.Name}: its lasting area ({area.name}) still affects allies with Selective " +
                         "(game rule or a mod such as Tabletop Tweaks); allies inside or walking in count as caught");
            return spared;
        }

        private static bool AskGame(AbilityData ability, BlueprintAbilityAreaEffect area)
        {
            if (s_Broken) return false;
            if (BlueprintField == null || ContextField == null || CanAffectAlliesField == null || CheckSelectiveMethod == null)
            {
                s_Broken = true;
                Main.Warning("[Selective] AreaEffectEntityData.CheckSelective not found (game update?): lasting areas count " +
                             "as affecting allies");
                return false;
            }
            try
            {
                var caster = ability.Caster?.Unit;
                if (caster == null) return false;
                // The same context a cast creates (its Params carry the metamagic, automatic metamagic included); the
                // stand-in entity is never registered anywhere, CheckSelective only reads these two fields.
                var context = new AbilityExecutionContext(ability, CombatMath.AbilityParameters(ability), new TargetWrapper(caster.Position));
                var stand = (AreaEffectEntityData)FormatterServices.GetUninitializedObject(typeof(AreaEffectEntityData));
                BlueprintField.SetValue(stand, area);
                ContextField.SetValue(stand, (MechanicsContext)context);
                CanAffectAlliesField.SetValue(stand, true);
                CheckSelectiveMethod.Invoke(stand, null);
                return !(bool)CanAffectAlliesField.GetValue(stand);
            }
            catch (Exception ex)
            {
                Main.Warning($"[Selective] {ability.Name} / {area.name}: cannot ask the game whether Selective spares allies " +
                             $"({(ex is TargetInvocationException tie ? tie.InnerException?.Message : ex.Message)}); " +
                             "the area counts as affecting allies");
                return false;
            }
        }

        /// <summary>A live area effect can affect its caster's allies (the value CheckSelective set when it spawned).</summary>
        public static bool LiveAreaAffectsAllies(AreaEffectEntityData area)
        {
            if (area == null) return false;
            try
            {
                return CanAffectAlliesField == null || (bool)CanAffectAlliesField.GetValue(area);
            }
            catch
            {
                return true;
            }
        }
    }
}
