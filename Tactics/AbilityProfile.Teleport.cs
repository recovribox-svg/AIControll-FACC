using Kingmaker.Blueprints;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic.Abilities.Components;

namespace CompanionAI_Pathfinder.Tactics
{
    public sealed partial class AbilityProfile
    {
        internal BlueprintComponent TeleportDelivery { get; private set; }

        private void ReadTeleportDelivery()
        {
            // An approach option quotes transport only. Mixed payloads, custom traveller selection, and
            // roaming teleports require their own forecasts.
            if (Fusion != null || HasDetectableEffect || m_ActionTypes.Count != 0 || IsCharge ||
                IsAoE || Blueprint.Range == AbilityRange.Personal || Blueprint.EffectOnEnemy == AbilityEffectOnUnit.Harmful ||
                Blueprint.Type == AbilityType.Physical || Blueprint.Type == AbilityType.Special ||
                Blueprint.GetComponent<AbilityExecuteActionOnCast>() != null) return;
            var door = Blueprint.GetComponent<AbilityCustomDimensionDoor>();
            if (door?.GetType() == typeof(AbilityCustomDimensionDoor))
                TeleportDelivery = door;
            else if (Blueprint.GetComponent<AbilityCustomTeleportation>() is AbilityCustomTeleportation solo &&
                     solo.GetType() == typeof(AbilityCustomTeleportation) && !solo.AlongPath)
                TeleportDelivery = solo;
        }
    }
}
