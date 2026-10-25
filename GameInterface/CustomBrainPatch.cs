using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Kingmaker;
using Kingmaker.AI;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.PubSubSystem;
using TurnBased.Controllers;
using CompanionAI_Pathfinder.Abstraction;
using CompanionAI_Pathfinder.Core;
using CompanionAI_Pathfinder.Core.TurnBased;
using CompanionAI_Pathfinder.Core.DecisionEngine;
using CompanionAI_Pathfinder.Settings;

namespace CompanionAI_Pathfinder.GameInterface
{
    /// <summary>
    /// AiBrainController에 대한 Harmony 패치
    /// AI 결정을 가로채서 커스텀 AI 로직 실행
    ///
    /// ★ v0.2.113: 아키텍처 개선
    /// - 턴제: TurnControllerTick_Postfix만 사용 → TurnBasedController
    /// - 실시간: TickBrain_Prefix → RealTimeController
    /// - TickBrain_Prefix는 턴제에서 호출 안 됨 (게임 로직)
    /// </summary>
    public static class CustomBrainPatch
    {
        private static int _tickCounter = 0;
        private static int _lastPreBuffFrame = 0;

        /// <summary>
        /// AiBrainController.TickBrain() 패치
        /// ★ v0.2.113: 실시간 전투 전용
        ///
        /// 게임 분석 결과:
        /// - 턴제 전투: AiBrainController.Tick()에서 바로 return (ForceTick 안 함)
        /// - 실시간 전투: Tick() → ForceTick() → TickBrain()
        ///
        /// 따라서 턴제에서는 이 메서드가 호출되지 않음!
        /// 턴제 AI는 TurnControllerTick_Postfix에서 처리
        /// </summary>
        public static bool TickBrain_Prefix(UnitEntityData unit, out long __state)
        {
            __state = 0; // ★ v0.3.22: time only a native allied summon brain that this prefix allows to run
            try
            {
                Main.TickCount++;
                _tickCounter++;

                // ★ v0.2.38: PreBuffController tick (프레임당 1회)
                int currentFrame = UnityEngine.Time.frameCount;
                if (currentFrame != _lastPreBuffFrame)
                {
                    _lastPreBuffFrame = currentFrame;
                    PreBuffController.Instance?.Tick();
                }

                // 모드가 비활성화되어 있으면 원본 실행
                if (!Main.Enabled)
                    return true;

                string unitName = unit?.Descriptor?.CharacterName ?? "Unknown";

                // 아군만 제어
                if (!ShouldControlUnit(unit))
                {
                    __state = CombatPacingDiagnostics.NativeSummonStart(unit);
                    return true;
                }

                Main.ProcessedUnits++;

                // ★ v0.2.113: 턴제/실시간 분기
                if (CombatController.IsInTurnBasedCombat())
                {
                    // ★ 턴제 모드에서는 TurnControllerTick_Postfix가 처리
                    // 이 코드는 실제로 호출되지 않아야 함 (게임이 턴제에서 TickBrain 안 함)
                    // 혹시 호출되면 무시
                    Main.Verbose($"[TickBrain] {unitName}: 턴제 모드 - TurnControllerTick에서 처리");
                    return false;
                }
                else
                {
                    // ★ v0.2.119: respect the "Enable in Real-Time Mode" setting (native AI runs instead)
                    if (!(ModSettings.Instance?.EnableInRealTime ?? true))
                        return true;

                    // ★ 실시간 모드 - 게임 AI 완전 대체
                    float nextTime = unit.CombatState?.AIData?.NextCommandTime ?? 0f;
                    if (nextTime >= UnityEngine.Time.time)
                    {
                        return false;  // 명령 쿨다운 중
                    }

                    // 능력 시전 중 체크
                    bool hasAbilityInProgress = false;
                    if (unit.Commands?.Raw != null)
                    {
                        foreach (var cmd in unit.Commands.Raw)
                        {
                            if (cmd == null || cmd.IsFinished) continue;
                            if (cmd is Kingmaker.UnitLogic.Commands.UnitUseAbility useAbility)
                            {
                                if (useAbility.IsStarted && !useAbility.IsActed)
                                {
                                    hasAbilityInProgress = true;
                                    break;
                                }
                            }
                        }
                    }

                    if (hasAbilityInProgress)
                    {
                        return false;  // 능력 시전 완료 대기
                    }

                    // RealTimeController에서 AI 결정 수행
                    RealTimeController.Instance.ProcessUnit(unit);
                    return false;
                }
            }
            catch (Exception ex)
            {
                Main.Error($"TickBrain 패치 오류: {ex.Message}\n{ex.StackTrace}");
                return true;
            }
        }

        /// <summary>
        /// 유닛을 제어해야 하는지 확인
        /// </summary>
        public static bool ShouldControlUnit(UnitEntityData unit) => ShouldControlUnit(unit, out _);

        /// <summary>
        /// ★ v0.3.14: the same check with the reason for a refusal (logged for party members at turn start: Seelah's
        /// turn was refused with no reason in the log, and a directly controllable unit the mod refuses waits for the
        /// player, which looks like a frozen turn).
        /// </summary>
        public static bool ShouldControlUnit(UnitEntityData unit, out string reason)
        {
            reason = null;
            if (unit == null) { reason = "no unit"; return false; }

            // 죽었거나 의식 없음
            // ★ v0.3.14: by the game's life state, not by HP. 0.3.13 refused any unit at 0 HP or less, but a unit that
            // stays conscious there (Diehard, ferocity) still gets its turn and the game waits for orders: the turn
            // never progressed. Unconscious units cannot act; the game ends their turns itself.
            var state = unit.Descriptor?.State;
            if (state == null || !state.IsConscious) { reason = state?.IsDead == true ? "dead" : "unconscious"; return false; }

            // 플레이어 팩션만 제어
            if (!unit.IsPlayerFaction) { reason = "not in the party's faction"; return false; }

            // ★ v0.2.119: Units the game currently drives itself (confused, panicked, PreventDirectControl, ...)
            // are not directly controllable; their commands come from the game and must not be replaced/interrupted.
            if (!unit.IsDirectlyControllable) { reason = "the game controls it now (" + WhyNotControllable(unit) + ")"; return false; }

            // ★ v0.2.119: Crusade tactical (army) combat has its own command/path pipeline.
            if (Kingmaker.Armies.TacticalCombat.TacticalCombatHelper.IsActive) { reason = "army battle"; return false; }

            var settings = ModSettings.Instance;
            if (settings == null) { reason = "settings not loaded"; return false; }

            // 주인공 여부 확인
            bool isMainCharacter = unit.IsMainCharacter;

            // 주인공 제어 여부
            if (isMainCharacter)
            {
                if (!settings.ControlMainCharacter) { reason = "main character control is off"; return false; }
            }
            else
            {
                // 동료 제어 여부
                if (!settings.ControlCompanions) { reason = "companion control is off"; return false; }
            }

            // 캐릭터별 AI 활성화 여부 확인
            string unitId = unit.UniqueId;
            string unitName = unit.Descriptor?.CharacterName ?? "Unknown";
            var charSettings = settings.GetOrCreateSettings(unitId, unitName);
            if (!charSettings.EnableCustomAI) { reason = "AI is off for this character"; return false; }

            return true;
        }

        // ★ v0.3.22: optional diagnostic only; never issue a command or replace the summon's native brain.
        public static void TickBrain_Postfix(UnitEntityData unit, long __state)
        {
            try { CombatPacingDiagnostics.NativeSummonEnd(unit, __state); }
            catch (Exception ex) { Main.Verbose($"[Pacing] summon timing unavailable: {ex.Message}"); }
        }

        /// <summary>★ v0.3.14: which of the game's reasons for taking direct control away applies (for the log).</summary>
        private static string WhyNotControllable(UnitEntityData unit)
        {
            try
            {
                var parts = new System.Collections.Generic.List<string>();
                var confusion = unit.Get<Kingmaker.UnitLogic.Parts.UnitPartConfusion>();
                if (confusion != null) parts.Add($"confused: {confusion.State}");
                if (unit.Descriptor.State.IsPanicked) parts.Add("panicked");
                if (unit.IsDetached) parts.Add("detached");
                if (!unit.IsInGame) parts.Add("not in game");
                if ((bool)unit.PreventDirectControl && parts.Count == 0) parts.Add("prevented by an effect");
                foreach (var buff in unit.Buffs)
                {
                    if (buff?.Blueprint == null) continue;
                    if (buff.Blueprint.ComponentsArray.Any(c => c is Kingmaker.UnitLogic.FactLogic.ChangeFaction ||
                                                                c is Kingmaker.UnitLogic.FactLogic.SetFleeOrApproachLogic))
                        parts.Add(buff.Name);
                }
                return parts.Count > 0 ? string.Join(", ", parts) : "faction or party mode";
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }
    }

    /// <summary>
    /// 턴제 전투 관련 패치
    /// </summary>
    public static class TurnBasedPatches
    {
        /// <summary>
        /// 턴 시작 시 호출 (수동 패치 - Main.PatchCombatControllerManually)
        /// 이 시점에서는 Status=Scrolling이므로 실제 AI 호출은 Tick에서 수행
        /// </summary>
        public static void StartTurn_Postfix(CombatController __instance)
        {
            try
            {
                if (!Main.Enabled)
                {
                    Main.Verbose("모드 비활성화 상태");
                    return;
                }

                var currentUnit = __instance.CurrentTurn?.Rider;
                if (currentUnit == null)
                {
                    Main.Verbose("CurrentTurn.Rider가 null");
                    return;
                }

                string unitName = currentUnit.Descriptor?.CharacterName ?? "Unknown";
                CombatPacingDiagnostics.Report(unitName); // ★ v0.3.22: includes native enemy/summon turns
                // ★ v0.3.19 (release): turn starts at verbose level - each decision line names the unit and round.
                Main.Verbose($"턴 시작: {unitName} (Status={__instance.CurrentTurn?.Status})");

                bool controlled = CustomBrainPatch.ShouldControlUnit(currentUnit, out string reason);
                s_LastTurnStartRefusal = reason;
                if (!controlled)
                {
                    // ★ v0.3.14: why, and at normal level for the party's own units.
                    // ★ v0.3.19 (release): once per unit and reason (a summon's turn repeated it every round).
                    if (currentUnit.IsPlayerFaction)
                        Tactics.AbilityDiscovery.ReportOnce($"notcontrolled|{currentUnit.UniqueId}|{reason}", () =>
                            $"유닛 제어 대상 아님: {unitName} - {reason}" +
                            (currentUnit.IsDirectlyControllable && reason != "dead" && reason != "unconscious"
                                ? " (the game waits for your orders)" : ""));
                    else
                        Main.Verbose($"유닛 제어 대상 아님: {unitName} - {reason}");
                    return;
                }

                Main.Verbose($"★ AI 제어 대상: {unitName} - TurnControllerTick에서 처리 예정");
                // ★ v0.2.86: StartTurn 시점에는 Status=Scrolling이므로 호출하지 않음
                // TurnControllerTick_Postfix에서 Preparing/Acting 상태일 때 호출
            }
            catch (Exception ex)
            {
                Main.Error($"StartTurn 패치 오류: {ex.Message}\n{ex.StackTrace}");
            }
        }

        /// <summary>
        /// ★ v0.2.113: TurnController.Tick() Postfix
        /// ★ v0.2.119: Only acts in Preparing/Acting and never writes TurnController.Status.
        ///
        /// Native state machine (TurnController.Tick, Assembly-CSharp):
        /// - Prepare(): directly-controllable rider → Status = Preparing (waits for input).
        /// - Tick(), Preparing: as soon as the rider's command slots are non-empty → Status = Acting.
        ///   Commands are only ticked while Acting/Ending (UnitCommandController.TickCommandTurnBased).
        /// - Ending/Ended/Delayed belong to the game (turn wrap-up, or the player delayed the turn).
        /// So issuing a command in Preparing is enough; the previous reflection-based "force Acting" hack was
        /// unnecessary and also fired in Ending/Delayed, flipping Ending back to Acting every frame.
        /// </summary>
        private static string s_LastRefusalKey, s_LastTurnStartRefusal;

        public static void TurnControllerTick_Postfix(TurnController __instance)
        {
            try
            {
                if (!Main.Enabled) return;

                var rider = __instance.Rider;
                if (rider == null) return;

                // 우리가 제어하는 유닛만
                if (!CustomBrainPatch.ShouldControlUnit(rider, out string refusal))
                {
                    // ★ v0.3.14: control lost during a party member's turn (it dropped below 0 HP, was charmed...): say so
                    // once per turn; a controllable unit the mod does not drive waits for the player.
                    if (rider.IsPlayerFaction && __instance.Status == TurnController.TurnStatus.Preparing)
                    {
                        string key = $"{rider.UniqueId}|{System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(__instance)}";
                        if (key != s_LastRefusalKey)
                        {
                            s_LastRefusalKey = key;
                            if (refusal != s_LastTurnStartRefusal)
                                Main.Log($"[TurnTick] {rider.CharacterName}: not controlled this turn - {refusal}");
                        }
                    }
                    return;
                }

                // ★ v0.2.119: respect the "Enable in Turn-Based Mode" setting
                if (!(ModSettings.Instance?.EnableInTurnBased ?? true)) return;

                // IsDirectlyControllable 유닛만 (게임이 AI를 호출하지 않는 유닛)
                if (!rider.IsDirectlyControllable) return;

                var status = __instance.Status;
                if (status != TurnController.TurnStatus.Preparing && status != TurnController.TurnStatus.Acting)
                {
                    return;
                }

                string unitName = rider.Descriptor?.CharacterName ?? "Unknown";

                // ★ v0.2.113: TurnBasedController 호출
                var result = TurnBasedController.Instance.ProcessTurn(rider);

                if (result != null && result.Type == Core.ResultType.EndTurn)
                {
                    // CanEndTurn() accepts both Preparing and Acting for a directly-controllable unit.
                    if (__instance.CanEndTurnAndNoActing())
                    {
                        // ★ v0.3.19 (release): the decision line already says "=> END TURN" when nothing is left to do.
                        // ★ v0.3.20: "Nothing worth doing" repeats the decision line's "=> END TURN".
                        if (result.Reason != "Nothing worth doing") Main.Log($"[TurnTick] {unitName}: EndTurn - {result.Reason}");
                        __instance.ForceToEnd(true);
                    }
                    else if (__instance.CanEndTurn() && !rider.Commands.Empty && !rider.Commands.IsRunning())
                    {
                        // Leftover command that never started (e.g. after the wait timeout) would block
                        // CanEndTurnAndNoActing() forever; clear it so the turn can end next frame.
                        Main.Log($"[TurnTick] {unitName}: EndTurn blocked by idle command - interrupting it");
                        rider.Commands.InterruptAll();
                    }
                }
                // Waiting: nothing to do - a queued command moves the turn to Acting on the game's next Tick().
            }
            catch (Exception ex)
            {
                Main.Error($"TurnControllerTick 패치 오류: {ex.Message}\n{ex.StackTrace}");
            }
        }
    }

    /// <summary>
    /// 전투 시작/종료 감지 패치
    /// </summary>
    public static class CombatPatches
    {
        /// <summary>
        /// ★ v0.3.14: the party entered combat (IPartyCombatHandler, both modes). A fresh combat memory for the new fight.
        /// </summary>
        public static void OnPartyCombatStart()
        {
            Main.Log("★★★ Party combat started ★★★");
            CombatPacingDiagnostics.NewFight();
            TurnBasedController.Instance.ResetAll();
            FailedAbilityTracker.Instance.ClearAll();
            CombatPhaseDetector.ResetCombatTracking();
            // ★ v0.3.15: a touch charge carried into the fight (from a loaded save or the last fight) - diagnostics.
            Tactics.CastOutcome.ReportHeldCharges();
            // ★ v0.3.17: diagnostics are per fight
            Tactics.FightStats.Clear();
            // ★ v0.3.18 (review): per-fight observations also at the start - a loaded save in real time raises no
            // "combat ended" (Player.IsInCombat is not saved), so a retried fight kept the last attempt's blocks.
            Tactics.CastOutcome.Clear();
            Tactics.DamageObserver.Clear();
            Tactics.SaveObserver.Clear();
            Tactics.EnemyThreat.Clear();   // ★ v0.3.20 (test4)
            Tactics.RealTimeTactics.ClearQuiet();   // ★ v0.3.20 (audit PERF-001)
            RealTimeController.ClearWrathHandoff();   // ★ v0.3.21
            Analysis.AoeDangerAnalyzer.ClearPending();
            CompanionAI_Pathfinder.Analysis.AbilityClassifier.ResetCombatReports();
            Tactics.CoverageReport.NewFight();
        }

        /// <summary>
        /// The party left combat. ★ v0.3.14: from the game's party-combat event; it was a postfix on
        /// CombatController.Deactivate, which the game calls on every game-mode switch (dialogue, UI, cutscenes).
        /// ★ v0.2.113: TurnBasedController 사용
        /// </summary>
        public static void OnPartyCombatEnd()
        {
            Main.Log("★★★ Party combat ended ★★★");
            // ★ v0.3.19: whether Buff It 2 The Limit shares the work (logged once when it changes).
            _ = ModInterop.BuffItToTheLimitActive;
            // ★ v0.3.17: [Perf] / [Calib] numbers of the fight that just ended
            Tactics.FightStats.Report();
            Tactics.SaveObserver.ReportFight();
            Tactics.DamageObserver.ReportFight();
            Tactics.EnemyThreat.ReportFight();   // ★ v0.3.20 (test4)
            Tactics.EnemyThreat.Clear();
            // ★ v0.3.17: analysis caches of this fight's units (they are per frame now; this only frees them)
            CompanionAI_Pathfinder.Analysis.CombatRulesAnalyzer.ClearCache();
            // ★ v0.3.18 (review): AI-started toggles the game keeps on out of combat (Mobility) go off first.
            Tactics.CombatMemory.SwitchOffAiToggles();
            // ★ v0.2.113: TurnBasedController 상태 초기화
            TurnBasedController.Instance.ResetAll();

            // ★ v0.2.23: Clear pending action tracker
            PendingActionTracker.Instance.Clear();

            // ★ v0.3.14: damage observations are about this fight's enemies
            Tactics.DamageObserver.Clear();
            // ★ v0.3.15: silent failures and observed saves are about this fight
            Tactics.CastOutcome.Clear();
            Tactics.SaveObserver.Clear();

            // ★ v0.3.14: failures are about this fight's positions and targets; ClearAll was never called, so a
            // blacklist lasted 5 minutes into the next fight.
            FailedAbilityTracker.Instance.ClearAll();

            // ★ v0.3.14: the real-time round clock restarts with each fight. It only reset when a decision happened out
            // of combat, which never happens (out of combat no decision is made), so the second and third real-time
            // fights started in "Midgame".
            CombatPhaseDetector.ResetCombatTracking();

            // v0.2.2: 전투 종료 후 영구 버프 자동 적용
            try
            {
                Main.Verbose("[CombatEnd] Applying permanent buffs to party...");
                RealTimeController.Instance.ApplyPermanentBuffsToParty();
            }
            catch (Exception ex)
            {
                Main.Error($"[CombatEnd] Permanent buff application failed: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// 전투 모드 전환 감지 패치
    /// </summary>
    public static class CombatModePatches
    {
        private static CombatMode _lastMode = CombatMode.None;

        /// <summary>
        /// 모드 변경 감지 및 이벤트 발생
        /// </summary>
        public static void CheckModeChange()
        {
            var currentMode = PathfinderCombatStateAdapter.Instance.CurrentMode;
            if (currentMode != _lastMode)
            {
                Main.Log($"전투 모드 변경: {_lastMode} → {currentMode}");
                _lastMode = currentMode;
                OnModeChanged?.Invoke(currentMode);
            }
        }

        public static event Action<CombatMode> OnModeChanged;
    }

    /// <summary>
    /// v0.2.2: 전투 이벤트 핸들러
    /// 유닛이 전투에서 빠져나올 때 영구 버프 적용
    /// </summary>
    public class CombatEventHandler : IInCombatHandler, IPartyCombatHandler
    {
        /// <summary>★ v0.3.14: the party's combat state changed (the game's own start/end of combat, both modes).</summary>
        public void HandlePartyCombatStateChanged(bool inCombat)
        {
            if (!Main.Enabled) return;
            try
            {
                if (inCombat) CombatPatches.OnPartyCombatStart();
                else CombatPatches.OnPartyCombatEnd();
            }
            catch (Exception ex)
            {
                Main.Error($"[CombatEventHandler] party combat {(inCombat ? "start" : "end")}: {ex.Message}");
            }
        }

        private static CombatEventHandler _instance;
        private static bool _isSubscribed = false;

        public static void Subscribe()
        {
            if (_isSubscribed) return;

            _instance = new CombatEventHandler();
            EventBus.Subscribe(_instance);
            _isSubscribed = true;
            Main.Verbose("[CombatEventHandler] Subscribed to combat events");
        }

        public static void Unsubscribe()
        {
            if (!_isSubscribed || _instance == null) return;

            EventBus.Unsubscribe(_instance);
            _isSubscribed = false;
            _instance = null;
            Main.Verbose("[CombatEventHandler] Unsubscribed from combat events");
        }

        /// <summary>
        /// 유닛이 전투에서 빠져나올 때 호출됨
        /// </summary>
        public void HandleObjectLeaveCombat(UnitEntityData unit)
        {
            if (!Main.Enabled) return;
            if (unit == null) return;

            // 플레이어 팩션만 처리
            if (!unit.IsPlayerFaction) return;
            // ★ v0.3.17: only units the AI controls - a character the player drives keeps its orders.
            if (!CustomBrainPatch.ShouldControlUnit(unit)) return;

            string unitName = unit.Descriptor?.CharacterName ?? "Unknown";
            Main.Verbose($"[CombatEventHandler] {unitName} left combat");

            try
            {
                // v0.2.10: 전투 종료 시 모든 AI 명령 취소
                // Move 명령뿐 아니라 Attack(접근 포함), UseAbility 등 모든 명령이 이동을 유발할 수 있음
                if (unit.Commands != null && unit.Commands.HasUnfinished())
                {
                    Main.Log($"[CombatEventHandler] {unitName}: Cancelling all commands (left combat)");
                    unit.Commands.InterruptAll();
                }

                // v0.2.2: queueOnBusy=true로 호출하여 명령 실행 중이면 대기열에 추가
                RealTimeController.Instance.ApplyPermanentBuffsOutOfCombat(unit, queueOnBusy: true);
            }
            catch (Exception ex)
            {
                Main.Error($"[CombatEventHandler] Failed for {unitName}: {ex.Message}");
            }
        }
    }
}
