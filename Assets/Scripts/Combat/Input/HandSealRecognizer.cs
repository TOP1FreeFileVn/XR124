using System;
using UnityEngine;

namespace XR124.Combat
{
    public enum FingerRequirement : byte
    {
        Any,
        Extended,
        Curled
    }

    // Hướng lòng bàn tay so với đầu người chơi (lấy hướng nhìn nằm ngang).
    public enum PalmFacing : byte
    {
        Any,
        Forward,
        TowardSelf,
        Up
    }

    // Vùng đặt tay so với đầu người chơi.
    public enum HandZone : byte
    {
        Any,
        Front,
        Chest
    }

    // Mô tả tư thế một ấn kí: yêu cầu từng ngón + hướng lòng bàn tay + vùng tay + có phải chỉ vào mục tiêu không.
    [Serializable]
    public struct SealPose
    {
        public SealType seal;
        public FingerRequirement thumb;
        public FingerRequirement index;
        public FingerRequirement middle;
        public FingerRequirement ring;
        public FingerRequirement little;
        public PalmFacing palmFacing;
        public HandZone zone;
        [Tooltip("Ngón trỏ phải chỉ về mục tiêu (địch; chưa có địch thì theo hướng nhìn).")]
        public bool pointAtTarget;
    }

    // Nhận diện ấn kí trên một bàn tay: tư thế phải giữ đủ holdSeconds mới tính, và phải buông ra rồi mới tính lại
    // cùng ấn đó (để A-A cần kết A hai lần).
    // Bộ ấn mặc định: A = Kiếm Chỉ (trỏ + giữa chỉ vào địch), D = Thuẫn Chưởng (xòe tay, lòng bàn tay hướng ra trước mặt),
    // H = Tâm Ấn (xòe tay áp lên ngực, lòng bàn tay hướng vào người). Nắm tay dành cho cầm kiếm, ngửa tay trái dành cho HUD.
    public sealed class HandSealRecognizer : MonoBehaviour
    {
        // Tăng khi đổi bộ ấn mặc định để scene cũ tự nâng cấp tư thế đã lưu.
        private const int CurrentPosesVersion = 2;

        [SerializeField] private HandSkeletonReader reader;
        [Min(0f)] [SerializeField] private float holdSeconds = 0.25f;

        [Header("Ngưỡng hướng / vùng (chỉnh khi test trên kính)")]
        [Tooltip("Cos góc tối đa giữa lòng bàn tay và hướng yêu cầu (0.6 ≈ 53°).")]
        [Range(0f, 1f)] [SerializeField] private float facingCosThreshold = 0.6f;
        [Tooltip("Cos góc tối đa giữa ngón trỏ và hướng tới mục tiêu (0.8 ≈ 37°).")]
        [Range(0f, 1f)] [SerializeField] private float aimCosThreshold = 0.8f;
        [Tooltip("Tay phải cách đầu ít nhất đoạn này theo hướng nhìn để tính là 'trước mặt' (m).")]
        [Min(0f)] [SerializeField] private float frontMinDistance = 0.25f;
        [Tooltip("Điểm ngực = đầu hạ xuống đoạn này (m).")]
        [Min(0f)] [SerializeField] private float chestDrop = 0.3f;
        [Tooltip("Điểm ngực = đầu tiến ra trước đoạn này (m).")]
        [Min(0f)] [SerializeField] private float chestForward = 0.1f;
        [Tooltip("Lòng bàn tay cách điểm ngực tối đa đoạn này để tính là 'áp ngực' (m).")]
        [Min(0.02f)] [SerializeField] private float chestRadius = 0.2f;

        [SerializeField] private SealPose[] poses = BuildDefaultPoses();
        // Khởi tạo 0: scene cũ chưa lưu field này sẽ đọc ra 0 và được nâng cấp ở OnValidate/Awake.
        [SerializeField, HideInInspector] private int posesVersion;

        [Header("Runtime (chỉ đọc)")]
        [SerializeField] private SealType candidate;
        [SerializeField] private SealType heldSeal;
        [SerializeField] private bool suppressed;
        [SerializeField] private float debugForwardDot;
        [SerializeField] private float debugSelfDot;
        [SerializeField] private float debugUpDot;
        [SerializeField] private float debugFrontDistance;
        [SerializeField] private float debugChestDistance;
        [SerializeField] private float debugAimDot;

        private float candidateTime;
        private Transform head;

        public event Action<HandSealRecognizer, SealType> SealPerformed;

        // Ấn kí đang được giữ sau khi đã được tính (None nếu không có).
        public SealType HeldSeal => heldSeal;
        public HandSkeletonReader Reader => reader;
        // Mục tiêu để ấn Kiếm Chỉ chỉ vào; SealComboCaster gán thành pet địch khi đang đánh.
        public Transform AimTarget { get; set; }

        // Tạo bộ 3 ấn mặc định theo thiết kế đã chốt.
        public static SealPose[] BuildDefaultPoses()
        {
            return new[]
            {
                // A – Kiếm Chỉ: trỏ + giữa duỗi, áp út + út gập, ngón trỏ chỉ vào địch.
                new SealPose { seal = SealType.A, index = FingerRequirement.Extended, middle = FingerRequirement.Extended,
                    ring = FingerRequirement.Curled, little = FingerRequirement.Curled, pointAtTarget = true },
                // D – Thuẫn Chưởng: xòe 4 ngón, lòng bàn tay hướng ra trước, tay đưa ra trước mặt.
                new SealPose { seal = SealType.D, index = FingerRequirement.Extended, middle = FingerRequirement.Extended,
                    ring = FingerRequirement.Extended, little = FingerRequirement.Extended, palmFacing = PalmFacing.Forward, zone = HandZone.Front },
                // H – Tâm Ấn: xòe 4 ngón, lòng bàn tay hướng vào người, áp lên ngực.
                new SealPose { seal = SealType.H, index = FingerRequirement.Extended, middle = FingerRequirement.Extended,
                    ring = FingerRequirement.Extended, little = FingerRequirement.Extended, palmFacing = PalmFacing.TowardSelf, zone = HandZone.Chest }
            };
        }

        // Gán bộ đọc tay từ công cụ Editor.
        public void Configure(HandSkeletonReader handReader)
        {
            reader = handReader;
        }

        // Nâng cấp bộ ấn đã lưu trong scene cũ lên bộ mặc định mới khi phiên bản thay đổi.
        private void OnValidate()
        {
            UpgradePosesIfNeeded();
        }

        // Đảm bảo bộ ấn mới cả khi chạy build (OnValidate chỉ chạy trong Editor) và cache camera.
        private void Awake()
        {
            UpgradePosesIfNeeded();
            CacheHead();
        }

        // Thay bộ ấn cũ bằng bộ mặc định mới nếu phiên bản lưu thấp hơn phiên bản hiện tại.
        private void UpgradePosesIfNeeded()
        {
            if (posesVersion >= CurrentPosesVersion && poses != null && poses.Length > 0)
            {
                return;
            }

            poses = BuildDefaultPoses();
            posesVersion = CurrentPosesVersion;
        }

        // Lấy transform camera chính làm vị trí đầu người chơi.
        private void CacheHead()
        {
            if (Camera.main != null)
            {
                head = Camera.main.transform;
            }
        }

        // Tạm tắt nhận diện (ví dụ tay đang cầm kiếm) và xóa trạng thái đang giữ.
        public void SetSuppressed(bool value)
        {
            suppressed = value;
            if (value)
            {
                ClearState();
            }
        }

        // Mỗi khung hình: tìm ấn khớp, đếm thời gian giữ, phát sự kiện đúng một lần khi giữ đủ lâu.
        private void Update()
        {
            if (head == null)
            {
                CacheHead();
            }

            if (suppressed || reader == null || head == null || !reader.IsValid)
            {
                ClearState();
                return;
            }

            UpdateSpatialDebug();
            SealType matched = Match();
            if (matched != candidate)
            {
                candidate = matched;
                candidateTime = 0f;
                // Đổi tư thế thì bỏ chốt ấn cũ để có thể kết lại ấn đó sau này.
                heldSeal = SealType.None;
                return;
            }

            if (candidate == SealType.None || heldSeal != SealType.None)
            {
                return;
            }

            candidateTime += Time.deltaTime;
            if (candidateTime >= holdSeconds)
            {
                heldSeal = candidate;
                SealPerformed?.Invoke(this, heldSeal);
            }
        }

        // Tính một lần mỗi khung hình các giá trị hướng/vùng dùng cho so khớp; lưu vào field để xem trong Inspector khi chỉnh ngưỡng.
        private void UpdateSpatialDebug()
        {
            Vector3 forward = GetFlatForward();
            Vector3 palmNormal = reader.PalmNormal;
            Vector3 palm = reader.PalmCenter;
            Vector3 headPosition = head.position;

            debugForwardDot = Vector3.Dot(palmNormal, forward);
            debugSelfDot = -debugForwardDot;
            debugUpDot = palmNormal.y;
            debugFrontDistance = Vector3.Dot(palm - headPosition, forward);

            Vector3 chest = headPosition + Vector3.down * chestDrop + forward * chestForward;
            debugChestDistance = (palm - chest).magnitude;

            Vector3 aimPoint = AimTarget != null ? AimTarget.position + Vector3.up * 0.3f : headPosition + forward * 3f;
            Vector3 toTarget = aimPoint - reader.PalmCenter;
            debugAimDot = toTarget.sqrMagnitude > 0.0001f ? Vector3.Dot(reader.IndexDirection, toTarget.normalized) : 0f;
        }

        // Trả về ấn đầu tiên trong danh sách khớp đủ mọi điều kiện.
        private SealType Match()
        {
            for (int i = 0; i < poses.Length; i++)
            {
                ref readonly SealPose pose = ref poses[i];
                if (Satisfies(HandSkeletonReader.Thumb, pose.thumb)
                    && Satisfies(HandSkeletonReader.Index, pose.index)
                    && Satisfies(HandSkeletonReader.Middle, pose.middle)
                    && Satisfies(HandSkeletonReader.Ring, pose.ring)
                    && Satisfies(HandSkeletonReader.Little, pose.little)
                    && SatisfiesFacing(pose.palmFacing)
                    && SatisfiesZone(pose.zone)
                    && (!pose.pointAtTarget || debugAimDot >= aimCosThreshold))
                {
                    return pose.seal;
                }
            }

            return SealType.None;
        }

        // Kiểm tra một ngón có đạt yêu cầu không; trạng thái Neutral không đạt cả Extended lẫn Curled.
        private bool Satisfies(int finger, FingerRequirement requirement)
        {
            switch (requirement)
            {
                case FingerRequirement.Extended:
                    return reader.GetFingerState(finger) == FingerState.Extended;
                case FingerRequirement.Curled:
                    return reader.GetFingerState(finger) == FingerState.Curled;
                default:
                    return true;
            }
        }

        // Hướng lòng bàn tay: Forward = cùng hướng nhìn, TowardSelf = ngược hướng nhìn, Up = hướng lên trời.
        private bool SatisfiesFacing(PalmFacing facing)
        {
            switch (facing)
            {
                case PalmFacing.Forward:
                    return debugForwardDot >= facingCosThreshold;
                case PalmFacing.TowardSelf:
                    return debugSelfDot >= facingCosThreshold;
                case PalmFacing.Up:
                    return debugUpDot >= facingCosThreshold;
                default:
                    return true;
            }
        }

        // Vùng tay: Front = đưa ra trước mặt đủ xa, Chest = lòng bàn tay nằm gần điểm ngực.
        private bool SatisfiesZone(HandZone zone)
        {
            switch (zone)
            {
                case HandZone.Front:
                    return debugFrontDistance >= frontMinDistance;
                case HandZone.Chest:
                    return debugChestDistance <= chestRadius;
                default:
                    return true;
            }
        }

        // Hướng nhìn của đầu chiếu lên mặt phẳng ngang, để cúi/ngẩng đầu không làm lệch phán đoán hướng tay.
        private Vector3 GetFlatForward()
        {
            Vector3 forward = Vector3.ProjectOnPlane(head.forward, Vector3.up);
            return forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.forward;
        }

        // Xóa ấn đang theo dõi.
        private void ClearState()
        {
            candidate = SealType.None;
            heldSeal = SealType.None;
            candidateTime = 0f;
        }
    }
}
