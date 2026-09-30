using System.Collections.Generic;
using UnityEngine;

namespace XR124.Combat
{
    public enum FingerState : byte
    {
        Unknown,
        Extended,
        Neutral,
        Curled
    }

    // Đọc một bàn tay từ OVRSkeleton: độ duỗi 5 ngón, tâm lòng bàn tay, cổ tay và trạng thái nắm tay.
    // BoneId được chọn theo SkeletonType thực tế lúc chạy: XRHand_* cho XRHandLeft/XRHandRight, Hand_* cho HandLeft/HandRight.
    public sealed class HandSkeletonReader : MonoBehaviour
    {
        public const int FingerCount = 5;
        public const int Thumb = 0;
        public const int Index = 1;
        public const int Middle = 2;
        public const int Ring = 3;
        public const int Little = 4;

        [SerializeField] private OVRSkeleton skeleton;

        [Header("Ngưỡng độ duỗi (khoảng cách gốc–đầu / tổng chiều dài đốt)")]
        [Range(0.5f, 1f)] [SerializeField] private float fingerExtendedRatio = 0.82f;
        [Range(0.2f, 0.95f)] [SerializeField] private float fingerCurledRatio = 0.68f;
        [Tooltip("Ngón cái ít gập hơn các ngón khác nên cần ngưỡng riêng; cần chỉnh khi test trên kính.")]
        [Range(0.5f, 1f)] [SerializeField] private float thumbExtendedRatio = 0.9f;
        [Range(0.2f, 0.99f)] [SerializeField] private float thumbCurledRatio = 0.8f;
        [Min(0f)] [SerializeField] private float trackingWarmupSeconds = 0.3f;

        [Header("Runtime (chỉ đọc)")]
        [SerializeField] private OVRSkeleton.SkeletonType resolvedType = OVRSkeleton.SkeletonType.None;
        [SerializeField] private bool isValid;
        [SerializeField] private float[] straightness = new float[FingerCount];
        [SerializeField] private FingerState[] fingerStates = new FingerState[FingerCount];

        private readonly Dictionary<OVRSkeleton.BoneId, Transform> boneCache = new Dictionary<OVRSkeleton.BoneId, Transform>(32);
        // Mỗi ngón 4 điểm theo thứ tự gốc → đầu ngón; ngón cái dùng Metacarpal/Proximal/Distal/Tip.
        private readonly OVRSkeleton.BoneId[,] fingerBones = new OVRSkeleton.BoneId[FingerCount, 4];
        private OVRSkeleton.BoneId wristId;
        private bool idsResolved;
        private int cachedBoneCount;
        private int lastRefreshFrame = -1;
        private float trackingValidSince = -1f;
        private Vector3 palmCenter;
        private Vector3 palmNormal;
        private Vector3 indexDirection;
        private Vector3 indexTip;
        private bool isLeftHand;
        private Transform wrist;

        public OVRSkeleton Skeleton => skeleton;
        public bool IsValid { get { Refresh(); return isValid; } }
        public Vector3 PalmCenter { get { Refresh(); return palmCenter; } }
        public Transform Wrist { get { Refresh(); return wrist; } }
        // Pháp tuyến lòng bàn tay (hướng mặt lòng bàn tay nhìn ra), đã chuẩn hóa.
        public Vector3 PalmNormal { get { Refresh(); return palmNormal; } }
        // Hướng ngón trỏ từ gốc tới đầu ngón, đã chuẩn hóa; dùng cho ấn Kiếm Chỉ chỉ vào địch.
        public Vector3 IndexDirection { get { Refresh(); return indexDirection; } }
        // Vị trí đầu ngón trỏ (world); dùng để chạm đồng hồ túi đồ khi chơi bằng tay không.
        public Vector3 IndexTip { get { Refresh(); return indexTip; } }
        public bool IsLeftHand => isLeftHand;

        // Gán skeleton từ công cụ Editor hoặc code khởi tạo.
        public void Configure(OVRSkeleton handSkeleton)
        {
            skeleton = handSkeleton;
            idsResolved = false;
        }

        // Trạng thái một ngón (Extended / Neutral / Curled); Unknown khi mất tracking.
        public FingerState GetFingerState(int finger)
        {
            Refresh();
            return isValid ? fingerStates[finger] : FingerState.Unknown;
        }

        // Nắm tay: bốn ngón dài đều gập; ngón cái bỏ qua vì tư thế nắm của mỗi người khác nhau.
        public bool IsFist
        {
            get
            {
                Refresh();
                return isValid
                       && fingerStates[Index] == FingerState.Curled
                       && fingerStates[Middle] == FingerState.Curled
                       && fingerStates[Ring] == FingerState.Curled
                       && fingerStates[Little] == FingerState.Curled;
            }
        }

        // Tính lại dữ liệu tối đa một lần mỗi khung hình dù nhiều hệ thống cùng đọc.
        private void Refresh()
        {
            if (lastRefreshFrame == Time.frameCount)
            {
                return;
            }

            lastRefreshFrame = Time.frameCount;
            isValid = false;

            if (skeleton == null || !skeleton.IsDataValid || skeleton.Bones == null || skeleton.Bones.Count == 0)
            {
                trackingValidSince = -1f;
                return;
            }

            // Chờ vài phần giây sau khi có tracking để bỏ các khung hình đầu bị nhiễu.
            if (trackingValidSince < 0f)
            {
                trackingValidSince = Time.unscaledTime;
            }

            if (Time.unscaledTime - trackingValidSince < trackingWarmupSeconds)
            {
                return;
            }

            OVRSkeleton.SkeletonType type = skeleton.GetSkeletonType();
            if (!idsResolved || type != resolvedType)
            {
                ResolveBoneIds(type);
                cachedBoneCount = -1;
            }

            if (!idsResolved)
            {
                return;
            }

            if (cachedBoneCount != skeleton.Bones.Count)
            {
                RebuildBoneCache();
            }

            if (!boneCache.TryGetValue(wristId, out wrist) || wrist == null)
            {
                return;
            }

            Vector3 wristPosition = wrist.position;
            Vector3 palmSum = wristPosition;
            Vector3 indexRoot = Vector3.zero;
            Vector3 littleRoot = Vector3.zero;
            for (int finger = 0; finger < FingerCount; finger++)
            {
                if (!TryMeasureFinger(finger, out float ratio, out Vector3 root, out Vector3 tip))
                {
                    return;
                }

                straightness[finger] = ratio;
                fingerStates[finger] = Classify(finger, ratio);
                if (finger != Thumb)
                {
                    palmSum += root;
                }

                if (finger == Index)
                {
                    indexRoot = root;
                    indexTip = tip;
                    indexDirection = (tip - root).normalized;
                }
                else if (finger == Little)
                {
                    littleRoot = root;
                }
            }

            // Tâm lòng bàn tay = trung bình cổ tay và gốc 4 ngón dài, ổn định hơn một khớp đơn lẻ.
            palmCenter = palmSum * 0.2f;

            // Pháp tuyến lòng bàn tay từ mặt phẳng cổ tay–gốc ngón trỏ–gốc ngón út. Với tay trái tích có hướng đã chỉ ra
            // phía lòng bàn tay; tay phải là ảnh gương nên phải đảo dấu (kiểm chứng: hai tay úp xuống đều cho −Y).
            Vector3 cross = Vector3.Cross(indexRoot - wristPosition, littleRoot - wristPosition);
            if (cross.sqrMagnitude < 1e-10f)
            {
                return;
            }

            palmNormal = (isLeftHand ? cross : -cross).normalized;
            isValid = true;
        }

        // Phân loại độ duỗi theo ngưỡng; ngón cái dùng ngưỡng riêng.
        private FingerState Classify(int finger, float ratio)
        {
            float extended = finger == Thumb ? thumbExtendedRatio : fingerExtendedRatio;
            float curled = finger == Thumb ? thumbCurledRatio : fingerCurledRatio;
            if (ratio >= extended)
            {
                return FingerState.Extended;
            }

            return ratio <= curled ? FingerState.Curled : FingerState.Neutral;
        }

        // Độ duỗi = khoảng cách gốc → đầu ngón / tổng chiều dài 3 đốt (1 = thẳng hoàn toàn); trả thêm vị trí gốc và đầu ngón.
        private bool TryMeasureFinger(int finger, out float ratio, out Vector3 root, out Vector3 tip)
        {
            ratio = 0f;
            root = Vector3.zero;
            tip = Vector3.zero;
            if (!boneCache.TryGetValue(fingerBones[finger, 0], out Transform a)
                || !boneCache.TryGetValue(fingerBones[finger, 1], out Transform b)
                || !boneCache.TryGetValue(fingerBones[finger, 2], out Transform c)
                || !boneCache.TryGetValue(fingerBones[finger, 3], out Transform d))
            {
                return false;
            }

            Vector3 pa = a.position;
            Vector3 pb = b.position;
            Vector3 pc = c.position;
            Vector3 pd = d.position;
            float chain = (pb - pa).magnitude + (pc - pb).magnitude + (pd - pc).magnitude;
            if (chain <= Mathf.Epsilon)
            {
                return false;
            }

            ratio = (pd - pa).magnitude / chain;
            root = pa;
            tip = pd;
            return true;
        }

        // Xây lại bảng BoneId → Transform khi danh sách xương thay đổi (khởi tạo hoặc đổi loại skeleton).
        private void RebuildBoneCache()
        {
            boneCache.Clear();
            IList<OVRBone> bones = skeleton.Bones;
            for (int i = 0; i < bones.Count; i++)
            {
                boneCache[bones[i].Id] = bones[i].Transform;
            }

            cachedBoneCount = bones.Count;
        }

        // Chọn BoneId theo SkeletonType runtime. Hai layout có giá trị số trùng nhau nhưng trỏ tới khớp khác nhau,
        // nên tuyệt đối không dùng lẫn XRHand_* với Hand_* (quy tắc dự án AGENTS.md).
        private void ResolveBoneIds(OVRSkeleton.SkeletonType type)
        {
            resolvedType = type;
            idsResolved = false;
            isLeftHand = type == OVRSkeleton.SkeletonType.XRHandLeft || type == OVRSkeleton.SkeletonType.HandLeft;

            if (type == OVRSkeleton.SkeletonType.XRHandLeft || type == OVRSkeleton.SkeletonType.XRHandRight)
            {
                wristId = OVRSkeleton.BoneId.XRHand_Wrist;
                SetFinger(Thumb, OVRSkeleton.BoneId.XRHand_ThumbMetacarpal, OVRSkeleton.BoneId.XRHand_ThumbProximal,
                    OVRSkeleton.BoneId.XRHand_ThumbDistal, OVRSkeleton.BoneId.XRHand_ThumbTip);
                SetFinger(Index, OVRSkeleton.BoneId.XRHand_IndexProximal, OVRSkeleton.BoneId.XRHand_IndexIntermediate,
                    OVRSkeleton.BoneId.XRHand_IndexDistal, OVRSkeleton.BoneId.XRHand_IndexTip);
                SetFinger(Middle, OVRSkeleton.BoneId.XRHand_MiddleProximal, OVRSkeleton.BoneId.XRHand_MiddleIntermediate,
                    OVRSkeleton.BoneId.XRHand_MiddleDistal, OVRSkeleton.BoneId.XRHand_MiddleTip);
                SetFinger(Ring, OVRSkeleton.BoneId.XRHand_RingProximal, OVRSkeleton.BoneId.XRHand_RingIntermediate,
                    OVRSkeleton.BoneId.XRHand_RingDistal, OVRSkeleton.BoneId.XRHand_RingTip);
                SetFinger(Little, OVRSkeleton.BoneId.XRHand_LittleProximal, OVRSkeleton.BoneId.XRHand_LittleIntermediate,
                    OVRSkeleton.BoneId.XRHand_LittleDistal, OVRSkeleton.BoneId.XRHand_LittleTip);
                idsResolved = true;
                return;
            }

            if (type == OVRSkeleton.SkeletonType.HandLeft || type == OVRSkeleton.SkeletonType.HandRight)
            {
                wristId = OVRSkeleton.BoneId.Hand_WristRoot;
                SetFinger(Thumb, OVRSkeleton.BoneId.Hand_Thumb1, OVRSkeleton.BoneId.Hand_Thumb2,
                    OVRSkeleton.BoneId.Hand_Thumb3, OVRSkeleton.BoneId.Hand_ThumbTip);
                SetFinger(Index, OVRSkeleton.BoneId.Hand_Index1, OVRSkeleton.BoneId.Hand_Index2,
                    OVRSkeleton.BoneId.Hand_Index3, OVRSkeleton.BoneId.Hand_IndexTip);
                SetFinger(Middle, OVRSkeleton.BoneId.Hand_Middle1, OVRSkeleton.BoneId.Hand_Middle2,
                    OVRSkeleton.BoneId.Hand_Middle3, OVRSkeleton.BoneId.Hand_MiddleTip);
                SetFinger(Ring, OVRSkeleton.BoneId.Hand_Ring1, OVRSkeleton.BoneId.Hand_Ring2,
                    OVRSkeleton.BoneId.Hand_Ring3, OVRSkeleton.BoneId.Hand_RingTip);
                SetFinger(Little, OVRSkeleton.BoneId.Hand_Pinky1, OVRSkeleton.BoneId.Hand_Pinky2,
                    OVRSkeleton.BoneId.Hand_Pinky3, OVRSkeleton.BoneId.Hand_PinkyTip);
                idsResolved = true;
            }
        }

        // Ghi 4 BoneId của một ngón theo thứ tự gốc → đầu ngón.
        private void SetFinger(int finger, OVRSkeleton.BoneId a, OVRSkeleton.BoneId b, OVRSkeleton.BoneId c, OVRSkeleton.BoneId d)
        {
            fingerBones[finger, 0] = a;
            fingerBones[finger, 1] = b;
            fingerBones[finger, 2] = c;
            fingerBones[finger, 3] = d;
        }
    }
}
