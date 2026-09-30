using UnityEngine;

namespace XR124.Combat
{
    // Giữ nhân vật full body (Movement SDK) luôn bám theo camera rig, chạy SAU retargeter mỗi khung hình:
    // 1) dời cả khung xương (bone Root) để đầu nhân vật trùng vị trí mắt/camera; nếu thân lệch hướng nhìn quá xa thì xoay theo;
    // 2) IK hai đốt kéo cổ tay nhân vật tới tay cầm / bàn tay (OVRCameraRig left/rightHandAnchor).
    // Trên kính, body tracking vốn khớp nên phần chỉnh gần như bằng 0; trong XR Simulator (thân chạy chuyển động giả lập)
    // hoặc khi body tracking chưa sẵn sàng, bước này giúp người không "rời" khỏi camera.
    [DefaultExecutionOrder(32000)]
    public sealed class AvatarRigFollower : MonoBehaviour
    {
        [Header("Xương (để trống thì tự tìm theo tên của RealisticCharacter)")]
        [SerializeField] private Transform rootBone;
        [SerializeField] private Transform headBone;
        [SerializeField] private Transform leftUpper, leftLower, leftWrist;
        [SerializeField] private Transform rightUpper, rightLower, rightWrist;

        [Header("Mục tiêu (để trống thì lấy từ OVRCameraRig)")]
        [SerializeField] private Transform head;
        [SerializeField] private Transform leftHandTarget;
        [SerializeField] private Transform rightHandTarget;

        [Header("Căn chỉnh")]
        [Tooltip("Xương Head nằm thấp hơn mắt và lùi sau mắt (m): đặt đầu tại mắt − up*y − forward*z.")]
        [SerializeField] private Vector2 eyeToHeadBone = new Vector2(0.09f, 0.08f);
        [Tooltip("Thân được lệch hướng nhìn tối đa (độ) trước khi bị xoay theo (cho phép vặn cổ tự nhiên).")]
        [Range(0f, 180f)] [SerializeField] private float maxTorsoTwist = 20f;
        [SerializeField] private bool followHead = true;
        [SerializeField] private bool armIk = true;
        [Range(0f, 1f)] [SerializeField] private float armIkWeight = 1f;

        [Header("Runtime (chỉ đọc)")]
        [SerializeField] private float lastHeadCorrection;

        // Tự tìm xương theo tên và lấy camera/anchor tay từ OVRCameraRig nếu chưa gán.
        private void Awake()
        {
            rootBone = rootBone != null ? rootBone : FindBone("Root");
            headBone = headBone != null ? headBone : FindBone("Head");
            leftUpper = leftUpper != null ? leftUpper : FindBone("LeftArmUpper");
            leftLower = leftLower != null ? leftLower : FindBone("LeftArmLower");
            leftWrist = leftWrist != null ? leftWrist : FindBone("LeftHandWrist");
            rightUpper = rightUpper != null ? rightUpper : FindBone("RightArmUpper");
            rightLower = rightLower != null ? rightLower : FindBone("RightArmLower");
            rightWrist = rightWrist != null ? rightWrist : FindBone("RightHandWrist");

            OVRCameraRig rig = FindFirstObjectByType<OVRCameraRig>();
            if (rig != null)
            {
                if (head == null) head = rig.centerEyeAnchor;
                if (leftHandTarget == null) leftHandTarget = rig.leftHandAnchor;
                if (rightHandTarget == null) rightHandTarget = rig.rightHandAnchor;
            }
        }

        // Chạy sau retargeter (execution order cao): căn đầu theo camera rồi IK hai tay.
        private void LateUpdate()
        {
            if (head == null || rootBone == null || headBone == null)
            {
                return;
            }

            if (followHead)
            {
                AlignBodyToHead();
            }

            if (armIk && armIkWeight > 0f)
            {
                Vector3 back = -FlatForward(head);
                SolveArm(leftUpper, leftLower, leftWrist, leftHandTarget, -head.right, back);
                SolveArm(rightUpper, rightLower, rightWrist, rightHandTarget, head.right, back);
            }
        }

        // Xoay thân (quanh đầu) khi hướng ngực lệch hướng nhìn quá maxTorsoTwist, rồi dời cả khung xương để xương Head
        // nằm đúng dưới/sau mắt. Làm trên bone Root nên retargeter vẫn điều khiển tư thế các khớp.
        private void AlignBodyToHead()
        {
            Vector3 lookForward = FlatForward(head);
            if (leftUpper != null && rightUpper != null)
            {
                // Hướng ngực = vuông góc với đường vai trái → vai phải (nhân vật nhìn +Z thì vai phải ở +X: Cross(+X, up) = +Z).
                Vector3 shoulders = rightUpper.position - leftUpper.position;
                Vector3 chestForward = Vector3.ProjectOnPlane(Vector3.Cross(shoulders, Vector3.up), Vector3.up);
                if (chestForward.sqrMagnitude > 0.0001f)
                {
                    float twist = Vector3.SignedAngle(chestForward.normalized, lookForward, Vector3.up);
                    if (Mathf.Abs(twist) > maxTorsoTwist)
                    {
                        float correction = twist - Mathf.Sign(twist) * maxTorsoTwist;
                        rootBone.RotateAround(headBone.position, Vector3.up, correction);
                    }
                }
            }

            Vector3 targetHead = head.position - Vector3.up * eyeToHeadBone.x - lookForward * eyeToHeadBone.y;
            Vector3 delta = targetHead - headBone.position;
            lastHeadCorrection = delta.magnitude;
            rootBone.position += delta;
        }

        // Kéo cổ tay tới tay cầm/bàn tay (trộn theo armIkWeight) bằng IK hai đốt; khuỷu gập xuống dưới, ra ngoài, lùi sau.
        private void SolveArm(Transform upper, Transform lower, Transform wrist, Transform target, Vector3 outward, Vector3 back)
        {
            if (wrist == null || target == null)
            {
                return;
            }

            Vector3 goal = Vector3.Lerp(wrist.position, target.position, armIkWeight);
            TwoBoneIk.Solve(upper, lower, wrist, goal, (Vector3.down * 0.6f + outward * 0.3f + back * 0.3f).normalized);
        }

        // Hướng nhìn chiếu lên mặt phẳng ngang (mặc định +Z khi nhìn thẳng lên/xuống).
        private static Vector3 FlatForward(Transform t)
        {
            Vector3 f = Vector3.ProjectOnPlane(t.forward, Vector3.up);
            return f.sqrMagnitude > 0.0001f ? f.normalized : Vector3.forward;
        }

        // Tìm xương con theo tên chính xác trong nhân vật.
        private Transform FindBone(string boneName)
        {
            Transform[] all = GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i].name == boneName)
                {
                    return all[i];
                }
            }

            return null;
        }
    }
}
