using UnityEngine;

namespace XR124.Combat
{
    // Tiwo (GDD mục 7.3): Skill gây/kích nổ Thiêu đốt; Ultimate mở cộng dồn Thiêu đốt 3 tầng; Passive mạnh hơn lên mục tiêu đang cháy.
    public sealed class TiwoAbility : PetAbility
    {
        [Header("Passive")]
        [Range(0f, 1f)] [SerializeField] private float burningTargetDamageBonus = 0.2f;
        [Range(0f, 1f)] [SerializeField] private float burningTargetArmorPenetration = 0.3f;

        [Header("Ultimate")]
        [Min(1)] [SerializeField] private int ultimateBurnStackCap = 3;

        [Header("Hiển thị (asset bổ sung sau)")]
        [SerializeField] private GameObject detonateVfx;

        private bool isDetonating;

        // Nếu mục tiêu đang cháy: xóa Thiêu đốt và gây ngay toàn bộ sát thương còn lại (được +20% passive); nếu chưa cháy: gây Thiêu đốt.
        public override void ActivateSkill(PetCombatant target)
        {
            if (!target.IsBurning)
            {
                target.ApplyBurn(Owner, 1);
                return;
            }

            float remainingBurnDamage = target.ConsumeBurn();
            // Mục tiêu hết cháy ngay khi ConsumeBurn, nên giữ cờ để passive vẫn cộng +20% cho đòn kích nổ.
            isDetonating = true;
            Owner.DealTrueDamage(target, remainingBurnDamage, false);
            isDetonating = false;

            if (detonateVfx != null)
            {
                detonateVfx.transform.position = target.transform.position;
                detonateVfx.SetActive(false);
                detonateVfx.SetActive(true);
            }
        }

        // Cho phép Thiêu đốt trên mục tiêu cộng dồn tới 3 tầng cho tới khi hết cháy, đồng thời gây thêm 1 tầng.
        public override void ActivateUltimate(PetCombatant target)
        {
            target.ApplyBurn(Owner, ultimateBurnStackCap);
        }

        // Passive: +20% sát thương lên mục tiêu đang cháy (hoặc đang bị kích nổ).
        public override float GetOutgoingDamageBonus(PetCombatant target)
        {
            return IsBurningTarget(target) ? burningTargetDamageBonus : 0f;
        }

        // Passive: bỏ qua 30% DEF của mục tiêu đang cháy.
        public override float GetArmorPenetration(PetCombatant target)
        {
            return IsBurningTarget(target) ? burningTargetArmorPenetration : 0f;
        }

        // Mục tiêu được coi là đang cháy khi có Thiêu đốt hoặc đang nhận đòn kích nổ.
        private bool IsBurningTarget(PetCombatant target)
        {
            return target != null && (isDetonating || target.IsBurning);
        }
    }
}
