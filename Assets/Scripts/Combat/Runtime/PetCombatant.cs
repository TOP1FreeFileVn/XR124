using System;
using System.Collections.Generic;
using UnityEngine;

namespace XR124.Combat
{
    // Trạng thái chiến đấu của một pet: HP, AP, Energy, hồi chiêu, khiên, trạng thái và thực thi hành động Orb.
    // Không phụ thuộc input hay hình ảnh; UI/VFX/AI chỉ gọi TryUseAction/TryUseUltimate và nghe các event.
    [DisallowMultipleComponent]
    public sealed class PetCombatant : MonoBehaviour
    {
        [Header("Cấu hình")]
        [SerializeField] private PetDefinition definition;
        [Tooltip("Nơi đặt model từ PetDefinition.modelPrefab; để trống thì dùng chính transform này.")]
        [SerializeField] private Transform modelRoot;
        [Tooltip("Hình tạm (capsule...) bị ẩn khi PetDefinition đã có modelPrefab.")]
        [SerializeField] private GameObject placeholderVisual;
        [Tooltip("Hệ được chọn trước trận cho pet có canChooseElement (Zeru).")]
        [SerializeField] private ElementType chosenElement = ElementType.None;

        [Header("Runtime (chỉ đọc)")]
        [SerializeField] private float currentHp;
        [SerializeField] private int currentAP;
        [SerializeField] private int currentEnergy;
        [SerializeField] private bool isInMatch;
        [SerializeField] private bool isDefeated;
        [SerializeField] private float[] cooldowns = new float[CombatConstants.ActionCount];
        [SerializeField] private List<StatusEffect> statuses = new List<StatusEffect>(16);
        [SerializeField] private List<ShieldInstance> shields = new List<ShieldInstance>(8);

        private PetAbility ability;
        private CombatRules rules;
        private ElementChart chart;
        private PetCombatant opponent;
        private float apRegenTimer;

        public event Action<PetCombatant, CombatActionType> ActionUsed;
        public event Action<PetCombatant> UltimateUsed;
        public event Action<PetCombatant, DamageInfo> DamageTaken;
        public event Action<PetCombatant, float> Healed;
        public event Action<PetCombatant, StatusEffectType> StatusApplied;
        public event Action<PetCombatant> Defeated;

        public PetDefinition Definition => definition;
        public PetAbility Ability => ability;
        public CombatRules Rules => rules;
        public ElementChart Chart => chart;
        public PetCombatant Opponent => opponent;
        public bool IsInMatch => isInMatch;
        public bool IsAlive => !isDefeated && currentHp > 0f;
        public string DisplayName => definition != null ? definition.displayName : name;

        public float MaxHp => definition != null ? definition.maxHp : 1f;
        public float CurrentHp => currentHp;
        public float HpRatio => currentHp / MaxHp;
        public float BaseAttack => definition != null ? definition.attack : 0f;
        public int CurrentAP => currentAP;
        public int MaxAP => rules != null ? rules.maxAP : 20;
        public int CurrentEnergy => currentEnergy;
        public int MaxEnergy => definition != null ? definition.MaxEnergy : 0;
        public int UltimateCost => definition != null ? definition.ultimateEnergyCost : int.MaxValue;
        public IReadOnlyList<StatusEffect> Statuses => statuses;

        public ElementType PrimaryElement =>
            definition != null && definition.canChooseElement && chosenElement != ElementType.None
                ? chosenElement
                : definition != null ? definition.primaryElement : ElementType.None;

        public ElementType SecondaryElement => definition != null ? definition.secondaryElement : ElementType.None;

        public bool IsStunned => HasStatus(StatusEffectType.Stun);
        public bool IsBurning => HasStatus(StatusEffectType.Burn);

        // ATK hiệu lực = ATK gốc × (1 + buff − debuff + bonus kỹ năng) + ATK cộng thẳng (hiệu ứng Cân bằng).
        public float EffectiveAttack
        {
            get
            {
                float ratio = 1f + SumStatus(StatusEffectType.AttackUp) - SumStatus(StatusEffectType.AttackDown);
                if (ability != null)
                {
                    ratio += ability.GetAttackBonusRatio();
                }

                return BaseAttack * Mathf.Max(0f, ratio) + SumStatus(StatusEffectType.AttackFlatUp);
            }
        }

        // Tổng giảm sát thương nhận vào từ D/D-D và kỹ năng, bị giới hạn bởi damageReductionCap (60%).
        public float TotalDamageReduction
        {
            get
            {
                float total = SumStatus(StatusEffectType.DamageReduction);
                if (ability != null)
                {
                    total += ability.GetDamageReductionBonus();
                }

                float cap = rules != null ? rules.damageReductionCap : 0.6f;
                return Mathf.Clamp(total, 0f, cap);
            }
        }

        // Tổng tăng sát thương phải chịu (Bào mòn, lãnh địa Onea), các nguồn cộng thẳng.
        public float TotalDamageTakenUp => SumStatus(StatusEffectType.DamageTakenUp);

        // Hệ số tốc độ di chuyển cho bộ điều khiển di chuyển: 0 khi choáng, giảm theo Tê cóng.
        public float MoveSpeedMultiplier => IsStunned ? 0f : Mathf.Max(0f, 1f - SumStatus(StatusEffectType.Slow));

        // Tổng khiên hiện có, dùng cho thanh máu/UI.
        public float ShieldTotal
        {
            get
            {
                float total = 0f;
                for (int i = 0; i < shields.Count; i++)
                {
                    total += shields[i].amount;
                }

                return total;
            }
        }

        // Số lần Ultimate có thể dùng ngay với Energy hiện tại (Tiwo tích trữ tối đa 2).
        public int UltimateCharges => UltimateCost > 0 ? currentEnergy / UltimateCost : 0;

        // Liên kết ability và tạo model placeholder/asset một lần lúc khởi tạo để không Instantiate trong trận.
        private void Awake()
        {
            ability = GetComponent<PetAbility>();
            if (ability != null)
            {
                ability.Bind(this);
            }

            if (modelRoot == null)
            {
                modelRoot = transform;
            }

            SpawnModelIfNeeded();
            currentHp = MaxHp;
        }

        // Tạo model từ PetDefinition dưới modelRoot và ẩn hình tạm; khi chưa có asset thì giữ nguyên placeholder trong scene.
        private void SpawnModelIfNeeded()
        {
            if (definition == null || definition.modelPrefab == null)
            {
                return;
            }

            GameObject model = Instantiate(definition.modelPrefab, modelRoot, false);
            model.transform.localRotation = Quaternion.Euler(0f, definition.modelYawOffset, 0f);
            model.transform.localScale = Vector3.one * definition.modelScale;

            // Đổi material theo PetDefinition (ví dụ cùng mesh nhưng màu theo hệ); làm một lần lúc tạo model.
            if (definition.overrideMaterial != null)
            {
                Renderer[] renderers = model.GetComponentsInChildren<Renderer>(true);
                for (int i = 0; i < renderers.Length; i++)
                {
                    Material[] slots = renderers[i].sharedMaterials;
                    for (int s = 0; s < slots.Length; s++)
                    {
                        slots[s] = definition.overrideMaterial;
                    }

                    renderers[i].sharedMaterials = slots;
                }
            }

            // Gán Animator Controller từ PetDefinition; tắt root motion vì vị trí pet do PetAutoBattler/BattleSummoner điều khiển.
            Animator animator = model.GetComponentInChildren<Animator>();
            if (animator != null)
            {
                if (definition.animatorController != null)
                {
                    animator.runtimeAnimatorController = definition.animatorController;
                }

                animator.applyRootMotion = false;
            }

            if (placeholderVisual != null)
            {
                placeholderVisual.SetActive(false);
            }
        }

        // Gán tham chiếu hiển thị từ công cụ dựng scene trong Editor.
        public void ConfigureVisuals(Transform newModelRoot, GameObject newPlaceholder)
        {
            modelRoot = newModelRoot;
            placeholderVisual = newPlaceholder;
        }

        // Gán definition từ code (công cụ Editor hoặc spawner); chỉ hợp lệ khi chưa vào trận.
        public void SetDefinition(PetDefinition newDefinition)
        {
            if (isInMatch)
            {
                Debug.LogWarning($"[Combat] {DisplayName}: không thể đổi PetDefinition trong trận.", this);
                return;
            }

            definition = newDefinition;
            currentHp = MaxHp;
        }

        // Chọn hệ trước trận cho pet có canChooseElement (passive Zeru); từ chối khi đang trong trận.
        public bool SetChosenElement(ElementType element)
        {
            if (isInMatch || definition == null || !definition.canChooseElement || element == ElementType.None)
            {
                return false;
            }

            chosenElement = element;
            return true;
        }

        // Đưa pet vào trận: gán đối thủ và luật, hồi đầy HP/AP, xóa Energy, hồi chiêu, khiên và trạng thái.
        public void BeginMatch(PetCombatant matchOpponent, CombatRules matchRules, ElementChart matchChart)
        {
            opponent = matchOpponent;
            rules = matchRules;
            chart = matchChart;

            currentHp = MaxHp;
            currentAP = MaxAP;
            currentEnergy = 0;
            apRegenTimer = 0f;
            isDefeated = false;
            statuses.Clear();
            shields.Clear();
            Array.Clear(cooldowns, 0, cooldowns.Length);

            if (ability != null)
            {
                ability.ResetForMatch();
            }

            isInMatch = true;
        }

        // Dừng mọi cập nhật chiến đấu khi trận kết thúc; trạng thái giữ nguyên để hiển thị kết quả.
        public void EndMatch()
        {
            isInMatch = false;
        }

        // Vòng cập nhật chính: hồi AP, đếm hồi chiêu, xử lý trạng thái, khiên và ability theo thứ tự cố định.
        private void Update()
        {
            if (!isInMatch || isDefeated)
            {
                return;
            }

            float deltaTime = Time.deltaTime;
            TickAP(deltaTime);
            TickCooldowns(deltaTime);
            TickStatuses(deltaTime);
            TickShields(deltaTime);

            if (ability != null && isInMatch && !isDefeated)
            {
                ability.Tick(deltaTime);
            }
        }

        // Hồi 1 AP mỗi apRegenInterval giây; bộ đếm dừng khi AP đầy để không dồn AP. AP vẫn hồi khi bị choáng.
        private void TickAP(float deltaTime)
        {
            if (currentAP >= MaxAP)
            {
                apRegenTimer = 0f;
                return;
            }

            float interval = rules != null ? rules.apRegenInterval : 2f;
            apRegenTimer += deltaTime;
            while (apRegenTimer >= interval && currentAP < MaxAP)
            {
                apRegenTimer -= interval;
                currentAP++;
            }
        }

        // Giảm mọi đồng hồ hồi chiêu; đứng yên khi bị Uy áp (hệ Thần thoại).
        private void TickCooldowns(float deltaTime)
        {
            if (HasStatus(StatusEffectType.CooldownFreeze))
            {
                return;
            }

            for (int i = 0; i < cooldowns.Length; i++)
            {
                if (cooldowns[i] > 0f)
                {
                    cooldowns[i] = Mathf.Max(0f, cooldowns[i] - deltaTime);
                }
            }
        }

        // Đếm thời gian các trạng thái, gây sát thương thiêu đốt mỗi 1 giây và xử lý hết hạn.
        // Khi Choáng hết hạn thì tự thêm Miễn choáng để tránh bị khóa liên tục (GDD mục 6.1).
        private void TickStatuses(float deltaTime)
        {
            for (int i = statuses.Count - 1; i >= 0; i--)
            {
                StatusEffect status = statuses[i];
                int burnTicks = 0;

                if (status.type == StatusEffectType.Burn)
                {
                    status.tickTimer += deltaTime;
                    // Trừ sai số nhỏ để nhịp cuối cùng ở giây thứ 5 không bị mất do làm tròn float.
                    while (status.tickTimer >= 1f - 0.0001f)
                    {
                        status.tickTimer -= 1f;
                        burnTicks++;
                    }
                }

                status.remaining -= deltaTime;
                bool expired = status.remaining <= 0f;

                if (expired)
                {
                    statuses.RemoveAt(i);
                }
                else
                {
                    statuses[i] = status;
                }

                for (int tick = 0; tick < burnTicks && IsAlive; tick++)
                {
                    DealBurnTick(status);
                }

                if (expired && status.type == StatusEffectType.Stun && rules != null && rules.stunImmunityDuration > 0f)
                {
                    ApplyStatus(StatusEffectType.StunImmunity, StatusKeys.StunImmunity, 0f, rules.stunImmunityDuration, status.source);
                }

                if (!isInMatch || isDefeated)
                {
                    return;
                }

                // Sát thương có thể làm danh sách co lại; kẹp chỉ số để vòng lặp tiếp tục an toàn.
                if (i > statuses.Count)
                {
                    i = statuses.Count;
                }
            }
        }

        // Gây một nhịp thiêu đốt: % HP tối đa × số tầng, là sát thương chuẩn do pet gây thiêu đốt sở hữu.
        private void DealBurnTick(in StatusEffect burn)
        {
            float percent = rules != null ? rules.burnMaxHpPerSecond : 0.03f;
            float baseAmount = MaxHp * percent * Mathf.Max(1, burn.stacks);

            if (burn.source != null)
            {
                burn.source.DealTrueDamage(this, baseAmount, true);
                return;
            }

            DamageInfo info = new DamageInfo
            {
                target = this,
                kind = DamageKind.True,
                amount = Mathf.Round(DamageCalculator.ComputeTrue(baseAmount, null, this)),
                elementMultiplier = 1f,
                isBurnTick = true
            };
            TakeDamage(ref info);
        }

        // Giảm thời gian các lượt khiên và bỏ lượt hết hạn.
        private void TickShields(float deltaTime)
        {
            for (int i = shields.Count - 1; i >= 0; i--)
            {
                ShieldInstance shield = shields[i];
                shield.remaining -= deltaTime;
                if (shield.remaining <= 0f || shield.amount <= 0f)
                {
                    shields.RemoveAt(i);
                }
                else
                {
                    shields[i] = shield;
                }
            }
        }

        // Thời gian hồi còn lại của một hành động, dùng cho UI hiển thị orb.
        public float GetCooldownRemaining(CombatActionType action)
        {
            return cooldowns[(int)action];
        }

        // Kiểm tra toàn bộ điều kiện dùng hành động mà không thực thi; UI dùng để làm mờ nút.
        public ActionResult CanUseAction(CombatActionType action)
        {
            if (!isInMatch || rules == null)
            {
                return ActionResult.MatchNotRunning;
            }

            if (!IsAlive)
            {
                return ActionResult.Defeated;
            }

            if (IsStunned)
            {
                return ActionResult.Stunned;
            }

            if (ability != null && !ability.CanUseOrbs)
            {
                return ActionResult.OrbsBlocked;
            }

            if (cooldowns[(int)action] > 0f)
            {
                return ActionResult.OnCooldown;
            }

            ref readonly ActionSpec spec = ref rules.GetAction(action);
            if (currentAP < spec.apCost)
            {
                return ActionResult.NotEnoughAP;
            }

            if (spec.RequiresTarget && (opponent == null || !opponent.IsAlive))
            {
                return ActionResult.NoTarget;
            }

            return ActionResult.Success;
        }

        // Dùng một hành động Orb: kiểm tra điều kiện, trừ AP, cộng Energy, bắt đầu hồi chiêu rồi thực thi hiệu ứng.
        public ActionResult TryUseAction(CombatActionType action)
        {
            ActionResult check = CanUseAction(action);
            if (check != ActionResult.Success)
            {
                return check;
            }

            ref readonly ActionSpec spec = ref rules.GetAction(action);
            currentAP -= spec.apCost;
            currentEnergy = Mathf.Min(currentEnergy + spec.apCost, MaxEnergy);
            StartCooldown(action, spec.cooldown);

            ExecuteAction(in spec);
            ActionUsed?.Invoke(this, action);
            return ActionResult.Success;
        }

        // Bắt đầu hồi chiêu: gốc × (1 − giảm hồi chiêu), giảm hồi chiêu bị giới hạn bởi cooldownReductionCap (75%).
        private void StartCooldown(CombatActionType action, float baseCooldown)
        {
            float reduction = ability != null ? ability.GetCooldownReduction(action) : 0f;
            reduction = Mathf.Clamp(reduction, 0f, rules.cooldownReductionCap);
            cooldowns[(int)action] = baseCooldown * (1f - reduction);
        }

        // Thực thi hiệu ứng theo thứ tự: sát thương → khiên/hút máu từ sát thương → giảm ST → hồi máu → khiên → AP → hiệu ứng hệ → Skill.
        private void ExecuteAction(in ActionSpec spec)
        {
            float dealt = 0f;
            if (spec.attackRatio > 0f)
            {
                dealt = DealDirectDamage(opponent, spec.attackRatio);
            }

            if (spec.shieldFromDamageRatio > 0f && dealt > 0f)
            {
                AddShield(dealt * spec.shieldFromDamageRatio);
            }

            if (spec.lifestealRatio > 0f && dealt > 0f)
            {
                Heal(dealt * spec.lifestealRatio);
            }

            if (spec.damageReduction > 0f && spec.damageReductionDuration > 0f)
            {
                // Mỗi lần dùng D/D-D là một lượt riêng (key Unique) để cộng dồn theo GDD mục 6.3.
                ApplyStatus(StatusEffectType.DamageReduction, StatusKeys.Unique, spec.damageReduction, spec.damageReductionDuration, this);
            }

            if (spec.healMaxHpRatio > 0f)
            {
                Heal(MaxHp * spec.healMaxHpRatio);
            }

            if (spec.shieldMaxHpRatio > 0f)
            {
                AddShield(MaxHp * spec.shieldMaxHpRatio);
            }

            if (spec.apRestore > 0)
            {
                RestoreAP(spec.apRestore);
            }

            if (spec.appliesElementEffect && opponent != null && opponent.IsAlive)
            {
                ApplyElementEffect(opponent);
            }

            if (spec.triggersSkill && ability != null && opponent != null && opponent.IsAlive)
            {
                ability.ActivateSkill(opponent);
            }
        }

        // Dùng Ultimate khi đủ Energy; không tốn AP và không có hồi chiêu riêng (GDD mục 2).
        public ActionResult TryUseUltimate()
        {
            if (!isInMatch || rules == null)
            {
                return ActionResult.MatchNotRunning;
            }

            if (!IsAlive)
            {
                return ActionResult.Defeated;
            }

            if (IsStunned)
            {
                return ActionResult.Stunned;
            }

            if (currentEnergy < UltimateCost)
            {
                return ActionResult.NotEnoughEnergy;
            }

            if (opponent == null || !opponent.IsAlive)
            {
                return ActionResult.NoTarget;
            }

            currentEnergy -= UltimateCost;
            if (ability != null)
            {
                ability.ActivateUltimate(opponent);
            }

            UltimateUsed?.Invoke(this);
            return ActionResult.Success;
        }

        // Gây sát thương trực tiếp lên mục tiêu theo hệ số ATK; trả về sát thương cuối (kể cả phần khiên hấp thụ).
        public float DealDirectDamage(PetCombatant target, float attackRatio)
        {
            if (target == null || !target.IsAlive || attackRatio <= 0f)
            {
                return 0f;
            }

            float amount = DamageCalculator.ComputeDirect(this, target, attackRatio, out float elementMultiplier);
            DamageInfo info = new DamageInfo
            {
                source = this,
                target = target,
                kind = DamageKind.Direct,
                amount = Mathf.Max(1f, Mathf.Round(amount)),
                elementMultiplier = elementMultiplier
            };

            target.TakeDamage(ref info);
            if (ability != null)
            {
                ability.OnDamageDealt(in info);
            }

            return info.amount;
        }

        // Gây sát thương chuẩn (thiêu đốt, kích nổ, phần ghi lại của Zeru); bỏ qua DEF và tương khắc.
        public float DealTrueDamage(PetCombatant target, float baseAmount, bool isBurnTick)
        {
            if (target == null || !target.IsAlive || baseAmount <= 0f)
            {
                return 0f;
            }

            DamageInfo info = new DamageInfo
            {
                source = this,
                target = target,
                kind = DamageKind.True,
                amount = Mathf.Max(1f, Mathf.Round(DamageCalculator.ComputeTrue(baseAmount, this, target))),
                elementMultiplier = 1f,
                isBurnTick = isBurnTick
            };

            target.TakeDamage(ref info);
            if (ability != null)
            {
                ability.OnDamageDealt(in info);
            }

            return info.amount;
        }

        // Nhận sát thương đã tính xong: khiên sắp hết hạn hấp thụ trước, phần dư trừ vào HP, HP về 0 thì bị hạ.
        public void TakeDamage(ref DamageInfo info)
        {
            if (!IsAlive || info.amount <= 0f)
            {
                return;
            }

            float remainingDamage = info.amount;
            // Danh sách khiên luôn sắp tăng dần theo thời gian còn lại nên duyệt từ đầu là trừ khiên sắp hết hạn trước.
            while (remainingDamage > 0f && shields.Count > 0)
            {
                ShieldInstance shield = shields[0];
                float absorbed = Mathf.Min(shield.amount, remainingDamage);
                shield.amount -= absorbed;
                remainingDamage -= absorbed;
                info.absorbedByShield += absorbed;

                if (shield.amount <= 0f)
                {
                    shields.RemoveAt(0);
                }
                else
                {
                    shields[0] = shield;
                }
            }

            currentHp = Mathf.Max(0f, currentHp - remainingDamage);
            DamageTaken?.Invoke(this, info);

            if (currentHp <= 0f && !isDefeated)
            {
                isDefeated = true;
                Defeated?.Invoke(this);
            }
        }

        // Hồi HP không vượt quá HP tối đa; bỏ qua khi pet đã bị hạ.
        public void Heal(float amount)
        {
            if (!IsAlive || amount <= 0f)
            {
                return;
            }

            float before = currentHp;
            currentHp = Mathf.Min(MaxHp, currentHp + Mathf.Round(amount));
            float healed = currentHp - before;
            if (healed > 0f)
            {
                Healed?.Invoke(this, healed);
            }
        }

        // Thêm một lượt khiên tồn tại shieldDuration giây; phần làm tổng khiên vượt giới hạn % HP bị cắt bỏ.
        public void AddShield(float amount)
        {
            if (!IsAlive || amount <= 0f || rules == null)
            {
                return;
            }

            float capacity = MaxHp * rules.shieldCapMaxHpRatio - ShieldTotal;
            amount = Mathf.Min(Mathf.Round(amount), capacity);
            if (amount <= 0f)
            {
                return;
            }

            ShieldInstance shield = new ShieldInstance { amount = amount, remaining = rules.shieldDuration };
            // Chèn giữ thứ tự tăng dần theo thời gian còn lại để TakeDamage trừ khiên sắp hết hạn trước.
            int index = shields.Count;
            while (index > 0 && shields[index - 1].remaining > shield.remaining)
            {
                index--;
            }

            shields.Insert(index, shield);
        }

        // Hồi AP (H-H); AP hồi lại không tạo Energy và không vượt quá AP tối đa.
        public void RestoreAP(int amount)
        {
            currentAP = Mathf.Min(MaxAP, currentAP + Mathf.Max(0, amount));
        }

        // Thêm hoặc làm mới trạng thái: cùng Type + Key (khác Unique) thì làm mới thời gian và độ mạnh, ngược lại thêm lượt mới.
        public void ApplyStatus(StatusEffectType type, int key, float magnitude, float duration, PetCombatant source)
        {
            if (!IsAlive || duration <= 0f)
            {
                return;
            }

            if (key != StatusKeys.Unique)
            {
                int existing = FindStatus(type, key);
                if (existing >= 0)
                {
                    StatusEffect refreshed = statuses[existing];
                    refreshed.magnitude = magnitude;
                    refreshed.remaining = duration;
                    refreshed.source = source;
                    statuses[existing] = refreshed;
                    StatusApplied?.Invoke(this, type);
                    return;
                }
            }

            statuses.Add(new StatusEffect
            {
                type = type,
                key = key,
                magnitude = magnitude,
                remaining = duration,
                stacks = 1,
                maxStacks = 1,
                source = source
            });
            StatusApplied?.Invoke(this, type);
        }

        // Xóa trạng thái theo Type + Key; trả về true nếu có trạng thái bị xóa.
        public bool RemoveStatus(StatusEffectType type, int key)
        {
            int index = FindStatus(type, key);
            if (index < 0)
            {
                return false;
            }

            statuses.RemoveAt(index);
            return true;
        }

        // Kiểm tra pet có đang chịu ít nhất một trạng thái thuộc loại này không.
        public bool HasStatus(StatusEffectType type)
        {
            for (int i = 0; i < statuses.Count; i++)
            {
                if (statuses[i].type == type)
                {
                    return true;
                }
            }

            return false;
        }

        // Cộng độ mạnh của mọi lượt trạng thái cùng loại (các nguồn cộng thẳng theo GDD mục 6.3).
        public float SumStatus(StatusEffectType type)
        {
            float total = 0f;
            for (int i = 0; i < statuses.Count; i++)
            {
                if (statuses[i].type == type)
                {
                    total += statuses[i].magnitude;
                }
            }

            return total;
        }

        // Tìm chỉ số trạng thái theo Type + Key; trả -1 nếu không có.
        private int FindStatus(StatusEffectType type, int key)
        {
            for (int i = 0; i < statuses.Count; i++)
            {
                if (statuses[i].type == type && statuses[i].key == key)
                {
                    return i;
                }
            }

            return -1;
        }

        // DEF hiệu lực = DEF × (1 − giảm DEF) × (1 − xuyên giáp của người tấn công).
        public float GetEffectiveDefense(float armorPenetration)
        {
            float defense = definition != null ? definition.defense : 0f;
            float defenseDown = Mathf.Clamp01(SumStatus(StatusEffectType.DefenseDown));
            return defense * (1f - defenseDown) * (1f - Mathf.Clamp01(armorPenetration));
        }

        // Gây Thiêu đốt: nếu đang cháy thì +1 tầng (không vượt maxStacks) và làm mới 5s; stackCap > 1 mở khóa cộng dồn (Ultimate Tiwo).
        public void ApplyBurn(PetCombatant source, int stackCap)
        {
            if (!IsAlive || rules == null)
            {
                return;
            }

            int index = FindStatus(StatusEffectType.Burn, StatusKeys.Burn);
            if (index >= 0)
            {
                StatusEffect burn = statuses[index];
                burn.maxStacks = Mathf.Max(burn.maxStacks, stackCap);
                burn.stacks = Mathf.Min(burn.stacks + 1, burn.maxStacks);
                burn.remaining = rules.burnDuration;
                burn.source = source;
                statuses[index] = burn;
            }
            else
            {
                statuses.Add(new StatusEffect
                {
                    type = StatusEffectType.Burn,
                    key = StatusKeys.Burn,
                    magnitude = rules.burnMaxHpPerSecond,
                    remaining = rules.burnDuration,
                    stacks = 1,
                    maxStacks = Mathf.Max(1, stackCap),
                    source = source
                });
            }

            StatusApplied?.Invoke(this, StatusEffectType.Burn);
        }

        // Xóa Thiêu đốt và trả về tổng sát thương gốc còn lại = % HP × số giây còn lại × số tầng (Skill Tiwo kích nổ).
        public float ConsumeBurn()
        {
            int index = FindStatus(StatusEffectType.Burn, StatusKeys.Burn);
            if (index < 0)
            {
                return 0f;
            }

            StatusEffect burn = statuses[index];
            statuses.RemoveAt(index);
            return MaxHp * burn.magnitude * Mathf.Max(1, burn.stacks) * Mathf.Max(0f, burn.remaining);
        }

        // Số tầng Thiêu đốt hiện tại, dùng cho UI và debug.
        public int GetBurnStacks()
        {
            int index = FindStatus(StatusEffectType.Burn, StatusKeys.Burn);
            return index >= 0 ? statuses[index].stacks : 0;
        }

        // Thử gây Choáng; thất bại khi đang choáng hoặc đang Miễn choáng. Báo ability để hủy hành động đang tụ lực.
        public bool TryApplyStun(float duration, PetCombatant source)
        {
            if (!IsAlive || IsStunned || HasStatus(StatusEffectType.StunImmunity))
            {
                return false;
            }

            ApplyStatus(StatusEffectType.Stun, StatusKeys.Stun, 0f, duration, source);
            if (ability != null)
            {
                ability.OnStunned();
            }

            return true;
        }

        // Gây hiệu ứng nguyên tố theo hệ chính của pet này lên mục tiêu (A-A), giá trị lấy từ CombatRules (GDD mục 6.1).
        public void ApplyElementEffect(PetCombatant target)
        {
            if (rules == null || target == null)
            {
                return;
            }

            switch (PrimaryElement)
            {
                case ElementType.Fire:
                    target.ApplyBurn(this, 1);
                    break;
                case ElementType.Grass:
                    ApplyStatus(StatusEffectType.AttackUp, StatusKeys.Growth, rules.growthAttackUp, rules.growthDuration, this);
                    break;
                case ElementType.Water:
                    target.ApplyStatus(StatusEffectType.DamageTakenUp, StatusKeys.Erosion, rules.erosionDamageTakenUp, rules.erosionDuration, this);
                    break;
                case ElementType.Ground:
                    target.ApplyStatus(StatusEffectType.DefenseDown, StatusKeys.Sandstorm, rules.sandstormDefenseDown, rules.sandstormDuration, this);
                    break;
                case ElementType.Ice:
                    target.ApplyStatus(StatusEffectType.Slow, StatusKeys.Frostbite, rules.frostbiteSlow, rules.frostbiteDuration, this);
                    break;
                case ElementType.Electric:
                    target.TryApplyStun(rules.stunDuration, this);
                    break;
                case ElementType.Normal:
                    // Cân bằng: đối thủ −10% ATK, bản thân + 10% ATK gốc của đối thủ (cộng thẳng).
                    target.ApplyStatus(StatusEffectType.AttackDown, StatusKeys.BalanceDebuff, rules.balanceAttackSteal, rules.balanceDuration, this);
                    ApplyStatus(StatusEffectType.AttackFlatUp, StatusKeys.BalanceBuff, target.BaseAttack * rules.balanceAttackSteal, rules.balanceDuration, this);
                    break;
                case ElementType.Myth:
                    target.ApplyStatus(StatusEffectType.CooldownFreeze, StatusKeys.Suppress, 0f, rules.suppressDuration, this);
                    break;
            }
        }
    }
}
