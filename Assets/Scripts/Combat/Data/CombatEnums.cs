namespace XR124.Combat
{
    // 8 hệ nguyên tố theo GDD mục 5; None dùng cho pet không có hệ phụ.
    public enum ElementType : byte
    {
        Fire = 0,
        Grass = 1,
        Water = 2,
        Electric = 3,
        Ice = 4,
        Ground = 5,
        Normal = 6,
        Myth = 7,
        None = 255
    }

    // 10 hành động Orb theo GDD mục 3; giá trị số được dùng làm chỉ số mảng hồi chiêu.
    public enum CombatActionType : byte
    {
        A = 0,
        D = 1,
        H = 2,
        AD = 3,
        AH = 4,
        DH = 5,
        AA = 6,
        DD = 7,
        HH = 8,
        ADH = 9
    }

    // Kết quả khi thử dùng hành động, dùng để debug vì sao lệnh bị từ chối.
    public enum ActionResult : byte
    {
        Success,
        MatchNotRunning,
        Defeated,
        Stunned,
        OrbsBlocked,
        OnCooldown,
        NotEnoughAP,
        NotEnoughEnergy,
        NoTarget
    }

    // Loại sát thương: Direct chịu DEF và tương khắc, True bỏ qua cả hai (thiêu đốt, kích nổ).
    public enum DamageKind : byte
    {
        Direct,
        True
    }

    // Các trạng thái runtime; mỗi loại được cộng dồn hoặc làm mới theo GDD mục 6.
    public enum StatusEffectType : byte
    {
        Burn,
        AttackUp,
        AttackDown,
        AttackFlatUp,
        DefenseDown,
        DamageTakenUp,
        DamageReduction,
        Slow,
        Stun,
        StunImmunity,
        CooldownFreeze
    }

    // 3 ấn kí tay tương ứng 3 orb; ghép 1–3 ấn liên tiếp thành một hành động Orb.
    public enum SealType : byte
    {
        None = 0,
        A = 1,
        D = 2,
        H = 3
    }

    public static class CombatConstants
    {
        public const int ElementCount = 8;
        public const int ActionCount = 10;
    }
}
