using System;
using System.Collections.Generic;
using CompanionAI_Pathfinder.Tactics;
using Kingmaker;
using Kingmaker.EntitySystem.Entities;
using UnityEngine;

namespace CompanionAI_Pathfinder.Analysis
{
    public static partial class AoeDangerAnalyzer
    {
        internal struct PathAreaDanger
        {
            public DangerousEffect Effect;
            public float Rounds;
            public bool EndsInside, MovementOnly, Stops;
        }

        /// <summary>Every newly entered live area on the already queried native route, once per area. Uses the same
        /// native shape, affect and duration readers as movement pricing. No path query or actor cache. Unknown routes,
        /// pending deliveries and exhausted budgets cannot authorize this exceptional advance.</summary>
        internal static bool TryQuotePathAreas(UnitEntityData unit, IList<Vector3> walk, out List<PathAreaDanger> dangers)
        {
            dangers = null;
            if (unit == null || walk == null || walk.Count == 0 || walk.Count > AreaQuoteRules.MaxPathPoints) return false;
            try
            {
                float length = 0f;
                for (int i = 1; i < walk.Count; i++) length += Vector3.Distance(walk[i - 1], walk[i]);
                if (!AreaQuoteRules.PathWithinBudget(walk.Count, length, unit.CurrentSpeedMps)) return false;
                for (int i = 1; i < walk.Count; i++)
                    if (PendingOnWay(unit, walk[i - 1], walk[i]) != null) return false;
                var areas = Game.Instance?.State?.AreaEffects;
                if (areas == null) return false;
                var result = new List<PathAreaDanger>();
                int scanned = 0;
                foreach (var area in areas)
                {
                    if (++scanned > AreaQuoteRules.MaxAreas) return false;
                    if (!IsLive(area) || IsUnitInsideEffect(unit, area)) continue;
                    bool enters = false;
                    for (int i = 1; i < walk.Count && !enters; i++)
                    {
                        int samples = Math.Max(1, Mathf.CeilToInt(Vector3.Distance(walk[i - 1], walk[i]) / 0.25f));
                        for (int j = 1; j <= samples && !enters; j++)
                            enters = Covers(area, Vector3.Lerp(walk[i - 1], walk[i], j / (float)samples), unit.Corpulence);
                    }
                    if (!enters) continue;
                    var effect = EvaluateEffectDanger(unit, area, quiet: true);
                    if (effect == null || !(effect.DangerLevel > 0f)) continue;
                    result.Add(new PathAreaDanger
                    {
                        Effect = effect, Rounds = RoundsLeft(area), EndsInside = Covers(area, walk[walk.Count - 1], unit.Corpulence),
                        MovementOnly = IsMovementOnly(effect, area),
                        Stops = effect.IsMovementImpairing || ControlOf(area) == ControlStrength.Hard
                    });
                }
                dangers = result;
                return true;
            }
            catch { return false; }
        }
    }
}
