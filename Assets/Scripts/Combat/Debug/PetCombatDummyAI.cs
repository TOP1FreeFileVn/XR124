using UnityEngine;

namespace XR124.Combat
{
    // AI thử nghiệm rất đơn giản cho đối thủ: mỗi nhịp ưu tiên Ultimate → Skill → hồi máu khi thấp → A-A → A.
    // Chỉ dùng để test khung; AI thật sẽ thay thế sau.
    public sealed class PetCombatDummyAI : MonoBehaviour
    {
        [SerializeField] private PetCombatant pet;
        [Min(0.1f)] [SerializeField] private float decisionInterval = 1.5f;
        [Range(0f, 1f)] [SerializeField] private float healBelowHpRatio = 0.4f;

        private float decisionTimer;

        // Gán pet được điều khiển từ công cụ dựng scene trong Editor.
        public void Configure(PetCombatant controlledPet)
        {
            pet = controlledPet;
        }

        // Ra quyết định theo nhịp cố định thay vì mỗi khung hình để AI không tiêu AP quá nhanh.
        private void Update()
        {
            if (pet == null || !pet.IsInMatch)
            {
                return;
            }

            decisionTimer -= Time.deltaTime;
            if (decisionTimer > 0f)
            {
                return;
            }

            decisionTimer = decisionInterval;
            Decide();
        }

        // Thử lần lượt theo thứ tự ưu tiên, dừng ở hành động đầu tiên thành công.
        private void Decide()
        {
            if (pet.TryUseUltimate() == ActionResult.Success)
            {
                return;
            }

            if (pet.TryUseAction(CombatActionType.ADH) == ActionResult.Success)
            {
                return;
            }

            if (pet.HpRatio < healBelowHpRatio && pet.TryUseAction(CombatActionType.AH) == ActionResult.Success)
            {
                return;
            }

            if (pet.TryUseAction(CombatActionType.AA) == ActionResult.Success)
            {
                return;
            }

            pet.TryUseAction(CombatActionType.A);
        }
    }
}
