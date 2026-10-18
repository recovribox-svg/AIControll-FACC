// ★ v0.3.13: let animal companions use the items in their belt (quick) slots in combat.
//
// The game forbids it in one place: AbilityData.CanBeCastByCaster returns false when the caster's UnitPartPet.Type is
// AnimalCompanion, the caster is in combat and the ability comes from an item. Animal companions can be sapient, drakes
// even cast spells, and other mods add humanoid companions that are pets by type, so the rule is optional
// (ModSettings.PetsUseItems). The patch replaces only that one Type read with PetTypeForItemRule; every other check
// (charges, UMD, conditions, CanTarget, the AI's ConsumablePolicy) is untouched. If a game update changes the method so
// that the read is not found, nothing is patched and the log says so.
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Kingmaker.Enums;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Parts;
using CompanionAI_Pathfinder.Settings;

namespace CompanionAI_Pathfinder.GameInterface
{
    [HarmonyPatch(typeof(AbilityData), "CanBeCastByCaster", MethodType.Getter)]
    public static class PetItemsPatch
    {
        private static readonly MethodInfo TypeGetter = AccessTools.PropertyGetter(typeof(UnitPartPet), nameof(UnitPartPet.Type));
        private static readonly MethodInfo Replacement = AccessTools.Method(typeof(PetItemsPatch), nameof(PetTypeForItemRule));

        /// <summary>The pet's type as the item rule sees it: not an animal companion when pets may use items.</summary>
        public static PetType PetTypeForItemRule(UnitPartPet pet)
        {
            PetType type = pet.Type;
            if (type == PetType.AnimalCompanion && (ModSettings.Instance?.PetsUseItems ?? true))
                return (PetType)(-1);
            return type;
        }

        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            int replaced = 0;
            foreach (var instruction in instructions)
            {
                if ((instruction.opcode == OpCodes.Callvirt || instruction.opcode == OpCodes.Call) &&
                    instruction.operand is MethodInfo method && method == TypeGetter)
                {
                    replaced++;
                    yield return new CodeInstruction(OpCodes.Call, Replacement) { labels = instruction.labels, blocks = instruction.blocks };
                    continue;
                }
                yield return instruction;
            }
            if (replaced == 1) Main.Log("[PetItems] animal companions may use their belt items in combat (setting)");
            else Main.Warning($"[PetItems] expected one pet type check in AbilityData.CanBeCastByCaster, found {replaced}: " +
                              (replaced == 0 ? "not patched (game changed?)" : "all replaced"));
        }
    }
}
