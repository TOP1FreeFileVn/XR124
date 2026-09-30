using System;

namespace XR124.Combat
{
    // Một trạng thái đang tác động lên pet. Key phân biệt các nguồn cùng loại: cùng Type + Key thì làm mới, khác Key thì cộng dồn.
    [Serializable]
    public struct StatusEffect
    {
        public StatusEffectType type;
        public int key;
        public float magnitude;
        public float remaining;
        public int stacks;
        public int maxStacks;
        public float tickTimer;
        [NonSerialized] public PetCombatant source;
    }

    // Một lượt khiên riêng; khiên sắp hết hạn bị trừ trước (GDD mục 4).
    [Serializable]
    public struct ShieldInstance
    {
        public float amount;
        public float remaining;
    }

    // Thông tin một lần gây sát thương, gửi kèm sự kiện để UI/VFX/debug đọc.
    public struct DamageInfo
    {
        public PetCombatant source;
        public PetCombatant target;
        public DamageKind kind;
        public float amount;
        public float absorbedByShield;
        public float elementMultiplier;
        public bool isBurnTick;
    }

    // Key cố định cho các trạng thái cần "làm mới thay vì cộng dồn"; key 0 luôn tạo lượt mới.
    public static class StatusKeys
    {
        public const int Unique = 0;
        public const int Burn = 1;
        public const int Growth = 2;
        public const int Erosion = 3;
        public const int Sandstorm = 4;
        public const int Frostbite = 5;
        public const int Stun = 6;
        public const int StunImmunity = 7;
        public const int BalanceDebuff = 8;
        public const int BalanceBuff = 9;
        public const int Suppress = 10;
        public const int OneaDomain = 100;
    }
}
