using System;
using UnityEngine;

namespace XR124.Combat
{
    // Thông số một hành động Orb; mọi hiệu ứng là tùy chọn và được thực thi theo thứ tự trong PetCombatant.ExecuteAction.
    [Serializable]
    public struct ActionSpec
    {
        public CombatActionType type;
        [Min(0)] public int apCost;
        [Min(0f)] public float cooldown;

        [Header("Tấn công")]
        [Tooltip("Hệ số ATK của đòn đánh; 0 nghĩa là hành động không gây sát thương.")]
        [Min(0f)] public float attackRatio;
        [Tooltip("Tỉ lệ sát thương gây ra chuyển thành khiên (A-D).")]
        [Min(0f)] public float shieldFromDamageRatio;
        [Tooltip("Tỉ lệ sát thương gây ra chuyển thành HP (A-H).")]
        [Min(0f)] public float lifestealRatio;
        [Tooltip("Gây hiệu ứng nguyên tố theo hệ chính (A-A).")]
        public bool appliesElementEffect;

        [Header("Phòng thủ / hồi phục")]
        [Range(0f, 1f)] public float damageReduction;
        [Min(0f)] public float damageReductionDuration;
        [Tooltip("Hồi HP theo tỉ lệ HP tối đa.")]
        [Range(0f, 1f)] public float healMaxHpRatio;
        [Tooltip("Tạo khiên theo tỉ lệ HP tối đa (D-H).")]
        [Range(0f, 1f)] public float shieldMaxHpRatio;
        [Min(0)] public int apRestore;

        [Header("Kỹ năng")]
        [Tooltip("Kích hoạt Skill riêng của pet (A-D-H).")]
        public bool triggersSkill;

        // Hành động cần mục tiêu khi có gây sát thương, gây hiệu ứng hệ hoặc kích hoạt Skill.
        public bool RequiresTarget => attackRatio > 0f || appliesElementEffect || triggersSkill;
    }

    [CreateAssetMenu(fileName = "CombatRules", menuName = "XR124/Combat/Combat Rules", order = 11)]
    public sealed class CombatRules : ScriptableObject
    {
        [Header("AP / Energy (GDD mục 2)")]
        [Min(1)] public int maxAP = 20;
        [Min(0.05f)] public float apRegenInterval = 2f;
        [Range(0f, 1f)] public float cooldownReductionCap = 0.75f;

        [Header("Công thức sát thương (GDD mục 4)")]
        [Min(1f)] public float defenseConstant = 300f;
        [Range(0f, 1f)] public float damageReductionCap = 0.6f;

        [Header("Khiên (GDD mục 6.3)")]
        [Min(0.1f)] public float shieldDuration = 5f;
        [Range(0f, 2f)] public float shieldCapMaxHpRatio = 0.5f;

        [Header("Hiệu ứng nguyên tố (GDD mục 6.1)")]
        [Range(0f, 1f)] public float burnMaxHpPerSecond = 0.03f;
        [Min(0.1f)] public float burnDuration = 5f;
        [Range(0f, 1f)] public float growthAttackUp = 0.15f;
        [Min(0.1f)] public float growthDuration = 10f;
        [Range(0f, 1f)] public float erosionDamageTakenUp = 0.1f;
        [Min(0.1f)] public float erosionDuration = 10f;
        [Range(0f, 1f)] public float sandstormDefenseDown = 0.15f;
        [Min(0.1f)] public float sandstormDuration = 10f;
        [Range(0f, 1f)] public float frostbiteSlow = 0.1f;
        [Min(0.1f)] public float frostbiteDuration = 5f;
        [Min(0.1f)] public float stunDuration = 3f;
        [Min(0f)] public float stunImmunityDuration = 5f;
        [Range(0f, 1f)] public float balanceAttackSteal = 0.1f;
        [Min(0.1f)] public float balanceDuration = 10f;
        [Min(0.1f)] public float suppressDuration = 2f;

        [Header("Bảng hành động Orb (GDD mục 3)")]
        [SerializeField] private ActionSpec[] actions = BuildDefaultActions();

        [NonSerialized] private ActionSpec[] lookup;

        // Nạp lại toàn bộ bảng Orb mặc định của GDD khi bấm Reset trong Inspector hoặc tạo asset mới.
        private void Reset()
        {
            actions = BuildDefaultActions();
            lookup = null;
        }

        // Xóa bảng tra cứu khi dữ liệu đổi trong Inspector để lần đọc sau dựng lại theo giá trị mới.
        private void OnValidate()
        {
            lookup = null;
        }

        // Lấy thông số hành động theo loại; bảng tra được dựng một lần theo chỉ số enum để truy cập O(1) trong trận.
        public ref readonly ActionSpec GetAction(CombatActionType type)
        {
            if (lookup == null)
            {
                BuildLookup();
            }

            return ref lookup[(int)type];
        }

        // Sắp xếp lại mảng hành động theo chỉ số enum; hành động bị thiếu trong asset sẽ lấy giá trị mặc định của GDD.
        private void BuildLookup()
        {
            ActionSpec[] defaults = BuildDefaultActions();
            lookup = new ActionSpec[CombatConstants.ActionCount];
            bool[] filled = new bool[CombatConstants.ActionCount];

            if (actions != null)
            {
                for (int i = 0; i < actions.Length; i++)
                {
                    int index = (int)actions[i].type;
                    if (index < CombatConstants.ActionCount && !filled[index])
                    {
                        lookup[index] = actions[i];
                        filled[index] = true;
                    }
                }
            }

            for (int i = 0; i < CombatConstants.ActionCount; i++)
            {
                if (!filled[i])
                {
                    lookup[i] = defaults[i];
                }
            }
        }

        // Tạo bảng 10 hành động Orb đúng giá trị GDD mục 3, theo thứ tự enum CombatActionType.
        public static ActionSpec[] BuildDefaultActions()
        {
            return new[]
            {
                new ActionSpec { type = CombatActionType.A, apCost = 1, cooldown = 3f, attackRatio = 0.5f },
                new ActionSpec { type = CombatActionType.D, apCost = 1, cooldown = 3f, damageReduction = 0.1f, damageReductionDuration = 1.5f },
                new ActionSpec { type = CombatActionType.H, apCost = 1, cooldown = 3f, healMaxHpRatio = 0.15f },
                new ActionSpec { type = CombatActionType.AD, apCost = 4, cooldown = 5f, attackRatio = 0.75f, shieldFromDamageRatio = 0.5f },
                new ActionSpec { type = CombatActionType.AH, apCost = 4, cooldown = 5f, attackRatio = 0.75f, lifestealRatio = 0.5f },
                new ActionSpec { type = CombatActionType.DH, apCost = 4, cooldown = 5f, shieldMaxHpRatio = 0.25f },
                new ActionSpec { type = CombatActionType.AA, apCost = 4, cooldown = 5f, attackRatio = 1f, appliesElementEffect = true },
                new ActionSpec { type = CombatActionType.DD, apCost = 4, cooldown = 5f, damageReduction = 0.25f, damageReductionDuration = 2f },
                new ActionSpec { type = CombatActionType.HH, apCost = 4, cooldown = 5f, healMaxHpRatio = 0.25f, apRestore = 2 },
                new ActionSpec { type = CombatActionType.ADH, apCost = 9, cooldown = 10f, triggersSkill = true }
            };
        }
    }
}
