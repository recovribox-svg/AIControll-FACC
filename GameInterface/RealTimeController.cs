using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Kingmaker.Blueprints;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Abilities.Components;
using Kingmaker.UnitLogic.Commands;
using Kingmaker.UnitLogic.Commands.Base;
using Kingmaker.UnitLogic.Mechanics.Actions;
using Kingmaker.Utility;
using CompanionAI_Pathfinder.Abstraction;
using CompanionAI_Pathfinder.Analysis;
using CompanionAI_Pathfinder.Settings;
using CompanionAI_Pathfinder.Core;
using CompanionAI_Pathfinder.Core.DecisionEngine;
using CompanionAI_Pathfinder.Scoring;
using UnityEngine;

namespace CompanionAI_Pathfinder.GameInterface
{
    /// <summary>
    /// Real-time combat AI controller
    /// v0.2.0: 계획 시스템 통합 - SituationAnalyzer, TargetScorer, Role-based 결정
    /// ★ v0.2.49: AoE 위험 지역 탈출 로직 추가
    /// </summary>
    public class RealTimeController
    {
        #region Constants

        // Decision interval (seconds). ★ v0.3.12: the "Real-Time Decision Interval" setting (0.2-2 s, default 0.5), which
        // the UI offered but nothing read.
        private static float DecisionInterval =>
            Mathf.Clamp(Settings.ModSettings.Instance?.RealTimeDecisionInterval ?? 0.5f, 0.2f, 2f);

        // ★ v0.2.32: 위상 분산 - 유닛별 최대 오프셋 (0~0.45초 분산)
        private const float MAX_PHASE_OFFSET = 0.45f;
        private const int PHASE_DIVISIONS = 10;  // 10개 위상으로 분산

        // ★ v0.2.32: 시간 예산 - 프레임당 최대 AI 처리 시간 (ms)
        private const float MAX_AI_TIME_MS = 3.0f;

        // Max engagement distance (meters)
        private const float MAX_ENGAGEMENT_DISTANCE = 30f;

        // RangePreference distance constants
        private const float MELEE_RANGE = 2f;
        private const float RANGED_MIN_DIST = 6f;
        private const float RANGED_OPTIMAL_DIST = 10f;
        private const float RETREAT_DISTANCE = 5f;

        // HP thresholds
        private const float HP_EMERGENCY_THRESHOLD = 30f;
        private const float HP_HEAL_ALLY_THRESHOLD = 50f;

        #endregion

        #region State

        // Last decision time per unit
        private readonly Dictionary<string, float> _lastDecisionTime = new Dictionary<string, float>();

        // ★ v0.2.32: 유닛별 위상 오프셋 (Phase Staggering)
        // 캐시: 유닛 ID → 오프셋 값
        private readonly Dictionary<string, float> _unitPhaseOffsets = new Dictionary<string, float>();

        // ★ v0.2.32: 시간 예산 시스템
        // 프레임당 AI 처리 시간 제한용 스톱워치
        private readonly Stopwatch _frameBudget = new Stopwatch();
        private int _lastBudgetFrame = -1;  // 마지막으로 스톱워치 리셋한 프레임

        // ★ v0.2.32: 처리 못한 유닛 대기열
        private readonly Queue<string> _pendingProcessUnits = new Queue<string>();

        // v0.2.1: Ability cooldown tracking (prevent free ability spam)
        // Key: "unitId:abilityGuid", Value: last use time
        private readonly Dictionary<string, float> _abilityCooldowns = new Dictionary<string, float>();
        private const float FREE_ABILITY_COOLDOWN = 6f; // 6초 쿨다운 (무료 능력)
        // v0.2.6: DEMORALIZE_COOLDOWN 제거됨 - Demoralize는 완전 비활성화

        // v0.2.2: Pending buff queue (units that left combat with commands in progress)
        private readonly HashSet<string> _pendingBuffUnits = new HashSet<string>();
        private const float PENDING_BUFF_RETRY_INTERVAL = 1.0f;
        private readonly Dictionary<string, float> _pendingBuffLastCheck = new Dictionary<string, float>();

        // Statistics
        private int _processCount = 0;
        private int _attackCount = 0;
        private int _moveCount = 0;
        private int _healCount = 0;
        private int _buffCount = 0;
        private int _debuffCount = 0;

        // Dependencies
        private readonly SituationAnalyzer _analyzer;

        #endregion

        #region Singleton

        private static RealTimeController _instance;
        public static RealTimeController Instance => _instance ?? (_instance = new RealTimeController());

        private RealTimeController()
        {
            _analyzer = new SituationAnalyzer();
        }

        #endregion

        #region Main Entry Point

        /// <summary>
        /// Process unit's real-time AI
        /// ★ v0.2.30: TeamBlackboard 통합
        /// ★ v0.2.32: 위상 분산 + 시간 예산 시스템
        /// </summary>
        public void ProcessUnit(UnitEntityData unit)
        {
            if (unit == null)
                return;

            // ★ v0.2.32: 시간 예산 체크 - 프레임당 처리 시간 제한
            if (!CheckAndUpdateTimeBudget())
            {
                // 시간 예산 초과 - 대기열에 추가
                if (unit.UniqueId != null && !_pendingProcessUnits.Contains(unit.UniqueId))
                {
                    _pendingProcessUnits.Enqueue(unit.UniqueId);
                }
                return;
            }

            // ★ v0.3.17: the unit lists and the team blackboard are read only by the old real-time engine
            // (SituationAnalyzer); the tactical engine never used them, yet they were rebuilt every frame.
            if (Settings.ModSettings.Instance?.RealTimeLegacyEngine ?? false)
            {
                // ★ v0.2.32: CombatDataCache 갱신 (프레임당 1회)
                CombatDataCache.Instance.RefreshIfNeeded();

                // ★ v0.2.30: TeamBlackboard 전투 상태 업데이트
                UpdateTeamBlackboardCombatState(unit);
            }

            // v0.2.2: 대기 중인 영구 버프 처리
            ProcessPendingBuffs();

            string unitName = unit.Descriptor?.CharacterName ?? "Unknown";
            string unitId = unit.UniqueId;
            float currentTime = Time.time;

            // ★ v0.2.32: 위상 분산 적용된 Decision throttling
            float phaseOffset = GetUnitPhaseOffset(unitId);
            float effectiveInterval = DecisionInterval + phaseOffset;

            if (_lastDecisionTime.TryGetValue(unitId, out float lastTime))
            {
                if (currentTime - lastTime < effectiveInterval)
                    return;
            }

            _processCount++;
            // ★ v0.3.14: no per-tick line (35 000 of a 110 000-line log); decisions and commands are logged anyway.

            // ★ v0.2.45: NextCommandTime 체크 추가 (움찔거림 방지)
            // 이전에 발행한 명령의 쿨다운이 끝나지 않았으면 새 결정 안함
            if (!IsNextCommandTimeReady(unit))
            {
                return;
            }

            // Check if can act
            // ★ v0.2.43: 실시간 전투에서는 핵심 조건만 체크
            // 기존 CanAct는 IsInExclusiveState(애니메이션)를 체크하는데, 이것이 stuck되면 AI 전체가 멈춤
            // 대신 치명적인 상태만 체크하고, 애니메이션 상태는 Commands.Empty 체크로 커버
            // ★ v0.2.52: 탑승된 마운트 체크를 최상단으로 이동 (stuck command 오탐 방지)
            // 마운트는 라이더가 제어하므로 자체 명령이 시작되지 않음 → "stuck" 오탐 발생
            // ★ v0.2.60: _lastDecisionTime 업데이트하여 매 프레임 호출 방지 (스터터링 수정)
            if (IsMountBeingRidden(unit))
            {
                _lastDecisionTime[unitId] = currentTime;
                return;
            }

            var state = unit.Descriptor?.State;
            if (state == null)
            {
                Main.Verbose($"[RT] {unitName}: Cannot act - State=null");
                _lastDecisionTime[unitId] = currentTime;  // ★ v0.2.54: Throttle even on early return
                return;
            }

            // ★ v0.2.54: 행동 불가 상태 체크를 하나의 블록으로 통합
            // 불가 상태에서도 _lastDecisionTime을 업데이트하여 매 프레임 체크 방지
            string cannotActReason = GetCannotActReason(state);
            if (cannotActReason != null)
            {
                Main.Verbose($"[RT] {unitName}: Cannot act - {cannotActReason}");
                _lastDecisionTime[unitId] = currentTime;  // Throttle even when can't act
                return;
            }
            // ★ v0.2.43: IsInExclusiveState는 체크하지 않음 - Commands.Empty 체크가 커버함

            // ★ v0.3.14: nauseated: the game still starts UnitMoveTo, nothing else (UnitCommandController.ShouldStartCommand).
            // It used to count as "cannot act" before the AoE escape below: Orvath stood 26 s in the Stinking Cloud that
            // nauseated him, his queued Gather Power waiting to start (docs/STATUS_AWARE_PLAN.md, F2). Now the escape runs,
            // commands that cannot start are dropped, and the tactical engine picks movement only (ActionRules).
            bool nauseated = state.HasCondition(Kingmaker.UnitLogic.UnitCondition.Nauseated);
            if (nauseated) DropUnstartableWhileNauseated(unit);

            // ★ v0.2.49: AoE 위험 지역 탈출 우선 체크
            // 이동 가능한 상태라면 AoE 위험 지역에서 탈출 시도
            // ★ v0.3.14 (test5): not again while it is already walking (an escape, a "Leave" or a withdraw): each new escape
            // replaced the last one, two metres into it (14 of 24 of Camellia's escapes ended that way).
            // ★ v0.3.18 (B8): the old engine only. The tactical engine weighs leaving against acting itself (its "Leave
            // area" option keeps a target in reach and prices attacks of opportunity); this escape ran whenever the summed
            // danger was 0.5 or more, before the engine was asked (41 escapes in two real-time logs). A command waiting to
            // start when the unit finds itself in a new area is dropped below, so the engine decides again.
            bool legacy = Settings.ModSettings.Instance?.RealTimeLegacyEngine ?? false;
            var walking = unit.Commands.Move as UnitMoveTo;
            if (legacy && state.CanMove && (walking == null || walking.IsFinished))
            {
                var aoeDanger = AoeDangerAnalyzer.AnalyzeUnit(unit);
                if (aoeDanger.ShouldEscape && aoeDanger.SuggestedEscapePosition.HasValue)
                {
                    // AoE 탈출이 필요하면 바로 실행
                    Main.Log($"[RT] {unitName}: PRIORITY - Escaping AoE danger zone (level={aoeDanger.TotalDangerLevel:F2})");
                    ExecuteMoveAction(unit, aoeDanger.SuggestedEscapePosition.Value, "Emergency AoE escape");
                    _lastDecisionTime[unitId] = currentTime;
                    return;
                }
            }

            // The old engine knows nothing of "movement only": it would queue commands that cannot start.
            if (nauseated && (Settings.ModSettings.Instance?.RealTimeLegacyEngine ?? false))
            {
                _lastDecisionTime[unitId] = currentTime;
                return;
            }

            // Check combat state
            if (!(unit.CombatState?.IsInCombat ?? false))
            {
                // v0.2.2: 전투 외 상황에서 영구 버프 자동 적용
                ApplyPermanentBuffsOutOfCombat(unit);
                Main.Verbose($"[RT] {unitName}: Not in combat, checked permanent buffs");
                return;
            }

            // ★ v0.2.47: 명령 상태 상세 체크 + Stuck 명령 감지/취소
            if (!unit.Commands.Empty)
            {
                // ★ v0.3.14 (test5): a command chosen at full health that has not started yet (still walking to its target,
                // waiting for a cooldown) is dropped when the unit has since lost a lot of HP, so the engine can heal or
                // withdraw now. Seelah went from 100 % to 9 % while a Smite Evil waited 5 s to start.
                if (Tactics.RealTimeTactics.ShouldReplanAfterDamage(unit, out string replanWhy) ||
                    (!legacy && Tactics.RealTimeTactics.ShouldReplanInArea(unit, out replanWhy)))
                {
                    Main.Log($"[RT] {unitName}: {replanWhy} - dropping the commands that have not started, deciding again");
                    unit.Commands.InterruptAll(c => c != null && !c.IsStarted && c.IsInterruptible && !c.Cutscene);
                    _lastDecisionTime[unitId] = currentTime;
                    return;
                }

                try
                {
                    var rawCommands = unit.Commands.Raw;
                    bool hasStuckCommand = false;
                    UnitCommand stuckCommand = null;
                    string stuckReason = null;
                    bool stuckOnTarget = false;

                    foreach (var cmd in rawCommands)
                    {
                        if (cmd == null || cmd.IsFinished) continue;

                        // ★ v0.2.119: Started=False alone is NOT "stuck". UnitCommandController.ShouldStartCommand
                        // starts UnitMoveTo / melee attacks only once IsUnitCloseEnough() is true, i.e. on arrival,
                        // and commands legitimately wait for a real-time action cooldown. Interrupting every
                        // not-started command cancelled all walks longer than one decision interval.
                        if (!cmd.IsStarted && IsCommandStuck(unit, cmd, out stuckReason, out stuckOnTarget))
                        {
                            hasStuckCommand = true;
                            stuckCommand = cmd;
                            break;
                        }
                    }

                    if (hasStuckCommand && stuckCommand != null)
                    {
                        // ★ v0.2.47: Stuck 명령 감지 - 강제 취소 및 블랙리스트
                        string cmdType = stuckCommand.GetType().Name;
                        Main.Log($"[RT] {unitName}: Stuck command ({cmdType}) - {stuckReason} - cancelling");

                        // UnitUseAbility인 경우 능력 정보 추출하여 블랙리스트
                        // ★ v0.3.14: only when the problem is reaching or seeing that target.
                        if (stuckOnTarget && stuckCommand is UnitUseAbility useAbilityCmd)
                        {
                            var ability = useAbilityCmd.Ability;
                            var target = useAbilityCmd.Target?.Unit;
                            if (ability != null && target != null && legacy)
                            {
                                FailedAbilityTracker.Instance.RecordFailure(unit, ability, target);
                                Main.Log($"[RT] {unitName}: Blacklisted stuck ability {ability.Name} -> {target.CharacterName}");
                            }
                        }
                        // ★ v0.3.18 (C3): the tactical engine learns from it too (the tracker above is the old engine's).
                        if (!legacy) Tactics.RealTimeTactics.RecordStuck(unit, stuckCommand, stuckOnTarget);

                        // 명령 강제 취소
                        try
                        {
                            unit.Commands.InterruptAll();
                            Main.Verbose($"[RT] {unitName}: Commands interrupted");
                        }
                        catch (Exception cancelEx)
                        {
                            Main.Verbose($"[RT] {unitName}: Failed to interrupt: {cancelEx.Message}");
                        }

                        return;
                    }

                    // 정상 진행 중인 명령 (Started=True)
                    // ★ v0.3.14: not logged every tick (33 000 lines); [Cmd] ISSUED / ENDED trace each command.
                }
                catch (Exception ex)
                {
                    Main.Verbose($"[RT] {unitName}: Commands check error: {ex.Message}");
                }
                return;
            }

            try
            {
                // ★ v0.3.14: the turn-based tactical engine decides real time too (Tactics/RealTimeTactics); the old
                // UnifiedDecisionEngine only when chosen in the settings.
                if (Settings.ModSettings.Instance?.RealTimeLegacyEngine ?? false)
                    ExecuteAIDecision(unit);
                else
                    Tactics.RealTimeTactics.Act(unit);
                _lastDecisionTime[unitId] = currentTime;
            }
            catch (Exception ex)
            {
                Main.Error($"[RT] ProcessUnit error ({unitName}): {ex.Message}\n{ex.StackTrace}");
            }
        }

        /// <summary>
        /// ★ v0.2.32: 유닛별 위상 오프셋 계산
        /// 유닛 ID 해시를 기반으로 0~MAX_PHASE_OFFSET 범위의 오프셋 생성
        /// 모든 유닛이 동시에 업데이트되는 것을 방지
        /// </summary>
        private float GetUnitPhaseOffset(string unitId)
        {
            if (string.IsNullOrEmpty(unitId))
                return 0f;

            // 캐시 확인
            if (_unitPhaseOffsets.TryGetValue(unitId, out float cachedOffset))
                return cachedOffset;

            // 해시 기반 오프셋 계산
            int hash = unitId.GetHashCode();
            int phaseIndex = Math.Abs(hash) % PHASE_DIVISIONS;
            float offset = (phaseIndex / (float)PHASE_DIVISIONS) * MAX_PHASE_OFFSET;

            // 캐시 저장
            _unitPhaseOffsets[unitId] = offset;

            return offset;
        }

        /// <summary>
        /// ★ v0.2.32: 시간 예산 확인 및 업데이트
        /// 새 프레임이면 스톱워치 리셋, 예산 초과면 false 반환
        /// </summary>
        private bool CheckAndUpdateTimeBudget()
        {
            int currentFrame = Time.frameCount;

            // 새 프레임이면 스톱워치 리셋 + 대기열 처리
            if (currentFrame != _lastBudgetFrame)
            {
                _frameBudget.Restart();
                _lastBudgetFrame = currentFrame;

                // 이전 프레임에서 처리 못한 유닛 먼저 처리
                ProcessPendingUnitsQueue();
            }

            // 시간 예산 확인
            return _frameBudget.ElapsedMilliseconds < MAX_AI_TIME_MS;
        }

        /// <summary>
        /// ★ v0.2.32: 대기열 처리 - 이전 프레임에서 처리 못한 유닛
        /// </summary>
        private void ProcessPendingUnitsQueue()
        {
            if (_pendingProcessUnits.Count == 0) return;

            int processed = 0;
            while (_pendingProcessUnits.Count > 0 && _frameBudget.ElapsedMilliseconds < MAX_AI_TIME_MS)
            {
                var unitId = _pendingProcessUnits.Dequeue();

                // 유닛 찾기
                var unit = Kingmaker.Game.Instance?.State?.Units?
                    .FirstOrDefault(u => u?.UniqueId == unitId);

                // ★ v0.3.12: through ProcessUnit, not straight to ExecuteAIDecision: the queue skipped the readiness gates
                // (NextCommandTime, a command still running, cannot act, ridden mount) and the control check, so a queued
                // unit could be given a new command that replaced the one it was carrying out. Same frame, so ProcessUnit
                // does not re-enter this queue; over budget it simply queues the unit again.
                if (unit != null && unit.HPLeft > 0 && (unit.CombatState?.IsInCombat ?? false) &&
                    CustomBrainPatch.ShouldControlUnit(unit))
                {
                    try
                    {
                        ProcessUnit(unit);
                        processed++;
                    }
                    catch (Exception ex)
                    {
                        Main.Verbose($"[RT] Pending queue process error: {ex.Message}");
                    }
                }
            }

            if (processed > 0)
            {
                Main.Verbose($"[RT] Processed {processed} pending units from queue");
            }
        }

        #endregion

        /// <summary>Seconds a not-started command may sit without the unit moving before it is treated as stuck.</summary>
        private const float STUCK_COMMAND_SECONDS = 3f;

        /// <summary>★ v0.3.14: when each pending command last had a reason to wait (see <see cref="IsCommandStuck"/>).</summary>
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<UnitCommand, WaitMark> LastWait =
            new System.Runtime.CompilerServices.ConditionalWeakTable<UnitCommand, WaitMark>();

        private sealed class WaitMark { public TimeSpan Time; }

        /// <summary>
        /// ★ v0.2.119: A not-started command is stuck only if the unit is not walking towards it, it is not waiting
        /// for a real-time action cooldown, and it has been pending for a while.
        /// ★ v0.3.14: "a while" counts from the last moment the command had a reason to wait, not from when it was
        /// issued, and the reasons are the game's own (UnitCommandController.ShouldStartCommand): moving, action
        /// cooldown, hands busy with an animation, an equipment update, waiting for initiative, unable to act,
        /// nauseated. 0.3.13 measured from the issue time, so on the frame a 6-second cooldown ran out, before the
        /// game started the command, it was "3 s old and not waiting": Aeiluvetar's every second attack, Orvath's
        /// cantrips and Seelah's Charge were cancelled that way and the abilities blacklisted (90 times in one log).
        /// <paramref name="reason"/> says what blocks a stuck command; <paramref name="targetProblem"/> is true when it
        /// is about reaching or seeing the target (the only case worth blacklisting the ability on that target).
        /// </summary>
        private static bool IsCommandStuck(UnitEntityData unit, UnitCommand cmd, out string reason, out bool targetProblem)
        {
            reason = null;
            targetProblem = false;
            try
            {
                var now = Kingmaker.Game.Instance.TimeController.GameTime;
                string wait = WaitReason(unit, cmd);
                // First seen counts as a wait: a command needs 3 s of observed nothing-to-wait-for to be stuck.
                var mark = LastWait.GetValue(cmd, _ => new WaitMark { Time = now });
                if (wait != null)
                {
                    mark.Time = now;
                    return false;
                }
                if ((now - mark.Time).TotalSeconds <= STUCK_COMMAND_SECONDS) return false;

                if (!cmd.CanStart)
                {
                    reason = "the game says it cannot start (e.g. cannot cast now)";
                    targetProblem = true;
                }
                else if (!cmd.IsUnitCloseEnough())
                {
                    float dist = Vector3.Distance(unit.Position, cmd.ApproachPoint);
                    bool sight = cmd.SightIsClear();
                    reason = $"not in range or sight and not moving (distance {dist:F1} m, range {cmd.ApproachRadius:F1} m" +
                             (cmd.NeedLoS ? $", line of sight {(sight ? "clear" : "blocked")}" : "") + ")";
                    targetProblem = true;
                }
                else
                {
                    reason = "in range, nothing to wait for, but not started";
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>★ v0.3.14: why the game is legitimately holding a command back, or null.</summary>
        private static string WaitReason(UnitEntityData unit, UnitCommand cmd)
        {
            if (unit.View != null && unit.View.IsMoving()) return "moving";
            if (cmd.AwaitMovementFinish && (unit.View?.MovementAgent?.IsReallyMoving ?? false)) return "finishing a move";
            if (unit.AreHandsBusyWithAnimation && !cmd.DontWaitForHands) return "hands busy";
            var state = unit.Descriptor?.State;
            if (state == null || !state.CanAct) return "cannot act";
            if (state.HasCondition(Kingmaker.UnitLogic.UnitCondition.Nauseated)) return "nauseated";
            if (Kingmaker.Game.Instance.HandsEquipmentController?.IsUpdateScheduledFor(unit) ?? false) return "equipment update";
            var combat = unit.CombatState;
            if (combat != null && unit.IsInCombat && !cmd.IsIgnoreCooldown)
            {
                if (!combat.CanActInCombat) return "waiting for initiative";
                if (combat.HasCooldownForCommand(cmd)) return "action cooldown";
            }
            return null;
        }

        /// <summary>
        /// ★ v0.3.14: commands a nauseated unit cannot start (anything but a walk) are dropped while it is nauseated, so
        /// it can walk instead (out of the cloud, out of melee). Started commands and walks are left alone; the engine
        /// decides again when the nausea ends.
        /// </summary>
        private static void DropUnstartableWhileNauseated(UnitEntityData unit)
        {
            try
            {
                if (unit.Commands.Empty || !unit.IsInCombat) return;
                var waiting = unit.Commands.Raw.Where(c => c != null && !c.IsFinished).ToList();
                if (waiting.Count == 0 || waiting.Any(c => c.IsStarted || c is UnitMoveTo)) return;
                if (!waiting.All(c => c.IsInterruptible && !c.Cutscene)) return;
                Main.Log($"[RT] {unit.CharacterName}: nauseated - dropping {string.Join(", ", waiting.Select(c => c.GetType().Name))} " +
                         "(cannot start while nauseated; only walking can)");
                unit.Commands.InterruptAll();
            }
            catch (Exception ex)
            {
                Main.Verbose($"[RT] {unit.CharacterName}: nauseated command check: {ex.Message}");
            }
        }

        /// <summary>
        /// ★ v0.2.54: 행동 불가 상태 체크 (통합)
        /// </summary>
        /// <returns>불가 사유 문자열, 행동 가능시 null</returns>
        private string GetCannotActReason(Kingmaker.UnitLogic.UnitState state)
        {
            if (state.IsDead) return "Dead";
            if (!state.IsConscious) return "NotConscious";
            if (state.IsUnconscious) return "Unconscious";
            if (state.HasCondition(Kingmaker.UnitLogic.UnitCondition.Paralyzed)) return "Paralyzed";
            if (state.HasCondition(Kingmaker.UnitLogic.UnitCondition.Petrified)) return "Petrified";
            if (state.HasCondition(Kingmaker.UnitLogic.UnitCondition.Stunned)) return "Stunned";
            if (state.HasCondition(Kingmaker.UnitLogic.UnitCondition.CantAct)) return "CantAct";
            if (state.HasCondition(Kingmaker.UnitLogic.UnitCondition.Dazed)) return "Dazed";
            // ★ v0.3.14: not Nauseated - a nauseated unit can still walk (handled in ProcessUnit).
            // ★ v0.3.18 (B9, from the game code): Dazed, Stunned and lying prone make UnitState.IsAble false, so the game
            // starts nothing (CanAct, CanMove); in real time UnitProneController stands a prone unit up by itself after
            // 3 s. A frightened unit that sees an enemy panics (UnitFearController): the game takes it out of direct
            // control and runs it away, and the controller never reaches this unit (CustomBrainPatch.ShouldControlUnit).
            // Frightened without panic (no enemy in sight) may act, and was refused here. The Cowering condition blocks
            // nothing in the game's code by itself; only when the game says the unit cannot act.
            if (state.HasCondition(Kingmaker.UnitLogic.UnitCondition.Cowering) && !state.CanAct) return "Cowering";
            if (state.IsPanicked) return "Panicked";
            if (state.Prone?.Active == true) return "Prone";
            return null;  // Can act
        }

        #region AI Decision

        /// <summary>
        /// Execute AI decision using Unified Decision Engine
        /// ★ v0.2.22: 역할별 로직 대신 통합 Utility Scoring 시스템 사용
        /// </summary>
        private void ExecuteAIDecision(UnitEntityData unit)
        {
            string unitName = unit.Descriptor?.CharacterName ?? "Unknown";

            try
            {
                // ★ v0.2.22: UnifiedDecisionEngine으로 최적 행동 결정
                var bestAction = UnifiedDecisionEngine.Instance.DecideAction(unit, (TurnState)null);

                if (bestAction == null || bestAction.ActionType == CandidateType.EndTurn)
                {
                    Main.Verbose($"[RT] {unitName}: No action (EndTurn)");
                    return;
                }

                // 행동 실행
                ExecuteActionCandidate(unit, bestAction);
            }
            catch (Exception ex)
            {
                Main.Error($"[RT] {unitName}: Decision error - {ex.Message}");
            }
        }

        /// <summary>
        /// ★ v0.2.22: ActionCandidate 실행
        /// </summary>
        private void ExecuteActionCandidate(UnitEntityData unit, ActionCandidate action)
        {
            string unitName = unit.Descriptor?.CharacterName ?? "Unknown";

            try
            {
                switch (action.ActionType)
                {
                    case CandidateType.AbilityAttack:
                        ExecuteAbilityAction(unit, action);
                        _attackCount++;
                        break;

                    case CandidateType.BasicAttack:
                        ExecuteBasicAttack(unit, action.Target);
                        _attackCount++;
                        break;

                    case CandidateType.Buff:
                        ExecuteAbilityAction(unit, action);
                        _buffCount++;
                        break;

                    case CandidateType.Heal:
                        ExecuteAbilityAction(unit, action);
                        _healCount++;
                        break;

                    case CandidateType.Debuff:
                        ExecuteAbilityAction(unit, action);
                        _debuffCount++;
                        break;

                    case CandidateType.Move:
                        if (action.MoveDestination.HasValue)
                        {
                            ExecuteMoveAction(unit, action.MoveDestination.Value, action.Reason);
                            _moveCount++;
                        }
                        break;

                    case CandidateType.EndTurn:
                    default:
                        // 아무 것도 안함
                        break;
                }
            }
            catch (Exception ex)
            {
                Main.Error($"[RT] {unitName}: Execute error - {ex.Message}");
            }
        }

        /// <summary>
        /// ★ v0.2.22: 능력 실행
        /// ★ v0.2.48: 반복 명령 감지 추가
        /// </summary>
        private void ExecuteAbilityAction(UnitEntityData unit, ActionCandidate action)
        {
            if (action.Ability == null || action.Target == null)
            {
                Main.Verbose($"[RT] Ability action missing ability or target");
                return;
            }

            string unitName = unit.Descriptor?.CharacterName ?? "Unknown";
            string targetName = action.Target.Descriptor?.CharacterName ?? "Unknown";

            // ★ v0.2.48: 반복 명령 감지 - 같은 능력+타겟이 짧은 시간 내 반복되면 블랙리스트
            if (FailedAbilityTracker.Instance.RecordCommandAndCheckRepeat(unit, action.Ability, action.Target))
            {
                Main.Log($"[RT] {unitName}: Repeat detected for {action.Ability.Name} -> {targetName}, skipping");
                return;
            }

            // ★ v0.3.14: the unit or, for a point-cast spell, its spot (same rule as candidate generation).
            var targetWrapper = Tactics.AbilityProfile.TargetOn(action.Ability, action.Target);
            if (targetWrapper == null)
            {
                Main.Verbose($"[RT] {unitName}: Cannot target {targetName} with {action.Ability.Name}");
                return;
            }

            try
            {
                var command = new UnitUseAbility(action.Ability, targetWrapper);
                CommandDispatcher.Run(unit, command, "RT ExecuteAbilityAction");

                // ★ v0.2.46: 명령이 실제로 큐에 들어갔는지 확인
                // 명령이 바로 취소되면 Commands.Empty가 true 상태 유지
                if (unit.Commands.Empty)
                {
                    // 명령이 큐에 들어가지 않음 - 이 능력+타겟 조합 블랙리스트
                    FailedAbilityTracker.Instance.RecordFailure(unit, action.Ability, action.Target);
                    Main.Verbose($"[RT] {unitName}: Command for {action.Ability.Name} -> {targetName} failed to queue (blacklisted)");
                    return;
                }

                SetNextCommandTime(unit, command);
                RecordAbilityUse(unit, action.Ability);

                // ★ v0.2.23: Register pending buff to prevent duplicate casting
                if (action.ActionType == CandidateType.Buff)
                {
                    PendingActionTracker.Instance.RegisterPendingBuff(action.Ability, action.Target, unit);
                }

                Main.Log($"[RT] ★ {unitName} -> {targetName}: {action.ActionType} ({action.Ability.Name}) Score={action.HybridFinalScore:F1}");   // ★ v0.3.14: the score it was chosen by
            }
            catch (Exception ex)
            {
                Main.Error($"[RT] Ability execution failed: {ex.Message}");
            }
        }

        /// <summary>
        /// ★ v0.2.22: 기본 공격 실행
        /// </summary>
        private void ExecuteBasicAttack(UnitEntityData unit, UnitEntityData target)
        {
            if (target == null) return;

            string unitName = unit.Descriptor?.CharacterName ?? "Unknown";
            string targetName = target.Descriptor?.CharacterName ?? "Unknown";

            try
            {
                var command = new UnitAttack(target);
                CommandDispatcher.Run(unit, command, "RT ExecuteBasicAttack");
                SetNextCommandTime(unit, command);

                Main.Log($"[RT] ★ {unitName} -> {targetName}: BasicAttack");
            }
            catch (Exception ex)
            {
                Main.Error($"[RT] Basic attack failed: {ex.Message}");
            }
        }

        /// <summary>
        /// ★ v0.2.30: 이동 실행 - BattlefieldGrid 위치 검증 추가
        /// ★ v0.2.53: 함정 회피 통합
        /// </summary>
        private void ExecuteMoveAction(UnitEntityData unit, Vector3 destination, string reason)
        {
            string unitName = unit.Descriptor?.CharacterName ?? "Unknown";

            try
            {
                // ★ v0.2.53: 함정 회피 체크
                Vector3 safeDestination = destination;
                if (TrapAwarenessController.Enabled &&
                    !TrapAwarenessController.CheckMovementSafety(unit, destination, out safeDestination))
                {
                    // 우회 경로로 목적지 변경됨
                    Main.Log($"[RT] {unitName}: Trap avoidance - redirecting to safe position");
                    destination = safeDestination;
                }

                // ★ v0.2.30: BattlefieldGrid로 목표 위치 사전 검증
                if (!BattlefieldGrid.Instance.ValidateTargetPosition(unit, destination))
                {
                    Main.Log($"[RT] {unitName}: Move target invalid ({destination.x:F1},{destination.z:F1}) - not walkable or occupied");
                    return;
                }

                // ★ v0.2.119: default ApproachRadius (0.3m); 0f never satisfies IsUnitCloseEnough(), so the move never completed
                var command = new UnitMoveTo(destination);
                CommandDispatcher.Run(unit, command, "RT ExecuteMoveAction");
                SetNextCommandTime(unit, command);

                Main.Log($"[RT] -> {unitName}: Move ({reason})");
            }
            catch (Exception ex)
            {
                Main.Error($"[RT] Move failed: {ex.Message}");
            }
        }

        #endregion

        // ★ v0.3.17: the role-based logic of v0.2 (DPS/Tank/Support routines, target selection, ability and movement
        // helpers only they called) was removed: nothing called it since the tactical engine (0.3.0).

        #region Helper Methods

        /// <summary>
        /// Get HP percent for unit
        /// </summary>
        private float GetHPPercent(UnitEntityData unit)
        {
            try
            {
                if (unit?.Stats?.HitPoints == null) return 100f;
                float current = unit.HPLeft;  // ★ v0.3.14: current HP (was max HP after bonuses: never changed with damage)
                float max = unit.MaxHP;
                if (max <= 0) return 100f;
                return (current / max) * 100f;
            }
            catch { return 100f; }
        }

        /// <summary>
        /// v0.2.21: 실시간 전투용 명령 체크 - 능력 시전만 블록
        /// 기본 공격(UnitAttack)과 이동(UnitMoveTo)은 블록하지 않음 (게임이 관리)
        /// 능력 시전(UnitUseAbility)만 완료까지 대기
        /// </summary>
        private bool HasNonInterruptibleCommand(UnitEntityData unit)
        {
            if (unit?.Commands?.Raw == null) return false;

            string unitName = unit.Descriptor?.CharacterName ?? "Unknown";

            foreach (var cmd in unit.Commands.Raw)
            {
                if (cmd == null || cmd.IsFinished) continue;

                // v0.2.21: 능력 시전(UnitUseAbility)만 블록
                // 기본 공격(UnitAttack)과 이동(UnitMoveTo)은 중단 가능 - 새 명령으로 대체됨
                if (cmd is UnitUseAbility useAbility)
                {
                    // 시작했고 메인 액션(Acted) 완료 전이면 대기
                    if (useAbility.IsStarted && !useAbility.IsActed)
                    {
                        Main.Verbose($"[RT] {unitName}: Blocked by UnitUseAbility " +
                            $"(Started={useAbility.IsStarted}, Acted={useAbility.IsActed}, " +
                            $"Ability={useAbility.Ability?.Name ?? "null"})");
                        return true;
                    }
                }

                // UnitAttack, UnitMoveTo 등은 블록하지 않음
                // 새 명령을 내리면 게임이 자동으로 이전 명령을 중단하고 교체함
            }

            return false;
        }

        /// <summary>
        /// v0.2.19: 게임의 NextCommandTime 체크 (AiBrainController.TickBrain 참조)
        /// AI 명령 스팸 방지용 내부 쿨다운
        /// v0.2.20: 소환수/펫 - AIData 없으면 허용 (자체 쿨다운 사용)
        /// </summary>
        private bool IsNextCommandTimeReady(UnitEntityData unit)
        {
            try
            {
                // 게임 로직: unit.CombatState.AIData.NextCommandTime >= Time.time 이면 대기
                var aiData = unit.CombatState?.AIData;

                // v0.2.20: 소환수/펫은 AIData가 없을 수 있음
                if (aiData == null)
                {
                    Main.Verbose($"[RT] {unit.Descriptor?.CharacterName ?? "Unknown"}: AIData is null, allowing command");
                    return true;
                }

                var nextTime = aiData.NextCommandTime;
                bool ready = nextTime < Time.time;

                if (!ready)
                {
                    Main.Verbose($"[RT] {unit.Descriptor?.CharacterName ?? "Unknown"}: NextCommandTime={nextTime:F2}, Current={Time.time:F2}, wait {nextTime - Time.time:F2}s");
                }

                return ready;
            }
            catch { return true; }
        }

        /// <summary>
        /// Clear cache
        /// ★ v0.2.32: 위상 오프셋, 대기열, CombatDataCache도 초기화
        /// ★ v0.2.65: MovementPlanner 이동 기록 초기화 추가
        /// </summary>
        public void ClearCache()
        {
            _lastDecisionTime.Clear();
            _abilityCooldowns.Clear();

            // ★ v0.2.32: 위상 오프셋과 대기열 초기화
            _unitPhaseOffsets.Clear();
            _pendingProcessUnits.Clear();

            // ★ v0.2.32: CombatDataCache 초기화
            CombatDataCache.Instance.Clear();

            // ★ v0.2.30: TeamBlackboard도 초기화
            TeamBlackboard.Instance.Clear();

            // ★ v0.2.65: 이동 루프 감지 기록 초기화
            Planning.MovementPlanner.ClearMoveHistory();
        }

        /// <summary>
        /// ★ v0.2.30: TeamBlackboard 전투 상태 업데이트
        /// 전투 시작 감지 및 초기화, 라운드 변경 감지
        /// </summary>
        private void UpdateTeamBlackboardCombatState(UnitEntityData unit)
        {
            try
            {
                bool unitInCombat = unit.CombatState?.IsInCombat ?? false;

                if (unitInCombat)
                {
                    // 전투 중인데 TeamBlackboard가 비활성 → 초기화
                    if (!TeamBlackboard.Instance.IsCombatActive)
                    {
                        TeamBlackboard.Instance.InitializeCombat();
                        Main.Log("[RT] TeamBlackboard initialized for combat");
                    }

                    // 팀 상태 주기적 업데이트 (모든 유닛 처리 시마다)
                    TeamBlackboard.Instance.UpdateTeamAssessment();
                }
                else
                {
                    // 전투 종료 감지 - 아무도 전투 중이지 않으면 정리
                    if (TeamBlackboard.Instance.IsCombatActive && TeamBlackboard.Instance.RegisteredUnitCount == 0)
                    {
                        // 모든 유닛이 전투 해제됐으면 정리
                        bool anyInCombat = false;
                        foreach (var u in Kingmaker.Game.Instance.State.Units)
                        {
                            if (u?.IsPlayerFaction == true && (u.CombatState?.IsInCombat ?? false))
                            {
                                anyInCombat = true;
                                break;
                            }
                        }

                        if (!anyInCombat)
                        {
                            TeamBlackboard.Instance.Clear();
                            Planning.MovementPlanner.ClearMoveHistory();  // ★ v0.2.65
                            Main.Log("[RT] TeamBlackboard cleared (combat ended)");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Main.Verbose($"[RT] UpdateTeamBlackboardCombatState error: {ex.Message}");
            }
        }

        /// <summary>
        /// v0.2.21: 명령 실행 후 NextCommandTime 설정
        /// 게임의 AiBrainController.TickBrain과 동일한 로직
        /// 이렇게 해야 게임 AI와 우리 AI가 같은 쿨다운 시스템을 공유함
        /// ★ v0.2.28: Free/Swift action에도 최소 딜레이 추가 (무한 루프 방지)
        /// </summary>
        private void SetNextCommandTime(UnitEntityData unit, UnitCommand command)
        {
            try
            {
                var aiData = unit?.CombatState?.AIData;
                if (aiData == null) return;

                // 게임 로직: Time.time + 명령 타입별 딜레이
                aiData.NextCommandTime = Time.time;

                if (command != null)
                {
                    if (command.Type != UnitCommand.CommandType.Free &&
                        command.Type != UnitCommand.CommandType.Swift)
                    {
                        // Standard/Move: 0.3~0.4초 딜레이
                        aiData.NextCommandTime += 0.3f + UnityEngine.Random.Range(0f, 0.1f);
                    }
                    else
                    {
                        // ★ v0.2.28: Free/Swift: 최소 0.15초 딜레이 (무한 루프 방지)
                        // 이전에는 딜레이 없어서 Free action 후 무한 호출됨
                        aiData.NextCommandTime += 0.15f;
                    }
                }

                Main.Verbose($"[RT] {unit.Descriptor?.CharacterName ?? "Unknown"}: NextCommandTime set to {aiData.NextCommandTime:F2} (cmdType={command?.Type})");
            }
            catch (Exception ex)
            {
                Main.Verbose($"[RT] SetNextCommandTime error: {ex.Message}");
            }
        }

        /// <summary>
        /// ★ v0.2.41: 이 유닛이 누군가의 마운트인지 확인
        /// 마운트가 탑승된 상태면 라이더가 명령을 내리므로 마운트 자체는 AI 처리 불필요
        /// </summary>
        private bool IsMountBeingRidden(UnitEntityData unit)
        {
            try
            {
                // 모든 아군 유닛을 순회하며 이 유닛을 타고 있는지 확인
                foreach (var other in Kingmaker.Game.Instance.State.Units)
                {
                    if (other == null || other == unit) continue;

                    var riderPart = other.Get<Kingmaker.UnitLogic.Parts.UnitPartRider>();
                    if (riderPart != null && riderPart.SaddledUnit == unit)
                    {
                        // other가 unit을 타고 있음
                        return true;
                    }
                }
            }
            catch { }
            return false;
        }

        #endregion

        #region v0.2.1 Ability Cooldown System

        /// <summary>
        /// Check if ability is on cooldown
        /// </summary>
        private bool IsAbilityOnCooldown(UnitEntityData unit, AbilityData ability)
        {
            if (ability?.Blueprint == null) return false;

            string key = $"{unit.UniqueId}:{ability.Blueprint.AssetGuid}";

            if (!_abilityCooldowns.TryGetValue(key, out float lastUse))
                return false;

            float elapsed = Time.time - lastUse;
            float cooldown = GetAbilityCooldown(ability);

            return elapsed < cooldown;
        }

        /// <summary>
        /// Record ability usage for cooldown tracking
        /// </summary>
        private void RecordAbilityUse(UnitEntityData unit, AbilityData ability)
        {
            if (ability?.Blueprint == null) return;

            string key = $"{unit.UniqueId}:{ability.Blueprint.AssetGuid}";
            _abilityCooldowns[key] = Time.time;
        }

        /// <summary>
        /// Get cooldown time for ability
        /// v0.2.6: Demoralize 제거됨 (완전 비활성화)
        /// </summary>
        private float GetAbilityCooldown(AbilityData ability)
        {
            if (ability?.Blueprint == null) return 0f;

            // 무료 능력은 기본 쿨다운 적용
            var classification = AbilityClassifier.Classify(ability, null);
            if (classification.IsFreeToUse)
            {
                return FREE_ABILITY_COOLDOWN;
            }

            // 그 외 능력은 쿨다운 없음 (게임 자체 쿨다운 사용)
            return 0f;
        }

        /// <summary>
        /// Check if ability should be skipped (on cooldown or spam protection)
        /// v0.2.9: 타입 기반 Demoralize 블랙리스트 (스트링 매칭 금지)
        /// v0.2.11: 게임 액션 이코노미 쿨다운 체크 추가
        /// v0.2.19: 실시간 전투에서는 액션 이코노미 체크 제거 (턴제 전용)
        /// </summary>
        private bool ShouldSkipAbility(UnitEntityData unit, AbilityData ability)
        {
            if (ability?.Blueprint == null) return true;

            // v0.2.19: 실시간 전투에서는 HasCooldownForCommand 체크 제거
            // 이 체크는 턴제 전투의 Standard/Move/Swift 액션 이코노미용
            // 실시간에서는 게임 자체 글로벌 쿨다운 시스템이 능력 스팸을 방지함
            // 이전 v0.2.11: 이 체크가 실시간에서 모든 능력을 블록하는 버그 유발

            // v0.2.9: Demoralize 완전 비활성화 - 타입 기반 체크
            try
            {
                var runAction = ability.Blueprint.GetComponent<AbilityEffectRunAction>();
                if (runAction?.Actions?.Actions != null)
                {
                    foreach (var action in runAction.Actions.Actions)
                    {
                        if (action is Demoralize)
                        {
                            Main.Verbose($"[RT] BLOCKED: {ability.Name} (contains Demoralize action)");
                            return true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Main.Verbose($"[RT] ShouldSkipAbility type check error: {ex.Message}");
            }

            // Custom cooldown check (for free abilities)
            if (IsAbilityOnCooldown(unit, ability))
            {
                Main.Verbose($"[RT] Skipping {ability.Name}: on custom cooldown");
                return true;
            }

            return false;
        }

        #endregion

        #region Out-of-Combat Buffs

        /// <summary>
        /// Queue a unit for pending buff application
        /// v0.2.2: 명령 실행 중인 유닛은 대기열에 추가
        /// </summary>
        public void QueuePendingBuff(UnitEntityData unit)
        {
            if (unit == null) return;
            string unitId = unit.UniqueId;
            if (!_pendingBuffUnits.Contains(unitId))
            {
                _pendingBuffUnits.Add(unitId);
                Main.Log($"[RT] {unit.Descriptor?.CharacterName}: Queued for pending buff");
            }
        }

        /// <summary>
        /// Process pending buff queue (called from ProcessUnit)
        /// </summary>
        public void ProcessPendingBuffs()
        {
            if (_pendingBuffUnits.Count == 0) return;

            float currentTime = UnityEngine.Time.time;
            var unitsToRemove = new List<string>();

            foreach (var unitId in _pendingBuffUnits.ToList())
            {
                // 재시도 간격 체크
                if (_pendingBuffLastCheck.TryGetValue(unitId, out float lastCheck))
                {
                    if (currentTime - lastCheck < PENDING_BUFF_RETRY_INTERVAL)
                        continue;
                }
                _pendingBuffLastCheck[unitId] = currentTime;

                // 유닛 찾기
                var unit = Kingmaker.Game.Instance.State.Units
                    .FirstOrDefault(u => u.UniqueId == unitId);

                if (unit == null || unit.HPLeft <= 0)
                {
                    unitsToRemove.Add(unitId);
                    continue;
                }

                // 아직 명령 실행 중이면 대기
                if (!unit.Commands.Empty)
                    continue;

                // 전투 중이면 대기열에서 제거
                if (unit.CombatState?.IsInCombat ?? false)
                {
                    unitsToRemove.Add(unitId);
                    continue;
                }

                // 버프 적용 시도
                ApplyPermanentBuffsOutOfCombat(unit);
                unitsToRemove.Add(unitId);
            }

            foreach (var unitId in unitsToRemove)
            {
                _pendingBuffUnits.Remove(unitId);
                _pendingBuffLastCheck.Remove(unitId);
            }
        }

        /// <summary>
        /// Apply permanent buffs out of combat
        /// v0.2.2: 로깅 개선 + 대기열 지원
        /// </summary>
        public void ApplyPermanentBuffsOutOfCombat(UnitEntityData unit, bool queueOnBusy = false)
        {
            if (unit == null)
                return;
            // ★ v0.3.17: only units the AI controls (main character / companions / per-character AI settings); every
            // party member was buffed after combat, whatever the settings said.
            if (!CustomBrainPatch.ShouldControlUnit(unit))
                return;

            string unitName = unit.Descriptor?.CharacterName ?? "Unknown";
            string unitId = unit.UniqueId;

            if (!(unit.Descriptor?.State?.CanAct ?? false))
            {
                Main.Verbose($"[RT] {unitName}: Cannot act, skipping permanent buffs");
                return;
            }

            if (!unit.Commands.Empty)
            {
                Main.Verbose($"[RT] {unitName}: Commands in progress, skipping permanent buffs");
                if (queueOnBusy)
                {
                    QueuePendingBuff(unit);
                }
                return;
            }

            // 대기열에서 제거 (성공적으로 처리됨)
            _pendingBuffUnits.Remove(unitId);

            // ★ v0.3.3: never flip an on/off switch outside combat. Isekai's "Shift Form: Overlord / Mortal Guise" applies
            // its form buff when absent and REMOVES it when present; recast after every fight, it left Orvath's form to
            // chance. Such toggles are the player's choice out of combat (the turn-based engine still uses them in combat).
            var unappliedPermanentBuffs = AbilityClassifier.GetUnappliedPermanentBuffs(unit)
                .Where(c => c.Ability != null && IsWorthKeepingUp(c.Ability, unit))
                .ToList();
            Main.Verbose($"[RT] {unitName}: Found {unappliedPermanentBuffs.Count} unapplied permanent buffs");

            if (unappliedPermanentBuffs.Count == 0)
                return;

            var classification = unappliedPermanentBuffs.First();
            var nativeAbility = classification.Ability;

            if (nativeAbility == null)
            {
                Main.Verbose($"[RT] {unitName}: Permanent buff ability is null");
                return;
            }

            if (!nativeAbility.IsAvailableForCast)
            {
                Main.Verbose($"[RT] {unitName}: Permanent buff ({nativeAbility.Name}) not available for cast");
                return;
            }

            var target = new TargetWrapper(unit);
            if (!nativeAbility.CanTarget(target))
            {
                Main.Verbose($"[RT] {unitName}: Permanent buff ({nativeAbility.Name}) cannot target self");
                return;
            }

            try
            {
                var useAbilityCommand = new UnitUseAbility(nativeAbility, target);
                CommandDispatcher.Run(unit, useAbilityCommand, "RT ApplyPermanentBuffsOutOfCombat");
                SetNextCommandTime(unit, useAbilityCommand);  // v0.2.21
                Main.Log($"[RT] ★ {unitName}: Out-of-combat permanent buff ({nativeAbility.Name})");
                _buffCount++;
            }
            catch (Exception ex)
            {
                Main.Error($"[RT] Out-of-combat buff failed ({nativeAbility.Name}): {ex.Message}");
            }
        }

        /// <summary>
        /// ★ v0.3.5: what the out-of-combat routine may cast, judged by what the ability actually does (Tactics.AbilityProfile)
        /// rather than by the classifier's "permanent buff" label. After 0.3.3 hid the Shift Form toggle, the label let
        /// through Soul Power (spends a daily use and corpses), Perfect Strike (a daily-use, next-attack buff) and an
        /// Inflict Light Wounds item cast on Camellia herself. Allowed: no item, no toggle, no damage/heal/burn effect, and a
        /// buff on the caster lasting hours or permanently, with a recognised combat effect, not already active.
        /// </summary>
        private static bool IsWorthKeepingUp(Kingmaker.UnitLogic.Abilities.AbilityData ability, UnitEntityData unit)
        {
            try
            {
                if (ability.SourceItem != null) return false;
                var profile = Tactics.AbilityProfile.Get(ability);
                if (profile == null || profile.IsToggle || profile.IsOneShot || profile.HealsBurn) return false;
                if (profile.DealsDamage || profile.Heals.Count > 0 || profile.Summons || profile.HarmfulEffects) return false;   // ★ v0.3.12: HarmfulEffects

                foreach (var buff in profile.CasterBuffs.Concat(profile.TargetBuffs).Distinct())
                {
                    if (!profile.IsPersistent(buff)) continue;
                    if (unit.Buffs.HasFact(buff)) continue;
                    if (profile.BuffAppliesTo(buff, ability, unit) == Tactics.EffectApplies.No) continue;
                    var analysis = BuffEffectAnalyzer.Analyze(buff);
                    if (analysis.Recognized && analysis.CombatValue >= 0.5f) return true;
                }
            }
            catch (Exception ex)
            {
                Main.Verbose($"[RT] IsWorthKeepingUp {ability?.Name}: {ex.Message}");
            }
            return false;
        }

        /// <summary>
        /// Apply permanent buffs to entire party
        /// </summary>
        public void ApplyPermanentBuffsToParty()
        {
            try
            {
                foreach (var unit in Kingmaker.Game.Instance.State.Units)
                {
                    if (unit == null) continue;
                    if (!unit.IsPlayerFaction) continue;
                    if (unit.HPLeft <= 0) continue;
                    if (unit.CombatState?.IsInCombat ?? false) continue;

                    ApplyPermanentBuffsOutOfCombat(unit);
                }
            }
            catch (Exception ex)
            {
                Main.Error($"[RT] ApplyPermanentBuffsToParty error: {ex.Message}");
            }
        }

        #endregion
    }
}
