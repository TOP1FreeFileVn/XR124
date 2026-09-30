using UnityEngine;

namespace XR124.Combat
{
    // Zeru (GDD mục 7.1): Skill cường hóa 6s giảm hồi A/A-A và ghi lại sát thương; Ultimate phóng vòng năng lượng.
    public sealed class ZeruAbility : PetAbility
    {
        [Header("Skill")]
        [Min(0.1f)] [SerializeField] private float empowerDuration = 6f;
        [Range(0f, 1f)] [SerializeField] private float orbACooldownReduction = 0.5f;
        [Range(0f, 1f)] [SerializeField] private float recordRatio = 0.4f;
        [Min(0f)] [SerializeField] private float recordCapAttackRatio = 2f;

        [Header("Ultimate")]
        [Min(0f)] [SerializeField] private float ultimateAttackRatio = 0.5f;
        [Min(0f)] [SerializeField] private float ultimateRecordedRatio = 0.75f;
        [Range(0f, 1f)] [SerializeField] private float ultimateBonusCooldownReduction = 0.25f;

        [Header("Hiển thị (asset bổ sung sau)")]
        [Tooltip("Vòng năng lượng trên đầu; bật khi đang cường hóa hoặc còn sát thương ghi lại.")]
        [SerializeField] private GameObject energyRingVisual;

        [Header("Runtime (chỉ đọc)")]
        [SerializeField] private float empowerRemaining;
        [SerializeField] private float activeReduction;
        [SerializeField] private float recordedDamage;
        [SerializeField] private bool ultimateBonusPending;

        private bool isFiringUltimate;

        public float RecordedDamage => recordedDamage;
        public bool IsEmpowered => empowerRemaining > 0f;

        // Xóa trạng thái cường hóa, sát thương ghi lại và bonus Ultimate khi vào trận mới.
        public override void ResetForMatch()
        {
            empowerRemaining = 0f;
            activeReduction = 0f;
            recordedDamage = 0f;
            ultimateBonusPending = false;
            isFiringUltimate = false;
            RefreshRing();
        }

        // Bắt đầu cường hóa 6s; nếu vừa dùng Ultimate thì lần này giảm hồi chiêu thêm 25% (tổng 75%) rồi tiêu bonus.
        public override void ActivateSkill(PetCombatant target)
        {
            activeReduction = orbACooldownReduction + (ultimateBonusPending ? ultimateBonusCooldownReduction : 0f);
            ultimateBonusPending = false;
            empowerRemaining = empowerDuration;
            RefreshRing();
        }

        // Gây 50% ATK (trực tiếp) + 75% lượng đã ghi (sát thương chuẩn vì lượng ghi đã qua DEF một lần), xóa lượng ghi và bật bonus cho Skill kế tiếp.
        public override void ActivateUltimate(PetCombatant target)
        {
            float storedDamage = recordedDamage * ultimateRecordedRatio;
            recordedDamage = 0f;

            // Chặn ghi lại chính sát thương của Ultimate vào vòng năng lượng.
            isFiringUltimate = true;
            Owner.DealDirectDamage(target, ultimateAttackRatio);
            Owner.DealTrueDamage(target, storedDamage, false);
            isFiringUltimate = false;

            ultimateBonusPending = true;
            RefreshRing();
        }

        // Đếm ngược thời gian cường hóa và tắt vòng khi hết cường hóa và không còn sát thương ghi lại.
        public override void Tick(float deltaTime)
        {
            if (empowerRemaining <= 0f)
            {
                return;
            }

            empowerRemaining -= deltaTime;
            if (empowerRemaining <= 0f)
            {
                empowerRemaining = 0f;
                activeReduction = 0f;
                RefreshRing();
            }
        }

        // Chỉ A và A-A được giảm hồi chiêu trong lúc cường hóa.
        public override float GetCooldownReduction(CombatActionType action)
        {
            if (!IsEmpowered)
            {
                return 0f;
            }

            return action == CombatActionType.A || action == CombatActionType.AA ? activeReduction : 0f;
        }

        // Ghi 40% sát thương trực tiếp gây ra khi đang cường hóa, tổng không vượt 200% ATK hiện tại.
        public override void OnDamageDealt(in DamageInfo info)
        {
            if (!IsEmpowered || isFiringUltimate || info.kind != DamageKind.Direct)
            {
                return;
            }

            float cap = Owner.EffectiveAttack * recordCapAttackRatio;
            recordedDamage = Mathf.Min(cap, recordedDamage + info.amount * recordRatio);
            RefreshRing();
        }

        // Bật/tắt hình vòng năng lượng theo trạng thái hiện tại.
        private void RefreshRing()
        {
            if (energyRingVisual != null)
            {
                energyRingVisual.SetActive(IsEmpowered || recordedDamage > 0f);
            }
        }
    }
}
