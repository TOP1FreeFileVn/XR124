using UnityEngine;

namespace XR124.Combat
{
    // Lớp cơ sở cho Skill / Ultimate / Passive riêng của từng pet. Gắn cùng GameObject với PetCombatant.
    // Các hàm Get* được PetCombatant và DamageCalculator gọi mỗi khi tính chỉ số, nên phải nhẹ và không cấp phát.
    [RequireComponent(typeof(PetCombatant))]
    public abstract class PetAbility : MonoBehaviour
    {
        protected PetCombatant Owner { get; private set; }

        // Liên kết ability với pet sở hữu; được PetCombatant gọi trong Awake.
        public void Bind(PetCombatant owner)
        {
            Owner = owner;
            OnBind();
        }

        // Cho lớp con chuẩn bị tài nguyên (VFX, trạng thái ban đầu) sau khi đã có Owner.
        protected virtual void OnBind() { }

        // Đặt lại mọi trạng thái nội bộ khi trận mới bắt đầu.
        public virtual void ResetForMatch() { }

        // Kích hoạt Skill khi người chơi dùng A-D-H; mục tiêu là đối thủ hiện tại.
        public abstract void ActivateSkill(PetCombatant target);

        // Kích hoạt Ultimate sau khi PetCombatant đã kiểm tra và trừ Energy.
        public abstract void ActivateUltimate(PetCombatant target);

        // Cập nhật theo thời gian (đếm thời gian cường hóa, tụ lực, lãnh địa...).
        public virtual void Tick(float deltaTime) { }

        // Trả về false khi pet đang bị khóa dùng orb (ví dụ Onea đang tụ lực).
        public virtual bool CanUseOrbs => true;

        // Tỉ lệ giảm hồi chiêu cho một hành động; PetCombatant cộng với giới hạn chung trong CombatRules.
        public virtual float GetCooldownReduction(CombatActionType action) => 0f;

        // Tỉ lệ cộng thêm vào ATK (ví dụ Onea +20% trong lãnh địa).
        public virtual float GetAttackBonusRatio() => 0f;

        // Tỉ lệ tăng sát thương gây ra lên một mục tiêu cụ thể (Tăng_ST_gây_ra trong GDD mục 4).
        public virtual float GetOutgoingDamageBonus(PetCombatant target) => 0f;

        // Tỉ lệ bỏ qua DEF của mục tiêu (ví dụ Tiwo xuyên 30% khi mục tiêu đang cháy).
        public virtual float GetArmorPenetration(PetCombatant target) => 0f;

        // Tỉ lệ giảm sát thương nhận vào từ nội tại hoặc kỹ năng, cộng chung giới hạn 60%.
        public virtual float GetDamageReductionBonus() => 0f;

        // Được gọi sau khi pet gây sát thương thành công (dùng cho Zeru ghi lại sát thương).
        public virtual void OnDamageDealt(in DamageInfo info) { }

        // Được gọi khi pet bị choáng (dùng cho Onea hủy tụ lực).
        public virtual void OnStunned() { }
    }
}
