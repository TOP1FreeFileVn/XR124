using UnityEngine;

namespace XR124.Combat
{
    // Onea (GDD mục 7.2): Skill tụ lực 3s rồi đánh 150% ATK; Ultimate tạo lãnh địa; Passive mạnh lên khi máu thấp.
    public sealed class OneaAbility : PetAbility
    {
        [Header("Skill – tụ lực")]
        [Min(0.1f)] [SerializeField] private float chargeDuration = 3f;
        [Range(0f, 1f)] [SerializeField] private float chargeDamageReduction = 0.3f;
        [Min(0f)] [SerializeField] private float chargeAttackRatio = 1.5f;

        [Header("Ultimate – lãnh địa")]
        [Min(0.1f)] [SerializeField] private float domainDuration = 5f;
        [Min(0.1f)] [SerializeField] private float domainRadius = 2.5f;
        [Range(0f, 1f)] [SerializeField] private float domainDamageTakenUp = 0.2f;
        [Range(0f, 1f)] [SerializeField] private float domainAttackUp = 0.2f;
        [Range(0f, 1f)] [SerializeField] private float domainSkillCooldownReduction = 0.5f;

        [Header("Passive")]
        [Range(0.001f, 1f)] [SerializeField] private float hpLostStep = 0.03f;
        [Range(0f, 1f)] [SerializeField] private float damagePerStep = 0.015f;
        [Range(0f, 1f)] [SerializeField] private float lowHpThreshold = 0.3f;
        [Range(0f, 1f)] [SerializeField] private float lowHpDamageReduction = 0.3f;

        [Header("Hiển thị (asset bổ sung sau)")]
        [SerializeField] private GameObject chargeVisual;
        [Tooltip("Prefab vùng lãnh địa; được tạo một lần lúc khởi tạo và đặt lại vị trí mỗi lần dùng Ultimate.")]
        [SerializeField] private GameObject domainVisualPrefab;

        [Header("Runtime (chỉ đọc)")]
        [SerializeField] private float chargeRemaining;
        [SerializeField] private float domainRemaining;
        [SerializeField] private Vector3 domainCenter;

        private PetCombatant chargeTarget;
        private PetCombatant domainTarget;
        private GameObject domainVisualInstance;

        public bool IsCharging => chargeRemaining > 0f;
        public bool IsDomainActive => domainRemaining > 0f;

        // Tạo sẵn VFX lãnh địa (ẩn) để không phải Instantiate trong lúc chiến đấu.
        protected override void OnBind()
        {
            if (domainVisualPrefab != null && domainVisualInstance == null)
            {
                domainVisualInstance = Instantiate(domainVisualPrefab);
                domainVisualInstance.SetActive(false);
            }
        }

        // Hủy tụ lực và lãnh địa đang dở khi vào trận mới.
        public override void ResetForMatch()
        {
            CancelCharge();
            EndDomain();
        }

        // Bắt đầu tụ lực: trong 3s nhận thêm giảm sát thương và không dùng được orb; hết thời gian thì ra đòn.
        public override void ActivateSkill(PetCombatant target)
        {
            chargeTarget = target;
            chargeRemaining = chargeDuration;
            SetActiveSafe(chargeVisual, true);
        }

        // Tạo lãnh địa tại vị trí hiện tại của Onea, tồn tại 5s.
        public override void ActivateUltimate(PetCombatant target)
        {
            domainTarget = target;
            domainCenter = Owner.transform.position;
            domainRemaining = domainDuration;

            if (domainVisualInstance != null)
            {
                domainVisualInstance.transform.position = domainCenter;
                domainVisualInstance.SetActive(true);
            }
        }

        // Đếm ngược tụ lực và lãnh địa; khi đối thủ đứng trong lãnh địa thì liên tục làm mới debuff chịu thêm sát thương.
        public override void Tick(float deltaTime)
        {
            if (IsCharging)
            {
                chargeRemaining -= deltaTime;
                if (chargeRemaining <= 0f)
                {
                    PetCombatant target = chargeTarget;
                    CancelCharge();
                    Owner.DealDirectDamage(target, chargeAttackRatio);
                }
            }

            if (IsDomainActive)
            {
                domainRemaining -= deltaTime;
                if (domainRemaining <= 0f)
                {
                    EndDomain();
                }
                else if (domainTarget != null && domainTarget.IsAlive)
                {
                    if (IsInsideDomain(domainTarget.transform.position))
                    {
                        // Thời hạn ngắn: rời lãnh địa thì debuff tự hết sau vài khung hình.
                        domainTarget.ApplyStatus(StatusEffectType.DamageTakenUp, StatusKeys.OneaDomain, domainDamageTakenUp, 0.2f, Owner);
                    }
                    else
                    {
                        domainTarget.RemoveStatus(StatusEffectType.DamageTakenUp, StatusKeys.OneaDomain);
                    }
                }
            }
        }

        // Khóa orb khi đang tụ lực.
        public override bool CanUseOrbs => !IsCharging;

        // Skill (A-D-H) giảm 50% hồi chiêu khi Onea đứng trong lãnh địa.
        public override float GetCooldownReduction(CombatActionType action)
        {
            return action == CombatActionType.ADH && IsOwnerInsideDomain() ? domainSkillCooldownReduction : 0f;
        }

        // +20% ATK khi Onea đứng trong lãnh địa.
        public override float GetAttackBonusRatio()
        {
            return IsOwnerInsideDomain() ? domainAttackUp : 0f;
        }

        // Passive: cứ mất 3% HP tối đa thì +1.5% sát thương gây ra (làm tròn xuống theo từng bậc).
        public override float GetOutgoingDamageBonus(PetCombatant target)
        {
            float hpLost = 1f - Owner.HpRatio;
            int steps = Mathf.FloorToInt(hpLost / hpLostStep + 0.0001f);
            return steps * damagePerStep;
        }

        // Giảm sát thương từ tụ lực (30%) và từ passive khi HP ≤ 30% (30%); PetCombatant áp giới hạn chung 60%.
        public override float GetDamageReductionBonus()
        {
            float total = 0f;
            if (IsCharging)
            {
                total += chargeDamageReduction;
            }

            if (Owner.HpRatio <= lowHpThreshold)
            {
                total += lowHpDamageReduction;
            }

            return total;
        }

        // Bị choáng khi đang tụ lực thì đòn đánh bị hủy; hồi chiêu Skill vẫn giữ nguyên (GDD mục 7.2).
        public override void OnStunned()
        {
            CancelCharge();
        }

        // Dừng tụ lực và tắt hình tụ lực.
        private void CancelCharge()
        {
            chargeRemaining = 0f;
            chargeTarget = null;
            SetActiveSafe(chargeVisual, false);
        }

        // Kết thúc lãnh địa: xóa debuff trên đối thủ và ẩn VFX.
        private void EndDomain()
        {
            domainRemaining = 0f;
            if (domainTarget != null)
            {
                domainTarget.RemoveStatus(StatusEffectType.DamageTakenUp, StatusKeys.OneaDomain);
                domainTarget = null;
            }

            SetActiveSafe(domainVisualInstance, false);
        }

        // Kiểm tra Onea có đứng trong lãnh địa đang hoạt động không.
        private bool IsOwnerInsideDomain()
        {
            return IsDomainActive && IsInsideDomain(Owner.transform.position);
        }

        // So khoảng cách mặt phẳng XZ bằng bình phương để tránh căn bậc hai; độ cao không ảnh hưởng.
        private bool IsInsideDomain(Vector3 position)
        {
            float dx = position.x - domainCenter.x;
            float dz = position.z - domainCenter.z;
            return dx * dx + dz * dz <= domainRadius * domainRadius;
        }

        // Bật/tắt GameObject nếu đã được gán.
        private static void SetActiveSafe(GameObject target, bool active)
        {
            if (target != null && target.activeSelf != active)
            {
                target.SetActive(active);
            }
        }
    }
}
