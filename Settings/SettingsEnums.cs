namespace CompanionAI_Pathfinder.Settings
{
    // ★ v0.3.70 features review: in a file of their own (moved from ModSettings.cs, unchanged) so the pure suggestion rules
    // (SuggestedSettingsRules) can be unit-tested without UnityModManager.

    public enum RangePreference
    {
        Mixed,      // 혼합 - 상황에 따라
        Melee,      // 근접 - 적에게 접근
        Ranged      // 원거리 - 거리 유지
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
}
