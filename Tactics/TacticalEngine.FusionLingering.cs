// ★ Fusion coverage C10 (docs/FUSION_COVERAGE_REST.md): the lingering area a fused cast leaves at its main target.
// Native contract (AbilityExecutionProcess, supplied assembly): after the delivery, a fused cast runs each source spell's
// AdditionalAbilityEffectRunActionOnClickedTarget once, at the main target (MechanicsContext.GetDataScope(MainTarget)) - Obsidian
// Flow's obsidian and Haunting Mists' mists, each a ContextActionSpawnAreaEffect. Its AdditionalAoeBlueprint (the source with
// AdditionalAoeForMagicHack) only shapes targeting (the shown radius, CanTargetFriends/CanTargetEnemies); the recipients of the
// two payloads come from the deliver spell. The area works on anyone inside it, every round, allies too.
// Valuation: the area is not credited (conservative: the fused cast is worth what its payloads do to their recipients). It
// refuses the cast when it would cover an ally, when a melee ally stands at its edge or fights an enemy inside it (they walk in,
// as the ordinary lingering-area rule says), and in real time when an ally's walk ends inside it.
using System;
using System.Collections.Generic;
using System.Linq;
using Kingmaker.EntitySystem.Entities;
using UnityEngine;

namespace CompanionAI_Pathfinder.Tactics
{
    public static partial class TacticalEngine
    {
        /// <summary>How far past the area's edge a melee ally still walks in to fight (the ordinary lingering-area rule's 2.5 m).</summary>
        private const float LingeringEdge = 2.5f;

        /// <summary>The ally the fused cast's lingering area would catch, or null (no area, or no ally near it).</summary>
        private static UnitEntityData LingeringAreaCatches(TacticalContext ctx, AbilityProfile profile, Vector3 center, out string why)
        {
            why = null;
            if (profile?.Fusion == null || profile.FusionLingeringAreas.Count == 0) return null;
            float radius = 0f;
            foreach (var area in profile.FusionLingeringAreas)
                if (area != null) radius = Math.Max(radius, area.Size.Meters);
            if (!(radius > 0f)) return null;
            foreach (var ally in ctx.Allies.Concat(ctx.DownedAllies).Concat(ctx.TurnedAllies).Distinct())
            {
                if (ally == null) continue;
                float d = HorizontalDistance(ally.Position, center);
                if (d <= radius + ally.Corpulence) { why = $"inside its {radius:F1} m"; return ally; }
                if (ally == ctx.Unit || UsesRangedWeapon(ally) || ally.Descriptor?.State?.IsConscious != true) continue;
                if (d <= radius + LingeringEdge + ally.Corpulence) { why = "fights at its edge"; return ally; }
                var nearest = ctx.Enemies.OrderBy(e => Vector3.Distance(ally.Position, e.Position)).FirstOrDefault();
                if (nearest != null && HorizontalDistance(nearest.Position, center) <= radius) { why = $"its enemy {nearest.CharacterName} is inside"; return ally; }
            }
            var walking = AlliesWalkingInto(ctx, new List<UnitEntityData>(), (ally, at) => HorizontalDistance(at, center) <= radius + ally.Corpulence);
            if (walking != null && walking.Count > 0) { why = "walks into it"; return walking[0]; }
            return null;
        }
    }
}
