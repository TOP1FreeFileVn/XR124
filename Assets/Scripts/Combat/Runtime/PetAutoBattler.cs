using System;
using UnityEngine;

namespace XR124.Combat
{
    // Cho pet tự đánh: quay về phía đối thủ, tự đi tới khi ngoài tầm và tự đánh thường khi trong tầm.
    // Mỗi bước di chuyển được ArenaPlacement kiểm tra (trên sàn, trong vòng đấu trường, không chồng collider); bị chặn thì đứng lại
    // và ghi lý do vào movementState thay vì đi xuyên đồ vật.
    [RequireComponent(typeof(PetCombatant))]
    public sealed class PetAutoBattler : MonoBehaviour
    {
        [SerializeField] private ArenaPlacement placement;
        [Tooltip("Animator của model (bổ sung sau). Chỉ set tham số nào tồn tại trong controller.")]
        [SerializeField] private Animator animator;
        [Min(0f)] [SerializeField] private float turnSpeedDegrees = 360f;
        [Tooltip("Thử lệch hướng ±góc này khi đường thẳng bị chặn.")]
        [Range(0f, 90f)] [SerializeField] private float detourAngle = 45f;

        [Header("Runtime (chỉ đọc)")]
        [SerializeField] private string movementState = "Idle";
        [SerializeField] private float attackTimer;

        private static readonly int SpeedHash = Animator.StringToHash("Speed");
        private static readonly int AttackHash = Animator.StringToHash("Attack");
        private static readonly int CastHash = Animator.StringToHash("Cast");
        private static readonly int UltimateHash = Animator.StringToHash("Ultimate");
        private static readonly int HitHash = Animator.StringToHash("Hit");
        private static readonly int DieHash = Animator.StringToHash("Die");
        private static readonly int SummonHash = Animator.StringToHash("Summon");
        private static readonly int LandHash = Animator.StringToHash("Land");

        private PetCombatant pet;
        private bool animatorResolved;
        private bool hasSpeed, hasAttack, hasCast, hasUltimate, hasHit, hasDie, hasSummon, hasLand;

        public event Action<PetAutoBattler> BasicAttackPerformed;

        public string MovementState => movementState;

        // Gán bộ kiểm tra vị trí đấu trường từ công cụ Editor hoặc BattleSummoner.
        public void Configure(ArenaPlacement floorPlacement)
        {
            placement = floorPlacement;
        }

        // Lấy PetCombatant. Animator được tìm muộn (EnsureAnimator) vì model do PetCombatant.Awake tạo ra,
        // mà thứ tự Awake giữa hai component trên cùng GameObject không cố định.
        private void Awake()
        {
            pet = GetComponent<PetCombatant>();
        }

        // Start chạy sau mọi Awake nên model chắc chắn đã được tạo.
        private void Start()
        {
            EnsureAnimator();
        }

        // Tìm Animator của model và ghi nhận tham số có sẵn, chỉ làm một lần.
        private void EnsureAnimator()
        {
            if (animatorResolved)
            {
                return;
            }

            animatorResolved = true;
            if (animator == null)
            {
                animator = GetComponentInChildren<Animator>();
            }

            CacheAnimatorParameters();
        }

        // Phát chuỗi triệu hồi (lấy đà → nhảy → rơi) khi pet nhảy ra từ pháp trận; falling lặp tới khi PlayLanding.
        public void PlaySummon()
        {
            EnsureAnimator();
            Trigger(hasSummon, SummonHash);
        }

        // Báo pet đã chạm sàn: Animator chuyển falling → landing → roar → idle.
        public void PlayLanding()
        {
            EnsureAnimator();
            Trigger(hasLand, LandHash);
        }

        // Nối sự kiện chiến đấu vào animation.
        private void OnEnable()
        {
            pet.ActionUsed += HandleActionUsed;
            pet.UltimateUsed += HandleUltimate;
            pet.DamageTaken += HandleDamageTaken;
            pet.Defeated += HandleDefeated;
        }

        // Hủy nối sự kiện.
        private void OnDisable()
        {
            pet.ActionUsed -= HandleActionUsed;
            pet.UltimateUsed -= HandleUltimate;
            pet.DamageTaken -= HandleDamageTaken;
            pet.Defeated -= HandleDefeated;
        }

        // Vòng tự đánh: chỉ chạy khi trận đang diễn ra và pet còn sống, không bị choáng.
        private void Update()
        {
            PetCombatant opponent = pet.Opponent;
            if (!pet.IsInMatch || !pet.IsAlive || opponent == null || !opponent.IsAlive || pet.Definition == null)
            {
                SetSpeed(0f);
                movementState = "Idle";
                return;
            }

            if (pet.IsStunned)
            {
                SetSpeed(0f);
                movementState = "Stunned";
                return;
            }

            Vector3 toOpponent = opponent.transform.position - transform.position;
            toOpponent.y = 0f;
            float distanceSqr = toOpponent.sqrMagnitude;
            FaceDirection(toOpponent);

            float range = pet.Definition.attackRange;
            if (distanceSqr > range * range)
            {
                MoveToward(toOpponent, opponent.transform);
                attackTimer = Mathf.Min(attackTimer, 0.2f);
                return;
            }

            SetSpeed(0f);
            movementState = "In range";
            TickBasicAttack(opponent);
        }

        // Đánh thường theo nhịp attacksPerSecond; sát thương là đòn trực tiếp nên chịu DEF và tương khắc.
        private void TickBasicAttack(PetCombatant opponent)
        {
            attackTimer -= Time.deltaTime;
            if (attackTimer > 0f)
            {
                return;
            }

            attackTimer = 1f / pet.Definition.attacksPerSecond;
            pet.DealDirectDamage(opponent, pet.Definition.basicAttackRatio);
            Trigger(hasAttack, AttackHash);
            BasicAttackPerformed?.Invoke(this);
        }

        // Đi về phía đối thủ; thử đường thẳng rồi hai hướng lệch. Mọi điểm đến đều phải qua kiểm tra đấu trường.
        private void MoveToward(Vector3 planarDirection, Transform opponentRoot)
        {
            float speed = pet.Definition.moveSpeed * pet.MoveSpeedMultiplier;
            if (speed <= 0f)
            {
                SetSpeed(0f);
                movementState = "Slowed to 0";
                return;
            }

            Vector3 direction = planarDirection.normalized;
            float step = speed * Time.deltaTime;

            if (TryStep(direction, step, opponentRoot)
                || TryStep(Quaternion.Euler(0f, detourAngle, 0f) * direction, step, opponentRoot)
                || TryStep(Quaternion.Euler(0f, -detourAngle, 0f) * direction, step, opponentRoot))
            {
                SetSpeed(speed);
                return;
            }

            SetSpeed(0f);
            movementState = placement != null ? $"Blocked: {placement.LastRejection}" : "Blocked";
        }

        // Thử di chuyển một bước; chấp nhận nếu điểm mới chiếu được xuống FLOOR và hợp lệ. Không có placement thì đi thẳng (chỉ test).
        private bool TryStep(Vector3 direction, float step, Transform opponentRoot)
        {
            Vector3 target = transform.position + direction * step;
            if (placement == null)
            {
                transform.position = target;
                movementState = "Moving (no arena check)";
                return true;
            }

            if (!placement.TryProjectToFloor(target, out Vector3 floorPoint))
            {
                return false;
            }

            PetDefinition definition = pet.Definition;
            if (placement.Validate(floorPoint, definition.bodyRadius, definition.bodyHeight, transform, opponentRoot) != PlacementResult.Valid)
            {
                return false;
            }

            transform.position = floorPoint;
            movementState = "Moving";
            return true;
        }

        // Xoay dần về hướng mục tiêu trên mặt phẳng ngang.
        private void FaceDirection(Vector3 planarDirection)
        {
            if (planarDirection.sqrMagnitude < 0.0001f)
            {
                return;
            }

            Quaternion target = Quaternion.LookRotation(planarDirection, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, target, turnSpeedDegrees * Time.deltaTime);
        }

        // Ghi nhận tham số Animator có sẵn (duyệt một lần lúc khởi tạo).
        private void CacheAnimatorParameters()
        {
            if (animator == null || animator.runtimeAnimatorController == null)
            {
                return;
            }

            AnimatorControllerParameter[] parameters = animator.parameters;
            for (int i = 0; i < parameters.Length; i++)
            {
                int hash = parameters[i].nameHash;
                hasSpeed |= hash == SpeedHash;
                hasAttack |= hash == AttackHash;
                hasCast |= hash == CastHash;
                hasUltimate |= hash == UltimateHash;
                hasHit |= hash == HitHash;
                hasDie |= hash == DieHash;
                hasSummon |= hash == SummonHash;
                hasLand |= hash == LandHash;
            }
        }

        // Đặt tham số Speed nếu có.
        private void SetSpeed(float value)
        {
            if (hasSpeed)
            {
                animator.SetFloat(SpeedHash, value);
            }
        }

        // Bật trigger nếu tham số tồn tại.
        private void Trigger(bool exists, int hash)
        {
            if (exists)
            {
                animator.SetTrigger(hash);
            }
        }

        // Người chơi kết ấn → animation thi triển.
        private void HandleActionUsed(PetCombatant source, CombatActionType action) => Trigger(hasCast, CastHash);

        // Ultimate → animation tuyệt kỹ.
        private void HandleUltimate(PetCombatant source) => Trigger(hasUltimate, UltimateHash);

        // Trúng đòn (trừ nhịp thiêu đốt để không giật liên tục) → animation bị đánh.
        private void HandleDamageTaken(PetCombatant target, DamageInfo info)
        {
            if (!info.isBurnTick)
            {
                Trigger(hasHit, HitHash);
            }
        }

        // Bị hạ → animation gục.
        private void HandleDefeated(PetCombatant target)
        {
            SetSpeed(0f);
            Trigger(hasDie, DieHash);
        }
    }
}
