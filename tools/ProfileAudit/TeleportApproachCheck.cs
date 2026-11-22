using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CompanionAI_Pathfinder.Tactics;
using Kingmaker.Blueprints;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic.Abilities.Components;
using Kingmaker.UnitLogic.Commands;
using Kingmaker.View;

namespace ProfileAudit
{
    internal static class TeleportApproachCheck
    {
        internal static List<string> Run(IEnumerable<SimpleBlueprint> blueprints, out int failures)
        {
            var lines = new List<string>();
            int failed = 0;
            void Check(bool valid, string detail)
            {
                if (!valid) failed++;
                lines.Add("TELEPORT-APPROACH " + (valid ? "passed: " : "FAILED: ") + detail);
            }

            var abilities = blueprints.OfType<BlueprintAbility>().ToLookup(a => a.name, StringComparer.Ordinal);
            BlueprintAbility Blueprint(string name) => abilities[name].FirstOrDefault();
            object Delivery(BlueprintAbility bp) => bp == null ? null :
                SummonEffectsCheck.Property(AbilityProfile.Get(bp), "TeleportDelivery");
            void Door(string name, bool point, bool enemy, float radius, Kingmaker.UnitLogic.Commands.Base.UnitCommand.CommandType slot)
            {
                var bp = Blueprint(name);
                Check(Delivery(bp) is AbilityCustomDimensionDoor door && bp.CanTargetPoint == point &&
                    bp.CanTargetEnemies == enemy && Math.Abs(door.Radius.Value - radius) < 0.01f && bp.ActionType == slot, name);
            }
            var standard = Kingmaker.UnitLogic.Commands.Base.UnitCommand.CommandType.Standard;
            var swift = Kingmaker.UnitLogic.Commands.Base.UnitCommand.CommandType.Swift;
            Door("Vrock_Ability_GreaterTeleport", false, true, 5f, standard);
            Door("Omox_Ability_LiquidLeap", false, true, 10f, swift);
            Door("DimensionDoorHell", true, true, 10f, standard);
            Door("DimensionDoor", true, false, 10f, standard);

            var roaming = Blueprint("DLC3_Nahyndri_Golemlike_Teleport");
            Check(roaming?.GetComponent<AbilityCustomTeleportation>()?.AlongPath == true && Delivery(roaming) == null,
                "stock AlongPath teleport is withheld");

            foreach (string name in new[] { "DemonChargeMainAbility", "UmbralDragonDeathFromAboveAbility",
                                            "DimensionDoorMassDLC_cutscene" })
                Check(Blueprint(name) is BlueprintAbility bp && Delivery(bp) == null, name + " stays on its existing route");

            const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
            IReadOnlyList<MemberInfo> Members(Type owner, string method) =>
                SaveBoostCheck.ReferencedMembers(owner.GetMethod(method, all)).ToArray();
            bool Calls(Type owner, string method, string name) => Members(owner, method).Any(member => member.Name == name);
            Check(Calls(typeof(TacticalEngine), "AddTeleportApproach", "CanTarget") &&
                  Calls(typeof(TacticalEngine), "AddTeleportApproach", "GetApproachDistance") &&
                  Calls(typeof(UnitUseAbility), "Init", "GetApproachDistance"), "native aim and command range are checked separately");
            Check(Members(typeof(TacticalEngine), "QuoteTeleportLanding").Count(member => member.Name == "RelaxPoints") == 1 &&
                  !Calls(typeof(TacticalEngine), "ValidateTeleportIssue", "RelaxPoints") &&
                  Calls(typeof(TacticalEngine), "Execute", "ValidateTeleportIssue") &&
                  Calls(typeof(TacticalEngine), "Execute", "get_ShouldUnitApproach") &&
                  Calls(typeof(TacticalEngine), "CanOfferTeleport", "get_SourceItem"),
                  "one native relaxation route and a separate pre-issue check through the shared dispatcher");

            failures = failed;
            lines.Add($"TELEPORT-APPROACH {lines.Count} checks, {failed} failures");
            return lines;
        }
    }
}
