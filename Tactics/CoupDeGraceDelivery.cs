// ★ v0.3.52 coup1: match the approach radius and estimate to the native/TTT delivering hand.
using CompanionAI_Pathfinder.Analysis;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.Items;
using Kingmaker.UnitLogic;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic.Abilities.Components;
using Kingmaker.UnitLogic.Abilities.Components.Base;
using Kingmaker.UnitLogic.Commands;
using Kingmaker.UnitLogic.Commands.Base;
using Kingmaker.UnitLogic.Mechanics.Actions;
using Kingmaker.Utility;

namespace CompanionAI_Pathfinder.Tactics
{
    /// <summary>
    /// AI-only Coup contract. AbilityData's Weapon range uses GetFirstWeapon, while native delivery uses
    /// GetThreatHandMelee and TTT's replacement uses GetThreatHand. Never borrow another hand's reach or damage.
    /// No patch, persistent actor cache, path query or attack forecast.
    /// </summary>
    internal static class CoupDeGraceDelivery
    {
        internal enum Route { Unsupported, Native, TabletopTweaks }

        internal static Route ReadRoute(BlueprintAbility blueprint)
        {
            if (blueprint?.ComponentsArray == null || blueprint.Range != AbilityRange.Weapon) return Route.Unsupported;
            int apply = 0, deliver = 0;
            bool native = false, ttt = false;
            foreach (var component in blueprint.ComponentsArray)
            {
                if (component is AbilityApplyEffect)
                {
                    apply++;
                    if (component.GetType() == typeof(AbilityEffectRunAction) &&
                        component is AbilityEffectRunAction run && run.Actions?.Actions?.Length > 0 &&
                        run.Actions.Actions[0]?.GetType() == typeof(ContextActionMeleeAttack) &&
                        run.Actions.Actions[0] is ContextActionMeleeAttack attack)
                        native = !attack.SelectNewTarget && !attack.FullAttack && attack.ExtraAttack &&
                                 attack.AutoHit && attack.AutoCritThreat && attack.AutoCritConfirmation && !attack.IgnoreStatBonus;
                }
                if (component is AbilityDeliverEffect)
                {
                    deliver++;
                    var type = component.GetType();
                    ttt = component is AbilityCustomLogic && CoupDeGraceRules.IsTabletopTweaks(type.FullName, type.Assembly.GetName().Name);
                }
            }
            if (apply == 1 && deliver == 0 && native) return Route.Native;
            if (apply == 0 && deliver == 1 && ttt) return Route.TabletopTweaks;
            return Route.Unsupported;
        }

        internal static bool TryWeapon(UnitEntityData unit, AbilityData ability, out ItemEntityWeapon weapon, out string failure)
        {
            weapon = null;
            failure = null;
            var route = ReadRoute(ability?.Blueprint);
            if (unit == null || !TacticalEngine.IsCoupDeGrace(ability) || route == Route.Unsupported)
            { failure = "Coup de Grace delivery is not a verified native/TTT weapon route"; return false; }
            weapon = (route == Route.Native ? unit.GetThreatHandMelee() : unit.GetThreatHand())?.MaybeWeapon;
            // In TTT, Snap Shot can make GetThreatHand choose a ranged weapon. Do not price a different melee
            // hand and then let the replacement deliver the bow. Manual and other mods' commands are unaffected.
            if (weapon?.Blueprint?.IsMelee != true)
            { weapon = null; failure = "Coup de Grace delivery has no selected melee weapon"; return false; }
            return true;
        }

        internal static bool TryRadius(UnitEntityData unit, AbilityData ability, UnitEntityData target,
                                       ItemEntityWeapon weapon, out float radius)
        {
            radius = 0f;
            return unit != null && target != null && weapon?.Blueprint?.IsMelee == true &&
                   CoupDeGraceRules.TryRadius(ability.GetApproachDistance(target),
                       UnitAttack.GetApproachRadius(weapon, unit, target), out radius);
        }

        /// <summary>After Init and before path preparation, in both preview and actual dispatch.</summary>
        internal static bool TryPrepare(UnitEntityData unit, UnitCommand command, out string failure)
        {
            failure = null;
            if (!(command is UnitUseAbility use) || !TacticalEngine.IsCoupDeGrace(use.Ability)) return true;
            if (!TryWeapon(unit, use.Ability, out var weapon, out failure)) return false;
            // Init only logs an invalid target. Recheck the game's current condition/item/side restrictions.
            if (use.Target?.Unit == null || !use.Ability.CanTarget(use.Target))
            { failure = "Coup de Grace target no longer passes native targeting"; return false; }
            if (!TryRadius(unit, use.Ability, use.Target.Unit, weapon, out float radius) ||
                !CoupDeGraceRules.TryRadius(command.ApproachRadius, radius, out radius))
            { failure = "Coup de Grace has no finite native melee approach radius"; return false; }
            command.ApproachRadius = radius;
            return true;
        }

        internal static void NoteIssued(UnitEntityData unit, UnitCommand command)
        {
            if (!(command is UnitUseAbility use) || !TacticalEngine.IsCoupDeGrace(use.Ability)) return;
            try
            {
                if (!TryWeapon(unit, use.Ability, out var weapon, out _)) return;
                AbilityDiscovery.ReportOnce($"coup-reach|{CombatMemory.Fight}|{unit.UniqueId}|{weapon.Blueprint.AssetGuid}", () =>
                    $"[CoupReach] {unit.CharacterName}: issued using {weapon.Blueprint.name} ({ReadRoute(use.Ability.Blueprint)}); " +
                    $"native ability radius {use.Ability.GetApproachDistance(use.Target.Unit):F2}m, " +
                    $"capped command radius {command.ApproachRadius:F2}m; native approach/LoS retained (first per fight/weapon)");
            }
            catch { } // A diagnostic cannot turn an accepted command into a refusal.
        }
    }
}
