// ★ v0.3.52 coup1: scalar bounds for an AI-issued Coup de Grace.
using System;

namespace CompanionAI_Pathfinder.Tactics
{
    /// <summary>Only narrows the native ability radius; the weapon radius already includes both bodies.</summary>
    internal static class CoupDeGraceRules
    {
        internal static bool TryRadius(float abilityRadius, float weaponRadius, out float radius)
        {
            radius = 0f;
            if (!PositiveFinite(abilityRadius) || !PositiveFinite(weaponRadius)) return false;
            radius = Math.Min(abilityRadius, weaponRadius);
            return true;
        }

        private static bool PositiveFinite(float value) => value > 0f && !float.IsInfinity(value) && !float.IsNaN(value);

        internal static bool IsTabletopTweaks(string type, string assembly) =>
            type == "TabletopTweaks.Core.NewComponents.OwlcatReplacements.CoupDeGraceTTT" && assembly == "TabletopTweaks-Core";
    }
}
