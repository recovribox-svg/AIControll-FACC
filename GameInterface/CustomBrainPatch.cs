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
    /// - ★ summon control (corrected): in turn-based combat the game does call TickBrain - for the current combatant
    ///   that is not directly controllable (TurnController.Prepare and Tick -> AiBrainController.ForceTick). Party
    ///   members are directly controllable, so their turns are driven from TurnControllerTick_Postfix.
    /// </summary>
    public static class CustomBrainPatch
    {
        private static int _tickCounter = 0;
        private static int _lastPreBuffFrame = 0;

        /// <summary>
        /// AiBrainController.TickBrain() 패치
        /// ★ v0.2.113: 실시간 전투 전용
        ///
        /// From the game (★ summon control: corrected - the old note said turn-based combat never comes here):
        /// - real time: AiBrainController.Tick() -> TickBrain() for every unit the game's AI drives;
        /// - turn-based: the regular Tick() scheduler is skipped, but TurnController.Prepare() and Tick() call
        ///   ForceTick(), which runs TickBrain() for the current combatant when it is not directly controllable.
        /// Directly controllable party members never come here in turn-based combat (TurnControllerTick_Postfix drives
        /// them); a summoned creature AI Control drives does (SummonTurnBridge).
        /// </summary>
        public static bool TickBrain_Prefix(UnitEntityData unit)
        {
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
                    return true;
                }

                Main.ProcessedUnits++;

                // ★ v0.2.113: 턴제/실시간 분기
                if (CombatController.IsInTurnBasedCombat())
                {
                    // ★ summon control: the game's brain call for the current, not directly controllable combatant. A
                    // summon AI Control drives is decided by the turn-based driver here; the game's brain is held back.
                    if (!unit.IsDirectlyControllable && SummonControl.IsOrdinarySummon(unit))
                        return SummonTurnBridge.OnBrainTick(unit);
                    // A directly controllable unit: TurnControllerTick_Postfix drives it (the game does not come here
                    // for such a unit in turn-based combat).
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

            // ★ summon control: a summoned creature that is not a pet is decided by SummonControl alone - also when
            // another mod makes it clickable - and never gets a settings row. Off by default: it stays with the game.
            if (SummonControl.IsOrdinarySummon(unit)) return SummonControl.ShouldDrive(unit, out reason);

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
                bool controlled = CustomBrainPatch.ShouldControlUnit(currentUnit, out string reason);
                s_LastTurnStartRefusal = reason;
                // ★ v0.3.19 (release): turn starts at verbose level - each decision line names the unit and round.
                // ★ v0.3.28 test 11: one line a turn start, with whose turn it is (test 10: three verbose lines a turn -
                // the start, "AI control target" for the party and "not controlled" for every enemy: 330 lines).
                Main.Verbose($"[Turn] {unitName}: turn starts (Status={__instance.CurrentTurn?.Status}) - " +
                             (controlled ? "AI Control acts" : $"not AI Control's: {reason}"));
                if (!controlled)
                {
                    // ★ v0.3.14: why, and at normal level for the party's own units.
                    // ★ v0.3.19 (release): once per unit and reason (a summon's turn repeated it every round).
                    if (currentUnit.IsPlayerFaction)
                        Tactics.AbilityDiscovery.ReportOnce($"notcontrolled|{currentUnit.UniqueId}|{reason}", () =>
                            $"유닛 제어 대상 아님: {unitName} - {reason}" +
                            (currentUnit.IsDirectlyControllable && reason != "dead" && reason != "unconscious"
                                ? " (the game waits for your orders)" : ""));
                    return;
                }

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

        // ★ v0.3.27 (author): the turn being ended, the reason it ended for, and the game time waited for it.
        private static TurnController s_EndingTurn;
        private static string s_EndReason;
        private static float s_EndIdle, s_EndWaited;
        private static bool s_EndCapLogged;

        /// <summary>Longest wait for a turn's end (game seconds): a projectile that never lands cannot hold the fight.</summary>
        private const float MaxTurnEndWait = 4f;

        /// <summary>
        /// ★ v0.3.27 (author): whether an AI turn may end now, by the game's own end-of-turn test for units it drives
        /// (TurnController.Tick and ContinueActing): no projectile of the rider or its mount still in flight
        /// (ProjectileController.HasLaunchedProjectile), not getting up, and idle - not moving - for the game's idle pause
        /// (0.2 s / the animation speed setting x the current time scale, in game time). Called once a frame while the
        /// decision says "end the turn"; the first reason is kept for the log.
        /// </summary>
        private static bool ReadyToEndTurn(TurnController turn, UnitEntityData rider, string reason, out string firstReason, out float waited)
        {
            if (!ReferenceEquals(turn, s_EndingTurn))
            {
                s_EndingTurn = turn;
                s_EndReason = reason;
                s_EndIdle = 0f;
                s_EndWaited = 0f;
                s_EndCapLogged = false;
            }
            firstReason = s_EndReason;
            try
            {
                var time = Game.Instance.TimeController;
                float dt = time.GameDeltaTime;
                s_EndWaited += dt;
                waited = s_EndWaited;
                var mount = turn.Mount;
                bool projectile = Game.Instance.ProjectileController.HasLaunchedProjectile(rider.Descriptor, mount?.Descriptor);
                bool gettingUp = rider.View?.IsGetUp ?? false;
                bool moving = (rider.View?.AgentASP?.IsReallyMoving ?? false) || (mount?.View?.AgentASP?.IsReallyMoving ?? false);
                if (projectile || gettingUp || moving) s_EndIdle = 0f;
                else s_EndIdle += dt;
                float pause = 0.2f / Math.Max(0.01f, Game.Instance.Player.UISettings.TimeScaleAverage) * time.PlayerTimeScale * time.DebugTimeScale;
                if (!projectile && !gettingUp && s_EndIdle > Math.Max(pause, time.DeltaTime)) return true;
                if (s_EndWaited <= MaxTurnEndWait) return false;
                if (!s_EndCapLogged)
                {
                    s_EndCapLogged = true;
                    Main.Log($"[TurnTick] {rider.CharacterName}: turn ended after {MaxTurnEndWait:0} s although " +
                             (projectile ? "a projectile is still in flight" : gettingUp ? "it is still getting up" : "it is still moving"));
                }
                return true;
            }
            catch (Exception ex)
            {
                waited = s_EndWaited;
                Main.Verbose($"[TurnTick] {rider.CharacterName}: turn-end wait skipped: {ex.Message}");
                return true;   // never hold a turn on a failed check
            }
        }

        /// <summary>
        /// The driver said "end the turn": end it the way the game ends its own units' turns, or clear a command that
        /// never started so it can end next frame. Shared by party members (directly controllable: the game's
        /// CanEndTurn gate) and by summons the game runs as AI units (★ summon control: CanEndTurn always refuses a unit
        /// that is not directly controllable, so their gate is the turn still being in Acting; ForceToEnd itself has no
        /// such requirement - the game calls it for these units too, TurnController.OnInterruptCommand).
        /// </summary>
        internal static void CompleteTurn(TurnController turn, UnitEntityData rider, string endReason, bool nativeRider)
        {
            string unitName = rider.Descriptor?.CharacterName ?? "Unknown";
            bool canEnd = nativeRider ? turn.Status == TurnController.TurnStatus.Acting : turn.CanEndTurn();
            if (!canEnd) return;
            var mount = turn.Mount;
            bool noCommands = rider.Commands.Empty && (mount == null || mount.Commands.Empty);
            // CanEndTurn() accepts both Preparing and Acting for a directly-controllable unit.
            if (noCommands)
            {
                // ★ v0.3.27 (author): ended the way the game ends its own units' turns - after the unit's projectiles
                // have landed and it has stood idle for the game's idle pause. Ended at once, the rest of the
                // animation and any projectile in flight played in the gap to the next turn, which the game runs
                // at triple speed (CombatController.TimeScaleBetweenTurns): spells looked sped up, projectiles vanished.
                if (!ReadyToEndTurn(turn, rider, endReason, out string reason, out float waited)) return;
                // ★ v0.3.19 (release): the decision line already says "=> END TURN" when nothing is left to do.
                // ★ v0.3.20: "Nothing worth doing" repeats the decision line's "=> END TURN".
                if (reason != "Nothing worth doing") Main.Log($"[TurnTick] {unitName}: EndTurn - {reason}");
                if (Main.VerboseOn) Main.Verbose($"[TurnTick] {unitName}: turn end waited {waited:0.00} s (projectiles, idle pause)");
                turn.ForceToEnd(true);
            }
            else if (!rider.Commands.Empty && !rider.Commands.IsRunning())
            {
                // Leftover command that never started (e.g. after the wait timeout) would block
                // CanEndTurnAndNoActing() forever; clear it so the turn can end next frame.
                Main.Log($"[TurnTick] {unitName}: EndTurn blocked by idle command - interrupting it");
                rider.Commands.InterruptAll();
            }
        }

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
                // ★ summon control: a summon the game's turn treats as its own AI unit is served by the bridge (waits,
                // timeouts and the end of its turn); it is never scored a second time here.
                if (!rider.IsDirectlyControllable)
                {
                    SummonTurnBridge.OnTurnTick(__instance, rider);
                    return;
                }

                var status = __instance.Status;
                if (status != TurnController.TurnStatus.Preparing && status != TurnController.TurnStatus.Acting)
                {
                    return;
                }

                string unitName = rider.Descriptor?.CharacterName ?? "Unknown";

                // ★ v0.2.113: TurnBasedController 호출
                var result = TurnBasedController.Instance.ProcessTurn(rider);

                if (result != null && result.Type == Core.ResultType.EndTurn)
                    CompleteTurn(__instance, rider, result.Reason, nativeRider: false);
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
            RealTimeController.ClearHarvestTries();   // ★ v0.3.28 test 12
            // ★ summon control: per-fight state of driven summons (pacing entries, the served turn, the adoption count).
            RealTimeController.Instance.ForgetSummonPacing();
            SummonTurnBridge.Reset();
            SummonControl.NewFight();
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
            // ★ summon control: the fight's summons are gone or leave with it - their pacing entries, any unit still
            // waiting for service and the served turn are forgotten (the toggle clean-up above stays as it is).
            RealTimeController.Instance.ForgetSummonPacing();
            SummonTurnBridge.Reset();
            SummonControl.NewFight();
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
