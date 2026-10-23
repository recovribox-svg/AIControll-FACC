using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityModManagerNet;

namespace CompanionAI_Pathfinder.Settings
{
    /// <summary>
    /// 언어 옵션
    /// </summary>
    public enum Language
    {
        English,
        Korean
    }

    /// <summary>
    /// 로컬라이제이션 시스템
    /// </summary>
    public static class Localization
    {
        public static Language CurrentLanguage { get; set; } = Language.English;

        private static readonly Dictionary<string, Dictionary<Language, string>> Strings = new()
        {
            // Header
            ["Title"] = new() { { Language.English, "AI Control - Fully Autonomous Combat Companions" }, { Language.Korean, "AI Control - Fully Autonomous Combat Companions" } },   // ★ v0.3.12: mod name
            ["Subtitle"] = new() { { Language.English, "Tactical AI for party companions" }, { Language.Korean, "파티 동료를 위한 전술 AI" } },

            // Global Settings
            ["GlobalSettings"] = new() { { Language.English, "Global Settings" }, { Language.Korean, "전역 설정" } },
            ["EnableDebugLogging"] = new() { { Language.English, "Enable Debug Logging" }, { Language.Korean, "디버그 로깅 활성화" } },
            ["Language"] = new() { { Language.English, "Language" }, { Language.Korean, "언어" } },

            // Unit Control Settings
            ["UnitControlSettings"] = new() { { Language.English, "Unit Control Settings" }, { Language.Korean, "유닛 제어 설정" } },
            ["ControlMainCharacter"] = new() { { Language.English, "Control Main Character" }, { Language.Korean, "주인공 제어" } },
            ["ControlMainCharacterDesc"] = new() {
                { Language.English, "Let AI control the main character in combat" },
                { Language.Korean, "전투에서 AI가 주인공을 제어합니다" }
            },
            ["ControlCompanions"] = new() { { Language.English, "Control Companions" }, { Language.Korean, "동료 제어" } },
            ["ControlCompanionsDesc"] = new() {
                { Language.English, "Let AI control companion characters in combat" },
                { Language.Korean, "전투에서 AI가 동료 캐릭터를 제어합니다" }
            },

            // Combat Mode Settings
            ["CombatModeSettings"] = new() { { Language.English, "Combat Mode Settings" }, { Language.Korean, "전투 모드 설정" } },
            ["EnableInTurnBased"] = new() { { Language.English, "Enable in Turn-Based Mode" }, { Language.Korean, "턴제 모드에서 활성화" } },
            ["EnableInRealTime"] = new() { { Language.English, "Enable in Real-Time Mode" }, { Language.Korean, "실시간 모드에서 활성화" } },
            // ★ v0.3.14
            ["RealTimeLegacyEngine"] = new() { { Language.English, "Real time: use the old decision engine" }, { Language.Korean, "실시간: 이전 결정 엔진 사용" } },
            ["RealTimeLegacyEngineDesc"] = new() {
                { Language.English, "Off (recommended): real time uses the same tactical engine as turn-based. On: the pre-0.3.14 real-time engine, kept only as a fallback." },
                { Language.Korean, "끔 (권장): 실시간도 턴제와 같은 전술 엔진을 사용합니다. 켬: 0.3.14 이전의 실시간 엔진 (대비용)." }
            },
            ["RealTimeDecisionInterval"] = new() { { Language.English, "Real-Time Decision Interval" }, { Language.Korean, "실시간 결정 간격" } },
            ["RealTimeDecisionIntervalDesc"] = new() {
                { Language.English, "How often AI makes decisions in real-time mode (seconds)" },
                { Language.Korean, "실시간 모드에서 AI가 결정을 내리는 간격 (초)" }
            },

            // ★ v0.3.7: Progressive spell-resource policy
            ["SpellPolicy"] = new() { { Language.English, "Spell Use" }, { Language.Korean, "주문 사용" } },
            ["SpellPolicyDescHeader"] = new() {
                { Language.English, "How this character spends spell slots on damage and control spells" },
                { Language.Korean, "피해/제어 주문에 주문 슬롯을 쓰는 방식" }
            },
            ["SpellPolicy_Standard"] = new() { { Language.English, "Standard" }, { Language.Korean, "기본" } },
            ["SpellPolicy_Progressive"] = new() { { Language.English, "Progressive" }, { Language.Korean, "점진적" } },
            ["SpellPolicyDesc_Standard"] = new() {
                { Language.English, "Picks the most valuable spell, with a mild preference for weapons and cantrips in easy fights." },
                { Language.Korean, "가장 가치 있는 주문을 선택합니다. 쉬운 전투에서는 무기와 캔트립을 약간 선호합니다." }
            },
            ["SpellPolicyDesc_Progressive"] = new() {
                { Language.English, "Uses the lowest spell level that has a damage or control spell with a real chance to work; higher levels only when lower ones cannot, or against bosses. Cantrips, buffs, heals and items are unaffected." },
                { Language.Korean, "효과가 있을 가능성이 충분한 피해/제어 주문이 있는 가장 낮은 주문 레벨을 사용합니다. 높은 레벨은 낮은 레벨로 안 될 때나 보스에게만 사용합니다. 캔트립, 버프, 치유, 아이템은 영향받지 않습니다." }
            },
            ["ProgressiveBossBypass"] = new() { { Language.English, "Progressive: allow higher spell levels against bosses" }, { Language.Korean, "점진적: 보스에게 높은 레벨 주문 허용" } },
            ["ProgressiveBossBypassDesc"] = new() {
                { Language.English, "Only enemies the game itself marks as bosses; only spells that would hit the boss." },
                { Language.Korean, "게임이 보스로 표시한 적에게만, 보스에게 닿는 주문에만 적용됩니다." }
            },

            // ★ v0.3.19: one switch for activatable abilities (replaces the 0.3.18 pair)
            ["AiManagesToggles"] = new() { { Language.English, "AI manages activatable abilities (toggles)" }, { Language.Korean, "AI가 활성화 능력(토글) 관리" } },
            ["AiManagesTogglesDesc"] = new() {
                { Language.English, "On: in fights the AI switches the toggles it understands on and off by itself - bardic performances, Rage, shaman spirits, Power Attack and the other attack modes, Fighting Defensively, Mobility - and puts them back as you left them when the fight ends. Off: the AI never switches a toggle; they stay exactly as you or another mod (such as Buff It 2 The Limit) set them. Either way the AI fights with whatever is on." },
                { Language.Korean, "켜기: 전투 중 AI가 이해하는 토글(바드 공연, 격노, 샤먼 정령, 강타 등 공격 모드, 방어적 전투, 기동성)을 스스로 켜고 끄며, 전투가 끝나면 원래대로 돌려놓습니다. 끄기: AI는 토글을 전혀 바꾸지 않습니다. 직접 또는 다른 모드(예: Buff It 2 The Limit)가 설정한 그대로 둡니다. 어느 쪽이든 AI는 켜져 있는 효과를 반영해 싸웁니다." }
            },
            ["RespectPlayerToggles"] = new() { { Language.English, "Leave a toggle alone for the rest of the fight once you switch it yourself" }, { Language.Korean, "전투 중 직접 전환한 토글은 그 전투가 끝날 때까지 AI가 건드리지 않음" } },
            ["RespectPlayerTogglesDesc"] = new() {
                { Language.English, "Only while the AI manages toggles. When you click a companion's toggle on its action bar during a fight, the AI keeps its hands off that toggle until the fight ends. Changes made by other mods are not clicks: if a mod manages your toggles, switch the setting above off instead." },
                { Language.Korean, "AI가 토글을 관리할 때만 적용됩니다. 전투 중 동료의 액션 바에서 토글을 직접 클릭하면 그 전투가 끝날 때까지 AI가 그 토글을 건드리지 않습니다. 다른 모드의 변경은 클릭으로 인식되지 않으므로, 모드가 토글을 관리한다면 위 설정을 끄세요." }
            },
            ["AiRecastsSelfBuffs"] = new() { { Language.English, "After fights, AI companions recast their long-lasting self buffs" }, { Language.Korean, "전투 후 AI 동료가 지속 시간이 긴 자기 버프를 다시 시전" } },
            ["AiRecastsSelfBuffsDesc"] = new() {
                { Language.English, "Self buffs lasting hours or permanently that have run out (no items, no toggles), cast when out of combat. Switch off if you buff by hand or with a buff mod, so those spell slots stay yours." },
                { Language.Korean, "몇 시간 이상 또는 영구 지속되는 자기 버프가 사라졌을 때 전투 밖에서 다시 시전합니다(아이템, 토글 제외). 직접 또는 버프 모드로 버프한다면 꺼서 주문 슬롯을 남겨 두세요." }
            },
            // ★ v0.3.20 (audit INT-001)
            ["SteerPlayerWalksAroundTraps"] = new() { { Language.English, "Steer my own walk orders around spotted traps" }, { Language.Korean, "내 이동 명령을 발견된 함정을 피해 우회" } },
            ["SteerPlayerWalksAroundTrapsDesc"] = new() {
                { Language.English, "When you order a party member to walk (real time or out of combat) and the way crosses a trap your party has spotted, the walk goes around it or stops short. Unspotted traps and turn-based moves are never changed. Switch off to walk exactly where you click." },
                { Language.Korean, "실시간 또는 전투 밖에서 파티원에게 이동을 명령했을 때 경로가 파티가 발견한 함정을 지나면, 함정을 우회하거나 그 앞에서 멈춥니다. 발견되지 않은 함정과 턴제 이동은 바뀌지 않습니다. 클릭한 곳으로 그대로 걷게 하려면 끄세요." }
            },
            // ★ v0.3.21
            ["WrathTacticsFirst"] = new() { { Language.English, "Real time: let Wrath Tactics act first; AI Control fills the gaps" }, { Language.Korean, "실시간: Wrath Tactics가 먼저 행동하고, AI Control이 빈틈을 채움" } },
            ["WrathTacticsFirstDesc"] = new() {
                { Language.English, "Only with the Wrath Tactics mod. When a companion that has Wrath Tactics rules becomes free, Wrath Tactics checks its rules first; AI Control acts only when no rule gives a command, a moment later. A rule whose action is \"Do nothing\" gives no command, so AI Control acts anyway. Turn-based combat and companions without rules are not affected." },
                { Language.Korean, "Wrath Tactics 모드가 있을 때만 적용됩니다. Wrath Tactics 규칙이 있는 동료가 행동할 수 있게 되면 Wrath Tactics가 먼저 규칙을 확인하고, 명령을 내리는 규칙이 없을 때만 잠시 후 AI Control이 행동합니다. \"아무것도 하지 않음\" 규칙은 명령을 내리지 않으므로 AI Control이 행동합니다. 턴제 전투와 규칙이 없는 동료에게는 영향이 없습니다." }
            },
            ["WrathTacticsDetected"] = new() { { Language.English, "Wrath Tactics is active." }, { Language.Korean, "Wrath Tactics가 활성화되어 있습니다." } },
            ["BuffItDetected"] = new() { { Language.English, "Buff It 2 The Limit is active. If it manages your companions' toggles or buffs, switch the matching setting above off so the two mods do not undo each other's work." }, { Language.Korean, "Buff It 2 The Limit이 활성화되어 있습니다. 그 모드가 동료의 토글이나 버프를 관리한다면, 두 모드가 서로의 작업을 되돌리지 않도록 위의 해당 설정을 끄세요." } },

            // ★ v0.3.0: Consumables
            ["ConsumableSettings"] = new() { { Language.English, "Consumables (quick-slot items)" }, { Language.Korean, "소모품 (퀵슬롯 아이템)" } },
            ["ConsumableSettingsDesc"] = new() {
                { Language.English, "Only items in a character's quick slots (belt) can be used in combat (game rule)." },
                { Language.Korean, "전투 중에는 퀵슬롯(벨트)에 있는 아이템만 사용할 수 있습니다 (게임 규칙)." }
            },
            // ★ v0.3.13
            ["PetsUseItems"] = new() { { Language.English, "Animal companions can use their belt items in combat" }, { Language.Korean, "동물 동료가 전투 중 벨트 아이템 사용 가능" } },
            ["PetsUseItemsDesc"] = new() {
                { Language.English, "Changes a game rule (also for you): the base game never lets animal companions use items in combat. Takes effect at once." },
                { Language.Korean, "게임 규칙을 바꿉니다 (플레이어 조작에도 적용): 기본 게임은 동물 동료의 전투 중 아이템 사용을 막습니다. 즉시 적용됩니다." }
            },
            ["UseConsumables"] = new() { { Language.English, "Use consumables in combat" }, { Language.Korean, "전투 중 소모품 사용" } },
            ["UsePotions"] = new() { { Language.English, "Potions" }, { Language.Korean, "물약" } },
            ["UseScrollsAndWands"] = new() { { Language.English, "Scrolls and wands" }, { Language.Korean, "두루마리 및 완드" } },
            ["UseThrownItems"] = new() { { Language.English, "Alchemical and thrown items (bombs, alchemist's fire, ...)" }, { Language.Korean, "연금술/투척 아이템 (폭탄, 연금술사의 불 등)" } },
            ["PotionHealThreshold"] = new() { { Language.English, "Drink healing potions below HP %" }, { Language.Korean, "체력 % 이하일 때 치유 물약 사용" } },

            // Party Members
            ["PartyMembers"] = new() { { Language.English, "Party Members" }, { Language.Korean, "파티원" } },
            ["AI"] = new() { { Language.English, "AI" }, { Language.Korean, "AI" } },
            ["Character"] = new() { { Language.English, "Character" }, { Language.Korean, "캐릭터" } },
            ["Range"] = new() { { Language.English, "Range" }, { Language.Korean, "거리" } },
            ["NoCharacters"] = new() { { Language.English, "No characters available. Load a save game first." }, { Language.Korean, "사용 가능한 캐릭터가 없습니다. 먼저 세이브 파일을 불러오세요." } },

            // Range Preference
            ["RangePreference"] = new() { { Language.English, "Range Preference" }, { Language.Korean, "거리 선호" } },
            ["RangePreferenceDesc"] = new() { { Language.English, "How should this character engage enemies?" }, { Language.Korean, "이 캐릭터가 적과 어떻게 교전할까요?" } },

            // Range names
            ["Range_Mixed"] = new() { { Language.English, "Mixed" }, { Language.Korean, "혼합" } },
            ["Range_Melee"] = new() { { Language.English, "Melee" }, { Language.Korean, "근접" } },
            ["Range_Ranged"] = new() { { Language.English, "Ranged" }, { Language.Korean, "원거리" } },

            // Range descriptions
            ["RangeDesc_Mixed"] = new() {
                { Language.English, "Uses both melee and ranged attacks depending on situation." },
                { Language.Korean, "상황에 따라 근접과 원거리 공격을 모두 사용합니다." }
            },
            ["RangeDesc_Melee"] = new() {
                { Language.English, "Prefers close combat. Actively moves toward enemies." },
                { Language.Korean, "근접 전투를 선호합니다. 적에게 적극적으로 접근합니다." }
            },
            ["RangeDesc_Ranged"] = new() {
                { Language.English, "Prefers ranged combat. Maintains distance from enemies." },
                { Language.Korean, "원거리 전투를 선호합니다. 적과 거리를 유지합니다." }
            },

            // v0.2.3: AI Role
            ["Role"] = new() { { Language.English, "Role" }, { Language.Korean, "역할" } },
            ["RolePreference"] = new() { { Language.English, "AI Role" }, { Language.Korean, "AI 역할" } },
            ["RolePreferenceDesc"] = new() { { Language.English, "What role should this character play?" }, { Language.Korean, "이 캐릭터가 어떤 역할을 수행할까요?" } },

            // Role names
            ["Role_DPS"] = new() { { Language.English, "DPS" }, { Language.Korean, "딜러" } },
            ["Role_Tank"] = new() { { Language.English, "Tank" }, { Language.Korean, "탱커" } },
            ["Role_Support"] = new() { { Language.English, "Support" }, { Language.Korean, "서포터" } },

            // Role descriptions
            ["RoleDesc_DPS"] = new() {
                { Language.English, "Focus on damage output. Attacks enemies, uses debuffs." },
                { Language.Korean, "공격력에 집중합니다. 적을 공격하고 디버프를 사용합니다." }
            },
            ["RoleDesc_Tank"] = new() {
                { Language.English, "Focus on survival and protection. Uses self-buffs, taunts enemies." },
                { Language.Korean, "생존과 보호에 집중합니다. 자기 버프와 도발을 사용합니다." }
            },
            ["RoleDesc_Support"] = new() {
                { Language.English, "Focus on healing and buffing allies. Heals wounded party members." },
                { Language.Korean, "아군 치료와 버프에 집중합니다. 부상당한 파티원을 치료합니다." }
            },

            // Status
            ["Status"] = new() { { Language.English, "Status" }, { Language.Korean, "상태" } },
            ["Enabled"] = new() { { Language.English, "Enabled" }, { Language.Korean, "활성화" } },
            ["Disabled"] = new() { { Language.English, "Disabled" }, { Language.Korean, "비활성화" } },
            ["TickBrainPatch"] = new() { { Language.English, "TickBrain Patch" }, { Language.Korean, "TickBrain 패치" } },
            ["CombatMode"] = new() { { Language.English, "Combat Mode" }, { Language.Korean, "전투 모드" } },
            ["CombatMode_TurnBased"] = new() { { Language.English, "Turn-Based" }, { Language.Korean, "턴제" } },
            ["CombatMode_RealTime"] = new() { { Language.English, "Real-Time" }, { Language.Korean, "실시간" } },
            ["CombatMode_None"] = new() { { Language.English, "Out of Combat" }, { Language.Korean, "전투 외" } },
            ["AITickCount"] = new() { { Language.English, "AI Tick Count" }, { Language.Korean, "AI Tick 횟수" } },
            ["ProcessedUnits"] = new() { { Language.English, "Processed Units" }, { Language.Korean, "처리된 유닛" } },

            // ★ v0.2.38: Pre-Buff
            ["PreBuffParty"] = new() { { Language.English, "Pre-Buff Party" }, { Language.Korean, "파티 버프 적용" } },
            ["PreBuffPartyDesc"] = new() {
                { Language.English, "Apply low-level buffs (level 1-2) to all party members" },
                { Language.Korean, "모든 파티원에게 저레벨 버프(1-2레벨)를 적용합니다" }
            },
        };

        public static string Get(string key)
        {
            if (Strings.TryGetValue(key, out var translations))
            {
                if (translations.TryGetValue(CurrentLanguage, out var text))
                    return text;
                if (translations.TryGetValue(Language.English, out var fallback))
                    return fallback;
            }
            return key;
        }

        public static string GetRangeName(RangePreference range) => Get($"Range_{range}");
        public static string GetSpellPolicyName(SpellResourcePolicy policy) => Get($"SpellPolicy_{policy}");
        public static string GetSpellPolicyDescription(SpellResourcePolicy policy) => Get($"SpellPolicyDesc_{policy}");
        public static string GetRangeDescription(RangePreference range) => Get($"RangeDesc_{range}");

        // v0.2.3: AI Role localization
        public static string GetRoleName(AIRole role) => Get($"Role_{role}");
        public static string GetRoleDescription(AIRole role) => Get($"RoleDesc_{role}");
    }

    /// <summary>
    /// 거리 선호도
    /// </summary>
    public enum RangePreference
    {
        Mixed,      // 혼합 - 상황에 따라
        Melee,      // 근접 - 적에게 접근
        Ranged      // 원거리 - 거리 유지
    }

    /// <summary>★ v0.3.7: how a caster spends spell slots on offensive spells (orthogonal to role and range).</summary>
    public enum SpellResourcePolicy
    {
        Standard,     // existing behaviour
        Progressive   // lowest usable spell level first; higher levels for bosses or when lower ones cannot work
    }

    /// <summary>
    /// AI 역할
    /// </summary>
    public enum AIRole
    {
        DPS,        // 딜러 - 공격 우선
        Tank,       // 탱커 - 방어, 도발
        Support     // 서포터 - 힐, 버프
    }

    /// <summary>
    /// 개별 캐릭터 설정
    /// </summary>
    public class CharacterSettings
    {
        public string CharacterId { get; set; } = "";
        public string CharacterName { get; set; } = "";
        public bool EnableCustomAI { get; set; } = true;
        public RangePreference RangePreference { get; set; } = RangePreference.Mixed;
        public AIRole Role { get; set; } = AIRole.DPS;

        // 전투 행동
        public int HealAtHPPercent { get; set; } = 30;
        /// <summary>★ v0.3.7: default Standard, so existing settings files behave exactly as before.</summary>
        public SpellResourcePolicy SpellPolicy { get; set; } = SpellResourcePolicy.Standard;
        public bool AvoidFriendlyFire { get; set; } = true;
    }

    /// <summary>
    /// 전역 모드 설정
    /// </summary>
    public class ModSettings
    {
        public static ModSettings Instance { get; private set; }
        private static UnityModManager.ModEntry _modEntry;

        // 로깅
        // ★ v0.2.119: off by default - verbose logging writes several lines per frame in turn-based mode
        public bool EnableDebugLogging { get; set; } = false;
        public Language UILanguage { get; set; } = Language.English;

        // 유닛 제어 설정
        public bool ControlMainCharacter { get; set; } = false;
        public bool ControlCompanions { get; set; } = true;

        // 전투 모드 설정
        public bool EnableInTurnBased { get; set; } = true;
        public bool EnableInRealTime { get; set; } = true;
        /// <summary>★ v0.3.14: false = real time is decided by Tactics/TacticalEngine (RealTimeTactics); true = the old UnifiedDecisionEngine.</summary>
        public bool RealTimeLegacyEngine { get; set; } = false;
        public float RealTimeDecisionInterval { get; set; } = 0.5f;

        // ★ v0.3.0: 소모품 (퀵슬롯 아이템) 사용 설정
        public bool UseConsumables { get; set; } = true;
        public bool UsePotions { get; set; } = true;
        public bool UseScrollsAndWands { get; set; } = true;
        public bool UseThrownItems { get; set; } = true;
        public int PotionHealThresholdPercent { get; set; } = 50;
        /// <summary>★ v0.3.13: lift the game's "animal companions cannot use items in combat" rule (GameInterface/PetItemsPatch).</summary>
        public bool PetsUseItems { get; set; } = true;

        /// <summary>★ v0.3.7: Progressive casters may use higher spell levels on enemies the game marks as bosses.</summary>
        public bool ProgressiveBossBypass { get; set; } = true;

        /// <summary>
        /// ★ v0.3.19: one owner for activatable abilities (toggles). On: the AI switches the ones it can value -
        /// performances, Rage and spirits (AA-2), attack modes and Mobility (AA-3) - and puts back after the fight what it
        /// switched. Off: the AI switches no toggle at all, on or off, in or out of combat: they are the player's, or another
        /// mod's (Buff It 2 The Limit sets IsOn itself, at combat start, after a round limit and when combat ends). The AI
        /// still sees what is on. Replaces AiStartsToggles and AiSwitchesCombatModes (read once to migrate, see Load).
        /// </summary>
        public bool AiManagesToggles { get; set; } = true;

        /// <summary>★ v0.3.18 (AA-2), legacy since 0.3.19: read only to migrate to <see cref="AiManagesToggles"/>.</summary>
        public bool AiStartsToggles { get; set; } = true;

        /// <summary>★ v0.3.18 (AA-3), legacy since 0.3.19: read only to migrate to <see cref="AiManagesToggles"/>.</summary>
        public bool AiSwitchesCombatModes { get; set; } = true;

        /// <summary>
        /// ★ v0.3.19: after a fight (and while out of combat in real time) AI-controlled characters cast their missing
        /// long-lasting self buffs (RealTimeController.ApplyPermanentBuffsOutOfCombat). Off for players whose buffing is
        /// done by hand or by a buff mod (Buff It 2 The Limit), which may have planned those spell slots.
        /// </summary>
        public bool AiRecastsSelfBuffs { get; set; } = true;

        /// <summary>★ v0.3.19: a toggle the player clicks during a fight is left as they set it until the fight ends.</summary>
        public bool RespectPlayerToggles { get; set; } = true;

        /// <summary>
        /// ★ v0.3.20 (audit INT-001): the player's own walk orders (real time and out of combat) are steered around traps the
        /// party has spotted (GameInterface/TrapAvoidancePatch). It was always on, with no switch. On by default: today's
        /// behaviour. Turn-based moves are never changed.
        /// </summary>
        public bool SteerPlayerWalksAroundTraps { get; set; } = true;

        /// <summary>
        /// ★ v0.3.21: real time with the Wrath Tactics mod - an idle companion that has Wrath Tactics rules is offered to
        /// Wrath Tactics first and AI Control decides once it gave no command (Tactics/WrathHandoff). Does nothing
        /// without Wrath Tactics; turn-based is never affected.
        /// </summary>
        public bool WrathTacticsFirst { get; set; } = true;

        // 캐릭터별 설정
        public Dictionary<string, CharacterSettings> CharacterSettings { get; set; } = new();

        /// <summary>
        /// 캐릭터 설정 가져오기 (없으면 생성)
        /// </summary>
        public CharacterSettings GetOrCreateSettings(string characterId, string characterName = null)
        {
            if (string.IsNullOrEmpty(characterId))
                return new CharacterSettings();

            if (!CharacterSettings.TryGetValue(characterId, out var settings))
            {
                settings = new CharacterSettings
                {
                    CharacterId = characterId,
                    CharacterName = characterName ?? characterId,
                    EnableCustomAI = true,
                    RangePreference = RangePreference.Mixed
                };
                CharacterSettings[characterId] = settings;
            }

            if (!string.IsNullOrEmpty(characterName))
                settings.CharacterName = characterName;

            return settings;
        }

        #region Save/Load

        private static string GetSettingsPath()
        {
            return Path.Combine(_modEntry.Path, "settings.json");
        }

        public static void Load(UnityModManager.ModEntry modEntry)
        {
            _modEntry = modEntry;
            try
            {
                string path = GetSettingsPath();
                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path);
                    var settings = JsonConvert.DeserializeObject<ModSettings>(json);
                    if (settings != null)
                    {
                        // ★ v0.3.19: the toggle settings became one. A file from before keeps the user's choice: the AI
                        // manages toggles only if both old switches were on (one switched off = the user wanted control).
                        try
                        {
                            if (json.IndexOf("\"" + nameof(AiManagesToggles) + "\"", StringComparison.Ordinal) < 0)
                                settings.AiManagesToggles = settings.AiStartsToggles && settings.AiSwitchesCombatModes;
                        }
                        catch { }
                        Instance = settings;
                        Main.Log("Settings loaded successfully");
                    }
                    else
                    {
                        Instance = new ModSettings();
                    }
                }
                else
                {
                    Main.Log("Settings file not found, creating default");
                    Instance = new ModSettings();
                    Save();
                }
            }
            catch (Exception ex)
            {
                Main.Error($"Failed to load settings: {ex.Message}");
                BackUpUnreadableSettings();
                Instance = new ModSettings();
            }
        }

        /// <summary>
        /// ★ v0.3.12: the next Save overwrites settings.json with defaults; keep the unreadable file so per-character
        /// settings can be recovered by hand (settings.json.unreadable-yyyyMMdd-HHmmss).
        /// </summary>
        private static void BackUpUnreadableSettings()
        {
            try
            {
                string path = GetSettingsPath();
                if (!File.Exists(path)) return;
                string backup = $"{path}.unreadable-{DateTime.Now:yyyyMMdd-HHmmss}";
                File.Copy(path, backup, overwrite: false);
                Main.Error($"Unreadable settings kept as {Path.GetFileName(backup)}; defaults are used");
            }
            catch (Exception ex)
            {
                Main.Error($"Could not back up unreadable settings: {ex.Message}");
            }
        }

        public static void Save()
        {
            if (Instance == null || _modEntry == null) return;

            try
            {
                string path = GetSettingsPath();
                // Bind only to overloads that the game's own Newtonsoft.Json build (8.0.0.0, Owlcat) provably
                // exposes: Assembly-CSharp itself calls SerializeObject(object, Formatting, JsonSerializerSettings).
                string json = JsonConvert.SerializeObject(Instance, Formatting.Indented, new JsonSerializerSettings());
                File.WriteAllText(path, json);
                Main.Verbose("Settings saved");
            }
            catch (Exception ex)
            {
                Main.Error($"Failed to save settings: {ex.Message}");
            }
        }

        #endregion
    }
}
