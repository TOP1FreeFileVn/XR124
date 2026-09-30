using UnityEngine;

namespace XR124.Combat
{
    // Công thức sát thương GDD mục 4, tách riêng để dễ viết unit test và chỉnh cân bằng.
    public static class DamageCalculator
    {
        // Tính sát thương trực tiếp (orb, Skill, Ultimate): ATK × hệ số kỹ năng × tương khắc × hệ số DEF × các hệ số tăng/giảm.
        // DEF dùng dạng K / (K + DEF) nên không bao giờ về 0 và luôn giảm dần khi DEF tăng.
        public static float ComputeDirect(PetCombatant attacker, PetCombatant target, float attackRatio, out float elementMultiplier)
        {
            CombatRules rules = attacker.Rules;
            ElementChart chart = attacker.Chart;

            elementMultiplier = chart != null
                ? chart.GetMultiplier(attacker.PrimaryElement, target.PrimaryElement, target.SecondaryElement)
                : 1f;

            float armorPenetration = attacker.Ability != null ? attacker.Ability.GetArmorPenetration(target) : 0f;
            float defense = target.GetEffectiveDefense(armorPenetration);
            float defenseConstant = rules != null ? rules.defenseConstant : 300f;
            float defenseFactor = defenseConstant / (defenseConstant + defense);

            float raw = attacker.EffectiveAttack * attackRatio * elementMultiplier * defenseFactor;
            return ApplyModifiers(raw, attacker, target);
        }

        // Tính sát thương chuẩn (thiêu đốt, kích nổ): bỏ qua DEF và tương khắc nhưng vẫn chịu các hệ số tăng/giảm.
        public static float ComputeTrue(float baseAmount, PetCombatant attacker, PetCombatant target)
        {
            return ApplyModifiers(baseAmount, attacker, target);
        }

        // Nhân (1 + Tăng_ST_gây_ra) × (1 + Tăng_ST_phải_chịu) × (1 − Giảm_ST_nhận); giảm sát thương đã bị giới hạn trong PetCombatant.
        private static float ApplyModifiers(float amount, PetCombatant attacker, PetCombatant target)
        {
            float outgoingBonus = attacker != null && attacker.Ability != null ? attacker.Ability.GetOutgoingDamageBonus(target) : 0f;
            float result = amount
                           * (1f + outgoingBonus)
                           * (1f + target.TotalDamageTakenUp)
                           * (1f - target.TotalDamageReduction);
            return Mathf.Max(0f, result);
        }
    }
}
