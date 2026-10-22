// ★ v0.2.49: AoE 위험 지역 분석기
// ★ v0.2.59: NavMesh 검증 추가
// 유닛이 적대적 지역 효과(Grease, Entangle, 화염 지역 등) 안에 있는지 감지
// 이탈 필요 여부 및 안전한 방향 계산
using System;
using TurnBased.Controllers;
using System.Collections.Generic;
using System.Linq;
using Kingmaker;
using Kingmaker.Blueprints.Classes.Spells;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.View;
using UnityEngine;

namespace CompanionAI_Pathfinder.Analysis
{
    /// <summary>
    /// ★ v0.2.49: AoE 위험 지역 분석
    ///
    /// 감지 대상:
    /// - 적이 시전한 지역 효과 (화염, 산성, 독 등)
    /// - 이동 방해 효과 (Grease, Entangle, Web 등)
    /// - 지속 피해 지역 (Cloudkill, Blade Barrier 등)
    /// </summary>
    public static class AoeDangerAnalyzer
    {
        #region Constants

        // 위험 수준 임계값
        private const float DANGER_THRESHOLD_ESCAPE = 0.5f;  // 이 이상이면 탈출 시도
        private const float DANGER_THRESHOLD_AVOID = 0.3f;   // 이 이상이면 진입 회피


        #endregion

        #region Data Classes

        /// <summary>
        /// 유닛의 AoE 위험 분석 결과
        /// </summary>
        public class DangerAnalysis
        {
            public UnitEntityData Unit { get; set; }
            public bool IsInDanger { get; set; }
            public float TotalDangerLevel { get; set; }  // 0.0 ~ 1.0+
            public List<DangerousEffect> DangerousEffects { get; } = new();
            public Vector3? SuggestedEscapePosition { get; set; }
            public bool ShouldEscape => TotalDangerLevel >= DANGER_THRESHOLD_ESCAPE;
        }

        /// <summary>
        /// 개별 위험 효과 정보
        /// </summary>
        public class DangerousEffect
        {
            public AreaEffectEntityData AreaEffect { get; set; }
            public string Name { get; set; }
            public float DangerLevel { get; set; }
            public SpellDescriptor Descriptors { get; set; }
            public bool IsDamaging { get; set; }
            public bool IsMovementImpairing { get; set; }
            /// <summary>★ v0.3.17: chance the area affects the unit at all before it ends (its save, immunities, time).</summary>
            public float AffectChance { get; set; } = 1f;
            /// <summary>★ v0.3.18 (test11): rounds its effect lasts after a failed save, outside the area too (Stinking
            /// Cloud's nausea); 0 when it ends on leaving or is unknown.</summary>
            public float LingerRounds { get; set; }
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// 유닛의 현재 AoE 위험 상황 분석
        /// </summary>
        public static DangerAnalysis AnalyzeUnit(UnitEntityData unit)
        {
            var result = new DangerAnalysis { Unit = unit };

            if (unit == null || !unit.IsInGame)
                return result;

            try
            {
                // ★ v0.3.14 (test5): the same live areas and the same exit as the tactical engine's "Leave <area>"
                // (InsideDanger / FindExit). Two separate rules disagreed: an ended Stinking Cloud still listed Seelah as
                // inside (438 "ShouldEscape" lines in one fight, no escape possible), and near a live cloud's edge the old
                // centre-distance test found no better spot while the game still counted her inside (she fell from 100 %
                // to 9 % HP in it).
                var inside = InsideDanger(unit);
                foreach (var aoe in inside.Areas)
                {
                    var danger = EvaluateEffectDanger(unit, aoe, quiet: true);
                    if (danger == null || danger.DangerLevel <= 0f) continue;
                    result.DangerousEffects.Add(danger);
                    result.TotalDangerLevel += danger.DangerLevel;
                }

                result.IsInDanger = result.DangerousEffects.Count > 0;

                if (result.ShouldEscape)
                    result.SuggestedEscapePosition = FindExit(unit, inside, ESCAPE_MAX_DISTANCE, null, out _);

                // ★ v0.3.17: the line is only built when it can be logged (real time asks every half second).
                if (result.IsInDanger && (result.ShouldEscape || Main.VerboseOn))
                {
                    // ★ v0.3.14: normal level only when it changes (848 identical lines for Seelah in one session).
                    string line = $"[AoeDanger] {unit.CharacterName}: Danger={result.TotalDangerLevel:F2}, " +
                                  $"Effects={result.DangerousEffects.Count} ({string.Join(", ", result.DangerousEffects.Select(d => d.Name))}), " +
                                  $"ShouldEscape={result.ShouldEscape}" +
                                  (result.ShouldEscape && !result.SuggestedEscapePosition.HasValue ? " - no way out found" : "");
                    if (result.ShouldEscape && (!LastReport.TryGetValue(unit.UniqueId, out var last) || last != line))
                        Main.Log(line);
                    else
                        Main.Verbose(line);
                    LastReport[unit.UniqueId] = line;
                }
                else LastReport.Remove(unit.UniqueId);
            }
            catch (Exception ex)
            {
                Main.Error($"[AoeDanger] Error analyzing {unit?.CharacterName}: {ex.Message}");
            }

            return result;
        }

        private static readonly Dictionary<string, string> LastReport = new Dictionary<string, string>();

        /// <summary>
        /// 특정 위치가 위험 지역인지 확인 (이동 계획용)
        /// </summary>
        public static bool IsPositionDangerous(UnitEntityData unit, Vector3 position)
        {
            if (unit == null)
                return false;

            try
            {
                var areaEffects = Game.Instance?.State?.AreaEffects;
                if (areaEffects == null)
                    return false;

                foreach (var aoe in areaEffects)
                {
                    if (!IsLive(aoe))
                        continue;

                    // 위치가 AoE 범위 안인지 확인
                    if (!Covers(aoe, position, unit.Corpulence))
                        continue;

                    // 유닛에게 위험한 AoE인지 확인
                    if (IsEffectHarmfulToUnit(unit, aoe))
                        return true;
                }
            }
            catch (Exception ex)
            {
                Main.Error($"[AoeDanger] Error checking position: {ex.Message}");
            }

            return false;
        }

        /// <summary>
        /// ★ v0.3.14: the worst danger level (0..1) of the harmful areas a move from <paramref name="from"/> to
        /// <paramref name="to"/> would enter: the destination or a point on the straight path lies inside, and the unit
        /// is not already inside (entering is what triggers the save). Seelah walked into Orvath's Entangle to attack;
        /// turn-based moves never looked at areas. <paramref name="name"/> is the area's name for the decision log.
        /// </summary>
        public static float EnteringDanger(UnitEntityData unit, Vector3 from, Vector3 to, out string name) =>
            EnteringDanger(unit, from, to, out name, out _, out _);

        /// <summary>
        /// ★ v0.3.15: also whether <paramref name="to"/> itself lies in the worst area (the unit would stay in it, saving
        /// every round) and the rounds that area has left.
        /// </summary>
        public static float EnteringDanger(UnitEntityData unit, Vector3 from, Vector3 to, out string name,
                                           out float roundsLeft, out bool endsInside) =>
            EnteringDanger(unit, from, to, out name, out roundsLeft, out endsInside, out _);

        /// <summary>
        /// ★ v0.3.17: also the chance an area that impedes movement (Grease, Entangle, Web) or applies hard control
        /// (Stinking Cloud's nausea) stops what the walk was for: its save failed, the unit falls, is held or cannot act. Seelah walked into Grease to
        /// attack the last Zombie, fell and never attacked; the price counted the rounds in the area, not the lost attack.
        /// </summary>
        public static float EnteringDanger(UnitEntityData unit, Vector3 from, Vector3 to, out string name,
                                           out float roundsLeft, out bool endsInside, out float stopChance) =>
            EnteringDanger(unit, from, to, out name, out roundsLeft, out endsInside, out stopChance, out _, out _, out _);

        /// <remarks>★ v0.3.18 (test10): <paramref name="movementOnly"/> - the worst area only hinders movement (Grease,
        /// Entangle, Web: no damage, no hard control, no poison, fear, stun, paralysis or death effect);
        /// <paramref name="affectChance"/> - the chance it affects the unit (its save, immunities, time left).</remarks>
        public static float EnteringDanger(UnitEntityData unit, Vector3 from, Vector3 to, out string name,
                                           out float roundsLeft, out bool endsInside, out float stopChance,
                                           out bool movementOnly, out float affectChance) =>
            EnteringDanger(unit, from, to, out name, out roundsLeft, out endsInside, out stopChance, out movementOnly, out affectChance, out _);

        /// <remarks>★ v0.3.18 (test11): <paramref name="lingerRounds"/> - how long the worst area's effect lasts after a
        /// failed save, outside it too (see <see cref="DangerousEffect.LingerRounds"/>).</remarks>
        public static float EnteringDanger(UnitEntityData unit, Vector3 from, Vector3 to, out string name,
                                           out float roundsLeft, out bool endsInside, out float stopChance,
                                           out bool movementOnly, out float affectChance, out float lingerRounds)
        {
            lingerRounds = 0f;
            movementOnly = false;
            affectChance = 0f;
            bool otherKind = false;   // ★ (review) another entered area does more than hinder movement
            name = null;
            roundsLeft = 0f;
            endsInside = false;
            stopChance = 0f;
            float worst = 0f;
            if (unit == null) return 0f;
            try
            {
                var areaEffects = Game.Instance?.State?.AreaEffects;
                if (areaEffects == null) return 0f;
                float length = Vector3.Distance(from, to);
                int samples = Math.Max(1, Mathf.CeilToInt(length / 1f));
                foreach (var aoe in areaEffects)
                {
                    if (!IsLive(aoe)) continue;
                    if (IsUnitInsideEffect(unit, aoe)) continue;
                    bool enters = false;
                    for (int i = 1; i <= samples && !enters; i++)
                        enters = Covers(aoe, Vector3.Lerp(from, to, i / (float)samples), unit.Corpulence);
                    if (!enters) continue;
                    var danger = EvaluateEffectDanger(unit, aoe, quiet: true);
                    // Hard control (Stinking Cloud's nausea, sleep, paralysis) also takes away the attack after the walk.
                    if (danger != null && danger.DangerLevel > 0f &&
                        (danger.IsMovementImpairing || ControlOf(aoe) == CompanionAI_Pathfinder.Tactics.ControlStrength.Hard))
                        stopChance = 1f - (1f - stopChance) * (1f - Mathf.Clamp01(danger.AffectChance));
                    if (danger != null && danger.DangerLevel > 0f && !IsMovementOnly(danger, aoe)) otherKind = true;
                    if (danger == null || danger.DangerLevel <= worst) continue;
                    worst = danger.DangerLevel;
                    name = aoe.Context?.SourceAbilityContext?.Ability?.Name ?? danger.Name;
                    endsInside = Covers(aoe, to, unit.Corpulence);
                    roundsLeft = RoundsLeft(aoe);
                    movementOnly = IsMovementOnly(danger, aoe);
                    affectChance = Mathf.Clamp01(danger.AffectChance);
                    lingerRounds = danger.LingerRounds;
                }
            }
            catch (Exception ex)
            {
                Main.Verbose($"[AoeDanger] entering check {unit.CharacterName}: {ex.Message}");
            }
            if (otherKind) movementOnly = false;
            // ★ v0.3.18 (test10): an ally's area that is about to land (real time).
            var pending = PendingOnWay(unit, from, to);
            if (pending != null && pending.Danger * 0.5f > worst)
            {
                // The save is not known yet: half the danger and half a stop chance, as for an even save.
                worst = pending.Danger * 0.5f;
                name = pending.Name + " (being cast)";
                Vector3 end = to - pending.Point; end.y = 0f;
                endsInside = end.magnitude <= pending.Radius + unit.Corpulence;
                roundsLeft = 10f;
                movementOnly = pending.MovementOnly;
                affectChance = 0.5f;
                if (pending.MovementOnly) stopChance = 1f - (1f - stopChance) * 0.5f;
            }
            return worst;
        }

        /// <summary>
        /// ★ v0.3.18 (test10): area spells a party member has been ordered to cast in real time and that have not landed
        /// yet. Real time decides for everyone at once: Camellia walked at the Forsaken Kobold Archer 12 lines after Orvath
        /// was ordered to put Grease on it, before the area existed, and lay prone for nearly the whole fight. While the
        /// cast's command runs (at most 8 s), its circle counts as the area it will be. Turn-based never has one: only
        /// one unit acts at a time and its cast lands before the next decides.
        /// </summary>
        private sealed class PendingArea
        {
            public Vector3 Point;
            public float Radius;
            public string Name;
            public float Danger;
            public bool MovementOnly;
            public Kingmaker.UnitLogic.Commands.Base.UnitCommand Command;
            public double Until;
        }

        private static readonly List<PendingArea> Pending = new List<PendingArea>();

        private static double Now => Game.Instance?.TimeController?.GameTime.TotalSeconds ?? 0d;

        public static void RegisterPending(Vector3 point, float radius, string name, bool damaging, bool controls, bool movementOnly,
                                           Kingmaker.UnitLogic.Commands.Base.UnitCommand command)
        {
            if (command == null || radius <= 0f || CombatController.IsInTurnBasedCombat()) return;
            Prune();
            Pending.Add(new PendingArea
            {
                Point = point, Radius = radius, Name = name, Command = command, Until = Now + 8d,
                Danger = Math.Max(0.3f, (damaging ? 0.6f : 0f) + (controls ? 0.4f : 0f)),
                MovementOnly = movementOnly && !damaging
            });
        }

        public static void ClearPending() => Pending.Clear();

        /// <summary>
        /// ★ (review) a live area harmful to <paramref name="unit"/>, or a pending one, lies within <paramref name="radius"/>
        /// of <paramref name="center"/>: only then is a real-time walk worth checking.
        /// </summary>
        public static bool HarmfulAreaNear(UnitEntityData unit, Vector3 center, float radius)
        {
            try
            {
                Prune();
                foreach (var p in Pending)
                    if (Vector3.Distance(p.Point, center) <= radius + p.Radius) return true;
                var areaEffects = Game.Instance?.State?.AreaEffects;
                if (areaEffects == null) return false;
                foreach (var aoe in areaEffects)
                {
                    if (!IsLive(aoe)) continue;
                    float size = 10f;
                    try { size = aoe.Blueprint.Size.Meters; } catch { }
                    if (Vector3.Distance(aoe.Position, center) > radius + size) continue;
                    if (IsEffectHarmfulToUnit(unit, aoe)) return true;
                }
            }
            catch
            {
                return true;   // unsure: check the walk
            }
            return false;
        }

        private static void Prune()
        {
            double now = Now;
            Pending.RemoveAll(p => p.Command == null || p.Command.IsFinished || now > p.Until);
        }

        /// <summary>The pending area <paramref name="from"/> → <paramref name="to"/> walks into (real time), or null.</summary>
        private static PendingArea PendingOnWay(UnitEntityData unit, Vector3 from, Vector3 to)
        {
            if (Pending.Count == 0 || CombatController.IsInTurnBasedCombat()) return null;
            Prune();
            float length = Vector3.Distance(from, to);
            int samples = Math.Max(1, Mathf.CeilToInt(length / 1f));
            foreach (var area in Pending)
            {
                float reach = area.Radius + unit.Corpulence;
                Vector3 flatFrom = from - area.Point; flatFrom.y = 0f;
                if (flatFrom.magnitude <= reach) continue;   // already inside: entering is what matters
                for (int i = 1; i <= samples; i++)
                {
                    Vector3 d = Vector3.Lerp(from, to, i / (float)samples) - area.Point;
                    d.y = 0f;
                    if (d.magnitude <= reach) return area;
                }
            }
            return null;
        }

        /// <summary>★ v0.3.18 (test10): an area whose only harm is to movement (a fall, being held in place).</summary>
        private static bool IsMovementOnly(DangerousEffect danger, AreaEffectEntityData aoe)
        {
            const SpellDescriptor other = SpellDescriptor.Death | SpellDescriptor.Poison | SpellDescriptor.Stun |
                                          SpellDescriptor.Paralysis | SpellDescriptor.Fear;
            if (!danger.IsMovementImpairing || danger.IsDamaging || (danger.Descriptors & other) != 0) return false;
            var profile = SourceProfile(aoe);
            // ★ (review) the spawning ability's own analysis decides when it is known: no damage of any kind (Spike Growth's
            // piercing damage has no damage descriptor), no hard control, and nothing but a fall (not Entangle or Web, which
            // hold a unit for rounds; not a blinding or sickening cloud).
            if (profile == null) return ControlOf(aoe) != CompanionAI_Pathfinder.Tactics.ControlStrength.Hard;
            return !profile.DealsDamage && profile.ControlStrength != CompanionAI_Pathfinder.Tactics.ControlStrength.Hard &&
                   profile.OnlyKnocksDown;
        }

        /// <summary>★ v0.3.14: the harmful areas a unit stands in (<see cref="InsideDanger"/>).</summary>
        public sealed class AreasInside
        {
            public readonly List<AreaEffectEntityData> Areas = new List<AreaEffectEntityData>();
            /// <summary>Worst area's danger level plus a quarter of the others' (0 when none).</summary>
            public float Danger;
            /// <summary>The worst area's name, for the decision log.</summary>
            public string Name;
            /// <summary>★ v0.3.18 (review): rounds the worst area still lasts (as for entering one).</summary>
            public float RoundsLeft;
            /// <summary>★ v0.3.18 (test10): the worst area only hinders movement (see EnteringDanger).</summary>
            public bool MovementOnly;
            /// <summary>★ v0.3.18 (test10): the chance the worst area affects the unit.</summary>
            public float AffectChance;

            /// <summary>The unit's body radius: the game counts a unit inside when its body overlaps the area.</summary>
            public float Corpulence;

            /// <summary>
            /// The unit standing at <paramref name="point"/> would be outside every one of these areas, by the game's own
            /// test (AreaEffectView.Contains: the area's shape against the unit's body). ★ test5: the centre distance
            /// without the body sent Orvath out of a Stinking Cloud in three walks (3.3 m, 1.6 m, 1.0 m).
            /// </summary>
            public bool IsOutside(Vector3 point) => Areas.All(a => !Covers(a, point, Corpulence));
        }

        /// <summary>
        /// ★ v0.3.14: the harmful areas <paramref name="unit"/> is standing in, rated like <see cref="EnteringDanger"/>
        /// (same danger levels), without logging. Turn-based moves only priced walking into an area; nothing ever
        /// considered walking out of the one the unit stood in (Orvath, nauseated in a Stinking Cloud).
        /// </summary>
        public static AreasInside InsideDanger(UnitEntityData unit)
        {
            var result = new AreasInside();
            if (unit == null) return result;
            result.Corpulence = unit.Corpulence;
            try
            {
                var areaEffects = Game.Instance?.State?.AreaEffects;
                if (areaEffects == null) return result;
                float worst = 0f, rest = 0f;
                foreach (var aoe in areaEffects)
                {
                    if (!IsLive(aoe)) continue;
                    if (!IsUnitInsideEffect(unit, aoe)) continue;
                    var danger = EvaluateEffectDanger(unit, aoe, quiet: true);
                    if (danger == null || danger.DangerLevel <= 0f) continue;
                    result.Areas.Add(aoe);
                    if (danger.DangerLevel > worst)
                    {
                        rest += worst;
                        worst = danger.DangerLevel;
                        result.Name = aoe.Context?.SourceAbilityContext?.Ability?.Name ?? danger.Name;
                        result.RoundsLeft = RoundsLeft(aoe);
                        result.MovementOnly = IsMovementOnly(danger, aoe);
                        result.AffectChance = Mathf.Clamp01(danger.AffectChance);
                    }
                    else rest += danger.DangerLevel;
                }
                result.Danger = worst + 0.25f * rest;
            }
            catch (Exception ex)
            {
                Main.Verbose($"[AoeDanger] inside check {unit.CharacterName}: {ex.Message}");
            }
            return result;
        }

        /// <summary>
        /// 모든 위험 지역 효과 목록 반환 (디버그/UI용)
        /// </summary>
        public static List<AreaEffectEntityData> GetAllDangerousEffects(UnitEntityData forUnit)
        {
            var result = new List<AreaEffectEntityData>();

            try
            {
                var areaEffects = Game.Instance?.State?.AreaEffects;
                if (areaEffects == null)
                    return result;

                foreach (var aoe in areaEffects)
                {
                    if (aoe == null || !aoe.IsInGame)
                        continue;

                    if (IsEffectHarmfulToUnit(forUnit, aoe))
                        result.Add(aoe);
                }
            }
            catch { }

            return result;
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// ★ v0.3.14 (test5): an area that still acts: in the game, not ended, with its view (its shape) loaded. An ended
        /// area can stay in the state's list for a while with its last list of units inside.
        /// </summary>
        private static bool IsLive(AreaEffectEntityData aoe)
        {
            try
            {
                return aoe != null && aoe.IsInGame && !aoe.IsEnded &&
                       (aoe.View != null || aoe.Blueprint?.Shape == AreaEffectShape.AllArea);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>The unit is inside the area now (the game's own list, live areas only).</summary>
        private static bool IsUnitInsideEffect(UnitEntityData unit, AreaEffectEntityData aoe)
        {
            if (!IsLive(aoe)) return false;
            try
            {
                return aoe.InGameUnitsInside?.Contains(unit) ?? false;
            }
            catch
            {
                return Covers(aoe, unit.Position, unit.Corpulence);
            }
        }

        /// <summary>
        /// ★ v0.3.14 (test5): a body of radius <paramref name="corpulence"/> standing at <paramref name="point"/> overlaps
        /// the area: the game's own shape test (AreaEffectView.Contains uses Shape.Contains(position, corpulence)), which
        /// also handles non-round areas. Falls back to the blueprint's radius.
        /// </summary>
        private static bool Covers(AreaEffectEntityData aoe, Vector3 point, float corpulence)
        {
            try
            {
                if (aoe.Blueprint?.Shape == AreaEffectShape.AllArea) return true;
                var shape = aoe.View?.Shape;
                if (shape != null) return shape.Contains(point, corpulence);
                Vector3 center = aoe.Position;
                return Vector3.Distance(point, center) <= (aoe.Blueprint?.Size.Meters ?? 0f) + corpulence;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// AoE가 유닛에게 위험한지 판단
        /// </summary>
        private static bool IsEffectHarmfulToUnit(UnitEntityData unit, AreaEffectEntityData aoe)
        {
            try
            {
                var context = aoe.Context;
                if (context == null)
                    return false;

                // 시전자 확인
                var caster = context.MaybeCaster;

                // 아군이 시전한 효과는 일반적으로 안전
                if (caster != null && caster.IsPlayerFaction == unit.IsPlayerFaction)
                {
                    // 단, AffectEnemies와 동시에 AffectAllies가 false면 안전
                    var blueprint = aoe.Blueprint;
                    if (blueprint != null && !blueprint.AffectEnemies)
                        return false;

                    // ★ v0.3.14: Selective, as the game applied it when the area spawned (Tabletop Tweaks keeps lasting
                    // areas affecting allies). Before, every own area that could target allies counted, Selective or not.
                    if (!CompanionAI_Pathfinder.Tactics.SelectiveRule.LiveAreaAffectsAllies(aoe))
                        return false;

                    // 아군 타겟 가능하고 적 타겟 가능하면 위험할 수 있음
                    // (Fireball, Cloudkill 등)
                    if (blueprint != null && blueprint.AffectEnemies && blueprint.CanTargetAllies)
                    {
                        Content(aoe, out bool damaging, out bool impairing);
                        if (damaging || impairing)
                            return true;
                    }

                    return false;
                }

                // 적이 시전한 효과
                if (caster != null && caster.IsPlayerFaction != unit.IsPlayerFaction)
                {
                    var blueprint = aoe.Blueprint;

                    // 적 전용 효과면 체크
                    if (blueprint != null && blueprint.AffectEnemies && !blueprint.CanTargetAllies)
                        return false;  // 적에게만 영향 → 아군에게 안전

                    // 아군에게 영향을 주는 적의 AoE → 위험
                    if (blueprint != null && blueprint.CanTargetAllies)
                        return true;

                    // 모두에게 영향 (AffectEnemies && AffectAllies 둘 다 true 또는 기본값)
                    return true;
                }

                // 시전자 없음 (환경 효과 등) - SpellDescriptor로 판단
                var descriptor = context.SpellDescriptor;
                return IsDamagingDescriptor(descriptor) || IsMovementImpairingDescriptor(descriptor);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 지역 효과의 위험도 평가
        /// </summary>
        private static DangerousEffect EvaluateEffectDanger(UnitEntityData unit, AreaEffectEntityData aoe, bool quiet = false)
        {
            if (!IsEffectHarmfulToUnit(unit, aoe))
                return null;

            var result = new DangerousEffect
            {
                AreaEffect = aoe,
                Name = aoe.Blueprint?.name ?? "Unknown"
            };

            try
            {
                var context = aoe.Context;
                var descriptor = context?.SpellDescriptor ?? SpellDescriptor.None;
                result.Descriptors = descriptor;

                // 피해 유형 확인
                Content(aoe, out bool damaging, out bool impairing);
                result.IsDamaging = damaging;
                result.IsMovementImpairing = impairing;

                // 위험도 계산
                float dangerLevel = 0f;

                // 직접 피해 효과 (Fire, Acid, Electricity 등)
                if (result.IsDamaging)
                    dangerLevel += 0.6f;

                // 이동 방해 (Entangle, Grease, Web)
                if (result.IsMovementImpairing)
                    dangerLevel += 0.4f;

                // 특수 위험 효과
                if ((descriptor & SpellDescriptor.Death) != 0)
                    dangerLevel += 0.8f;
                if ((descriptor & SpellDescriptor.Poison) != 0)
                    dangerLevel += 0.3f;
                if ((descriptor & SpellDescriptor.Stun) != 0)
                    dangerLevel += 0.5f;
                if ((descriptor & SpellDescriptor.Paralysis) != 0)
                    dangerLevel += 0.6f;
                if ((descriptor & SpellDescriptor.Fear) != 0)
                    dangerLevel += 0.3f;

                // ★ v0.3.14: what the area applies, from the analysis of the ability that spawned it. Stinking Cloud's
                // descriptor says only Poison (0.3, under the 0.5 escape level), yet it nauseates: no actions at all.
                // Orvath, Camellia and Seelah stood in one for a whole fight (887 checks, never "ShouldEscape").
                switch (ControlOf(aoe))
                {
                    case CompanionAI_Pathfinder.Tactics.ControlStrength.Hard: dangerLevel = Math.Max(dangerLevel, 0.3f) + 0.5f; break;
                    case CompanionAI_Pathfinder.Tactics.ControlStrength.Soft: dangerLevel = Math.Max(dangerLevel, 0.4f); break;
                }

                // 최대 1.0으로 제한
                // ★ v0.3.14 (test5): weighted by the chance the area affects this unit at all before it ends (its save,
                // its immunities, the time left). Camellia (good Fortitude) left a Stinking Cloud past two attacks of
                // opportunity, was stopped by the first and stayed inside anyway.
                result.AffectChance = AffectChance(unit, aoe);
                result.DangerLevel = Mathf.Clamp01(dangerLevel) * result.AffectChance;
                result.LingerRounds = LingerRounds(unit, aoe);

                if (!quiet)
                {
                    Main.Verbose($"[AoeDanger] Effect '{result.Name}': Danger={result.DangerLevel:F2}, " +
                        $"Desc={descriptor}, Damaging={result.IsDamaging}, MovImpair={result.IsMovementImpairing}");
                }
            }
            catch (Exception ex)
            {
                Main.Error($"[AoeDanger] Error evaluating effect: {ex.Message}");
                result.DangerLevel = 0.3f;  // 알 수 없는 효과는 중간 위험도
            }

            return result;
        }

        /// <summary>Farthest exit walk considered, metres.</summary>
        private const float ESCAPE_MAX_DISTANCE = 9f;

        /// <summary>
        /// ★ v0.3.14 (test5): the nearest spot within <paramref name="maxDistance"/> where the unit would stand outside all
        /// the areas in <paramref name="inside"/>, without walking into another one, reachable on the navmesh. Shared by
        /// the tactical engine's "Leave <area>" and real time's emergency escape. With <paramref name="prefer"/> (e.g.
        /// "keeps an enemy in reach"), a slightly longer way out that satisfies it wins over the nearest one.
        /// </summary>
        public static Vector3? FindExit(UnitEntityData unit, AreasInside inside, float maxDistance,
                                        Func<Vector3, bool> prefer, out bool preferred)
        {
            preferred = false;
            if (unit == null || inside == null || inside.Areas.Count == 0) return null;
            Vector3? best = null;
            float bestScore = float.MinValue;
            try
            {
                var enemies = unit.CombatState?.EngagedBy?.Where(e => e != null).ToList() ?? new List<UnitEntityData>();
                foreach (float ring in new[] { 1.5f, 3f, 4.5f, 6f, 9f })
                {
                    if (ring > maxDistance) break;
                    for (int i = 0; i < 16; i++)
                    {
                        float angle = i * 22.5f * Mathf.Deg2Rad;
                        var point = unit.Position + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * ring;
                        if (!inside.IsOutside(point)) continue;
                        if (EnteringDanger(unit, unit.Position, point, out _) > 0f) continue;
                        if (!IsPositionValid(unit.Position, point)) continue;
                        bool keeps = prefer?.Invoke(point) ?? false;
                        // Shortest way out; away from whoever engages the unit as a tie-break.
                        float away = enemies.Count > 0 ? Math.Min(10f, enemies.Min(e => Vector3.Distance(point, e.Position))) : 0f;
                        float score = (keeps ? 10f : 0f) - ring + 0.1f * away;
                        if (score > bestScore)
                        {
                            bestScore = score;
                            best = point;
                            preferred = keeps;
                        }
                    }
                    if (best.HasValue && (prefer == null || preferred)) break;
                }
            }
            catch (Exception ex)
            {
                Main.Verbose($"[AoeDanger] exit search {unit.CharacterName}: {ex.Message}");
            }
            return best;
        }

        /// <summary>
        /// ★ v0.2.59: 위치가 유효하고 도달 가능한지 확인
        /// ★ v0.3.14: public - real-time back-off points use it too (Tactics.TacticalEngine.FindRetreatPoint).
        /// </summary>
        public static bool IsPositionValid(Vector3 from, Vector3 to)
        {
            try
            {
                // 1. 목표 위치가 NavMesh 위에 있는지 확인
                if (!ObstacleAnalyzer.IsPointInsideNavMesh(to))
                {
                    // NavMesh 밖이면 가장 가까운 유효 위치로 스냅
                    var nearest = ObstacleAnalyzer.GetNearestNode(to, null);
                    if (nearest.node == null)
                        return false;

                    // 스냅된 위치가 원래 위치에서 너무 멀면 무효
                    if (Vector3.Distance(to, nearest.position) > 2f)
                        return false;
                }

                // 2. 경로가 막혀있는지 확인
                Vector3 blocked = ObstacleAnalyzer.TraceAlongNavmesh(from, to);
                float pathBlocked = Vector3.Distance(blocked, to);

                // 목표 지점과 막힌 지점 차이가 1m 이상이면 경로 막힘
                if (pathBlocked > 1f)
                    return false;

                return true;
            }
            catch
            {
                return true;  // 오류 시 기존 동작 유지
            }
        }

        private static readonly System.Reflection.FieldInfo CreationField =
            HarmonyLib.AccessTools.Field(typeof(AreaEffectEntityData), "m_CreationTime");
        private static readonly System.Reflection.FieldInfo DurationField =
            HarmonyLib.AccessTools.Field(typeof(AreaEffectEntityData), "m_Duration");

        /// <summary>
        /// ★ v0.3.14 (test5): chance a lasting area does anything to <paramref name="unit"/> in the next round:
        /// - immune by descriptor, the game's own test for who the area affects (UnitPartSpellResistance.IsImmune with
        ///   spellDescriptorOnly, as AreaEffectEntityData.ShouldUnitBeInside): 0;
        /// - immune to the control it applies and dealing no damage: 0;
        /// - otherwise the chance to fail the area's save (its live DC against the unit's save; damage halved on a save
        ///   still counts half), 1 when the area has no save;
        /// - times the share of a round the area still lasts.
        /// </summary>
        /// <summary>★ v0.3.19 (test7): party members' near-certain area effects, reported once each (see AffectChance).</summary>
        private static readonly HashSet<string> SureReported = new HashSet<string>();

        private static float AffectChance(UnitEntityData unit, AreaEffectEntityData aoe)
        {
            float result = AffectChanceRaw(unit, aoe, out string how);
            // ★ v0.3.19 (test7): Orvath's revive of Camellia inside his Grease was priced as a walk that never arrives
            // ("the walk x0.00") for 17 rounds; the inputs say whether that was the save or something unread.
            try
            {
                if (result >= 0.9f && unit.IsPlayerFaction && SureReported.Add($"{unit.UniqueId}|{aoe.Blueprint?.AssetGuid}"))
                    CompanionAI_Pathfinder.Main.Log($"[AoeDanger] {unit.CharacterName}: {aoe.Context?.SourceAbilityContext?.Ability?.Name ?? aoe.Blueprint?.name} " +
                                                    $"affects it at {result:P0} ({how})");
                if (SureReported.Count > 256) SureReported.Clear();
            }
            catch { }
            return result;
        }

        private static float AffectChanceRaw(UnitEntityData unit, AreaEffectEntityData aoe, out string how)
        {
            how = "no save known";
            try
            {
                var context = aoe.Context;
                if (unit.Get<Kingmaker.UnitLogic.Parts.UnitPartSpellResistance>()?.IsImmune(context, spellDescriptorOnly: true) ?? false)
                    return 0f;
                float chance = 1f;
                var profile = SourceProfile(aoe);
                if (profile != null)
                {
                    if (!profile.DealsDamage && profile.Control != CCType.None && AbilityClassifier.HasCCImmunity(unit, profile.Control))
                        return 0f;
                    int dc = context?.Params?.DC ?? 0;
                    if (profile.Save != Kingmaker.EntitySystem.Stats.SavingThrowType.Unknown && dc > 0)
                    {
                        int bonus;
                        switch (profile.Save)
                        {
                            case Kingmaker.EntitySystem.Stats.SavingThrowType.Fortitude: bonus = unit.Stats.SaveFortitude.ModifiedValue; break;
                            case Kingmaker.EntitySystem.Stats.SavingThrowType.Reflex: bonus = unit.Stats.SaveReflex.ModifiedValue; break;
                            case Kingmaker.EntitySystem.Stats.SavingThrowType.Will: bonus = unit.Stats.SaveWill.ModifiedValue; break;
                            default: bonus = int.MinValue; break;
                        }
                        if (bonus != int.MinValue)
                        {
                            float fail = Mathf.Clamp((dc - bonus - 1) / 20f, 0.05f, 0.95f);
                            chance = profile.DealsDamage && profile.HalfOnSave ? 0.5f + 0.5f * fail : fail;
                            how = $"{profile.Save} {bonus:+#;-#;0} vs DC {dc}";
                        }
                    }
                    else how = $"save {profile.Save}, DC {dc}";
                }
                if (CreationField != null && DurationField != null && DurationField.GetValue(aoe) is TimeSpan duration)
                {
                    var created = (TimeSpan)CreationField.GetValue(aoe);
                    double left = (created + duration - Game.Instance.TimeController.GameTime).TotalSeconds;
                    chance *= Mathf.Clamp01((float)(left / 6.0));
                }
                return chance;
            }
            catch (Exception ex)
            {
                how = "unreadable: " + ex.Message;
                return 1f;
            }
        }

        /// <summary>★ v0.3.15: rounds the area has left (an area without a known end: 10, longer than a fight).</summary>
        public static float RoundsLeft(AreaEffectEntityData aoe)
        {
            try
            {
                if (CreationField != null && DurationField != null && DurationField.GetValue(aoe) is TimeSpan duration &&
                    duration > TimeSpan.Zero)
                {
                    var created = (TimeSpan)CreationField.GetValue(aoe);
                    double left = (created + duration - Game.Instance.TimeController.GameTime).TotalSeconds;
                    return Math.Max(0f, (float)(left / 6.0));
                }
            }
            catch
            {
            }
            return 10f;
        }

        /// <summary>
        /// ★ v0.3.18 (test11): how long what the area puts on a unit lasts once applied, from the spawning ability's
        /// analysis: a buff given a duration (Stinking Cloud's nausea goes on for 1d4 rounds after the unit leaves). An
        /// area buff that only lasts while inside has no duration and gives 0. Orvath walked a step into his Dretch's
        /// Stinking Cloud for a Color Spray and lost three turns to nausea outside it; the walk was priced as one round.
        /// </summary>
        private static float LingerRounds(UnitEntityData unit, AreaEffectEntityData aoe)
        {
            try
            {
                var profile = SourceProfile(aoe);
                if (profile == null) return 0f;
                float rounds = profile.EffectRoundsOn(aoe.Context?.SourceAbilityContext?.Ability, unit);
                return rounds > 0f ? Math.Min(rounds, 10f) : 0f;
            }
            catch
            {
                return 0f;
            }
        }

        /// <summary>★ v0.3.14: how strongly the area controls those inside (from the spawning ability's analysis).</summary>
        private static CompanionAI_Pathfinder.Tactics.ControlStrength ControlOf(AreaEffectEntityData aoe)
        {
            try
            {
                var profile = SourceProfile(aoe);
                return profile?.ControlStrength ?? CompanionAI_Pathfinder.Tactics.ControlStrength.None;
            }
            catch
            {
                return CompanionAI_Pathfinder.Tactics.ControlStrength.None;
            }
        }

        /// <summary>★ v0.3.14: the profile of the ability that spawned this area, if the area is one of its own.</summary>
        private static CompanionAI_Pathfinder.Tactics.AbilityProfile SourceProfile(AreaEffectEntityData aoe)
        {
            var source = aoe.Context?.SourceAbilityContext?.Ability;
            if (source == null) return null;
            var profile = CompanionAI_Pathfinder.Tactics.AbilityProfile.Get(source);
            return profile != null && profile.SpawnedAreas.Contains(aoe.Blueprint) ? profile : null;
        }

        /// <summary>
        /// ★ v0.3.14: what the area does, from its descriptor or, when the descriptor says nothing, from the turn-based
        /// engine's analysis of the ability that spawned it (an area with damage, control or a debuff in its actions).
        /// </summary>
        private static void Content(AreaEffectEntityData aoe, out bool damaging, out bool impairing)
        {
            var descriptor = aoe.Context?.SpellDescriptor ?? SpellDescriptor.None;
            damaging = IsDamagingDescriptor(descriptor);
            impairing = IsMovementImpairingDescriptor(descriptor);
            if (damaging || impairing) return;
            try
            {
                var profile = SourceProfile(aoe);
                if (profile == null) return;
                damaging = profile.DealsDamage;
                impairing = profile.ControlStrength != CompanionAI_Pathfinder.Tactics.ControlStrength.None || profile.AppliesHarmfulBuffs;
            }
            catch
            {
            }
        }

        /// <summary>
        /// 피해 관련 SpellDescriptor 확인
        /// </summary>
        private static bool IsDamagingDescriptor(SpellDescriptor descriptor)
        {
            // 피해 유형 디스크립터들
            return (descriptor & (
                SpellDescriptor.Fire |
                SpellDescriptor.Cold |
                SpellDescriptor.Acid |
                SpellDescriptor.Electricity |
                SpellDescriptor.Sonic |
                SpellDescriptor.Force |
                SpellDescriptor.Death
            )) != 0;
        }

        /// <summary>
        /// 이동 방해 SpellDescriptor 확인
        /// </summary>
        private static bool IsMovementImpairingDescriptor(SpellDescriptor descriptor)
        {
            return (descriptor & (
                SpellDescriptor.MovementImpairing |
                SpellDescriptor.Ground |
                SpellDescriptor.Paralysis |
                SpellDescriptor.Stun
            )) != 0;
        }

        #endregion
    }
}
