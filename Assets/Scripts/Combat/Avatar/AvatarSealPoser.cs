using UnityEngine;

namespace XR124.Combat
{
    // Cho nhân vật full body "diễn" ấn kí mỗi khi kết ấn (tay cầm không tạo được tư thế ngón riêng, Simulator chỉ có thân giả lập):
    //   A – Kiếm Chỉ: tay phải duỗi trỏ + giữa, gập áp út/út/cái, chỉ vào quái.
    //   D – Thuẫn Chưởng: tay phải xòe, đẩy lòng bàn tay ra trước.
    //   H – Tâm Ấn: tay trái xòe, áp lòng bàn tay lên ngực.
    //   Ultimate: hai tay Kiếm Chỉ chỉ vào quái.
    // Chạy sau AvatarRigFollower; tư thế được trộn (localRotation) lên trên dữ liệu tracking rồi nhả dần sau holdSeconds.
    [DefaultExecutionOrder(32100)]
    public sealed class AvatarSealPoser : MonoBehaviour
    {
        private enum Pose : byte { None, SwordFinger, PalmForward, PalmOnChest }

        private const int Thumb = 0, Index = 1, Middle = 2, Ring = 3, Pinky = 4;

        // Tên đốt ngón theo khung xương RealisticCharacter (Movement SDK); đốt cuối (Tip) chỉ dùng để lấy hướng.
        private static readonly string[][] FingerBones =
        {
            new[] { "ThumbMeta", "ThumbProximal", "ThumbDistal", "ThumbTip" },
            new[] { "IndexProximal", "IndexIntermediate", "IndexDistal", "IndexTip" },
            new[] { "MiddleProximal", "MiddleIntermediate", "MiddleDistal", "MiddleTip" },
            new[] { "RingProximal", "RingIntermediate", "RingDistal", "RingTip" },
            new[] { "PinkyProximal", "PinkyIntermediate", "PinkyDistal", "PinkyTip" }
        };

        [SerializeField] private SealComboCaster caster;
        [SerializeField] private BattleSummoner summoner;
        [Tooltip("Thời gian giữ tư thế ấn (giây) trước khi nhả về tư thế tracking.")]
        [Min(0.1f)] [SerializeField] private float holdSeconds = 0.8f;
        [Tooltip("Thời gian chuyển vào/ra tư thế (giây).")]
        [Min(0.01f)] [SerializeField] private float blendSeconds = 0.12f;
        [Tooltip("Pháp tuyến lòng bàn tay = dấu × Cross(gốc trỏ − cổ tay, gốc út − cổ tay); đo ở tư thế bind của nhân vật.")]
        [SerializeField] private float leftPalmSign = 1f;
        [SerializeField] private float rightPalmSign = -1f;

        private sealed class HandRig
        {
            public bool isLeft;
            public Transform upper, lower, wrist;
            public Transform[][] fingers;
            public Transform[] all;
            public Quaternion[] saved;
            public Pose pose;
            public float timer;
            public float weight;
        }

        [Header("Cầm kiếm")]
        [Tooltip("Kiếm triệu hồi; để trống thì tự tìm. Khi kiếm đang trong tay, bàn tay phải nắm quanh chuôi.")]
        [SerializeField] private SummonSword sword;
        [Min(0.01f)] [SerializeField] private float gripBlendSeconds = 0.15f;
        [Tooltip("Khoảng từ đường khớp gốc ngón tới trục chuôi theo pháp tuyến lòng bàn tay (m) – chuôi áp sát lòng bàn tay.")]
        [Min(0f)] [SerializeField] private float gripDepth = 0.015f;
        [Tooltip("Dời trục chuôi từ đường khớp gốc ngón về phía đầu ngón (m, âm = về phía cổ tay) để chuôi nằm giữa vòng nắm.")]
        [SerializeField] private float gripForward = -0.005f;

        private HandRig left;
        private HandRig right;
        private Transform head;
        private Transform swordGrip;
        private float gripWeight;

        // Tìm xương hai tay, camera và nối sự kiện kết ấn / Ultimate.
        private void Start()
        {
            left = BuildHand("Left", true);
            right = BuildHand("Right", false);
            OVRCameraRig rig = FindFirstObjectByType<OVRCameraRig>();
            head = rig != null ? rig.centerEyeAnchor : (Camera.main != null ? Camera.main.transform : null);
            if (caster == null) caster = FindFirstObjectByType<SealComboCaster>();
            if (summoner == null) summoner = FindFirstObjectByType<BattleSummoner>();
            if (sword == null) sword = FindFirstObjectByType<SummonSword>(FindObjectsInactive.Include);
            swordGrip = sword != null ? sword.transform.Find("Grip") : null;
            if (caster != null)
            {
                caster.SealQueued += HandleSeal;
                caster.UltimateCast += HandleUltimate;
            }
        }

        // Hủy nối sự kiện.
        private void OnDestroy()
        {
            if (caster != null)
            {
                caster.SealQueued -= HandleSeal;
                caster.UltimateCast -= HandleUltimate;
            }
        }

        // Ấn A/D dùng tay phải (nút A/B trên tay cầm phải), ấn H dùng tay trái (nút X trên tay cầm trái).
        private void HandleSeal(SealType seal)
        {
            switch (seal)
            {
                case SealType.A: Play(right, Pose.SwordFinger); break;
                case SealType.D: Play(right, Pose.PalmForward); break;
                case SealType.H: Play(left, Pose.PalmOnChest); break;
            }
        }

        // Ultimate: hai tay cùng Kiếm Chỉ.
        private void HandleUltimate(ActionResult result)
        {
            Play(left, Pose.SwordFinger);
            Play(right, Pose.SwordFinger);
        }

        private void Play(HandRig hand, Pose pose)
        {
            if (hand == null)
            {
                return;
            }

            hand.pose = pose;
            hand.timer = holdSeconds;
        }

        // Mỗi khung hình sau tracking/IK: đếm giờ, cập nhật trọng số trộn và áp tư thế ấn cho tay đang diễn.
        private void LateUpdate()
        {
            if (head == null)
            {
                return;
            }

            Animate(left);
            Animate(right);
            AnimateGrip();
        }

        // Kiếm đang trong tay (và tay phải không đang diễn ấn) → trộn dần tư thế nắm chuôi kiếm; rời tay → nhả dần.
        private void AnimateGrip()
        {
            if (right == null || sword == null || swordGrip == null)
            {
                return;
            }

            bool holding = sword.IsEquipped && sword.gameObject.activeInHierarchy && right.weight <= 0f;
            gripWeight = Mathf.MoveTowards(gripWeight, holding ? 1f : 0f, Time.deltaTime / gripBlendSeconds);
            if (gripWeight <= 0f)
            {
                return;
            }

            for (int i = 0; i < right.all.Length; i++)
            {
                right.saved[i] = right.all[i].localRotation;
            }

            GripSword(right);

            for (int i = 0; i < right.all.Length; i++)
            {
                right.all[i].localRotation = Quaternion.Slerp(right.saved[i], right.all[i].localRotation, gripWeight);
            }
        }

        // Nắm kiếm như người thật: chuôi nằm ngang lòng bàn tay (trục gốc út → gốc trỏ trùng hướng lưỡi, lưỡi ra phía ngón cái),
        // mu các đốt ngón hướng về phía sống/lưỡi đối diện; IK cánh tay để tâm nắm tay trùng điểm Grip của kiếm (lặp 2 lần
        // vì IK làm đổi hướng cổ tay), rồi gập 4 ngón và quấn ngón cái quanh chuôi.
        private void GripSword(HandRig hand)
        {
            Vector3 bladeDir = -sword.transform.up;
            Vector3 fingerDir = Vector3.ProjectOnPlane(-sword.transform.forward, bladeDir);
            fingerDir = fingerDir.sqrMagnitude > 1e-6f ? fingerDir.normalized : Vector3.ProjectOnPlane(Vector3.down, bladeDir).normalized;
            Vector3 outward = Vector3.Cross(Vector3.up, Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized);

            for (int iteration = 0; iteration < 2; iteration++)
            {
                OrientWristAlongHandle(hand, fingerDir, bladeDir);
                Vector3 fistOffset = FistCenter(hand) - hand.wrist.position;
                TwoBoneIk.Solve(hand.upper, hand.lower, hand.wrist, swordGrip.position - fistOffset, (Vector3.down * 0.7f + outward * 0.3f).normalized);
            }

            OrientWristAlongHandle(hand, fingerDir, bladeDir);
            // Nắm chặt quanh chuôi (ngón út gập sâu nhất như nắm kiếm thật); ngón cái gập vòng qua chuôi, đè lên ngón trỏ/giữa.
            CurlFinger(hand, Index, 75f, 95f, 60f);
            CurlFinger(hand, Middle, 80f, 95f, 60f);
            CurlFinger(hand, Ring, 90f, 100f, 65f);
            CurlFinger(hand, Pinky, 95f, 100f, 65f);
            CurlFinger(hand, Thumb, 40f, 45f, 35f);
        }

        // Xoay cổ tay: hướng bàn tay (cổ tay → gốc ngón giữa) về fingerDir, rồi vặn quanh trục đó để trục gốc út → gốc trỏ
        // trùng hướng lưỡi kiếm (chuôi nằm ngang lòng bàn tay, lưỡi đi ra phía ngón trỏ/ngón cái).
        private void OrientWristAlongHandle(HandRig hand, Vector3 fingerDir, Vector3 bladeDir)
        {
            hand.wrist.rotation = Quaternion.FromToRotation(HandForward(hand), fingerDir) * hand.wrist.rotation;
            Transform index = hand.fingers[Index] != null ? hand.fingers[Index][0] : null;
            Transform pinky = hand.fingers[Pinky] != null ? hand.fingers[Pinky][0] : null;
            if (index == null || pinky == null)
            {
                return;
            }

            Vector3 lateral = index.position - pinky.position;
            float twist = Vector3.SignedAngle(Vector3.ProjectOnPlane(lateral, fingerDir), Vector3.ProjectOnPlane(bladeDir, fingerDir), fingerDir);
            hand.wrist.rotation = Quaternion.AngleAxis(twist, fingerDir) * hand.wrist.rotation;
        }

        // Tâm nắm tay (nơi trục chuôi đi qua): giữa gốc ngón trỏ và gốc ngón út, áp vào phía lòng bàn tay gripDepth và dời về
        // phía đầu ngón gripForward – vùng mà các đốt ngón gập lại sẽ ôm quanh.
        private Vector3 FistCenter(HandRig hand)
        {
            Transform index = hand.fingers[Index] != null ? hand.fingers[Index][0] : null;
            Transform pinky = hand.fingers[Pinky] != null ? hand.fingers[Pinky][0] : null;
            if (index == null || pinky == null)
            {
                return hand.wrist.position;
            }

            return (index.position + pinky.position) * 0.5f + PalmNormal(hand) * gripDepth + HandForward(hand) * gripForward;
        }

        // Lưu localRotation hiện tại, dựng tư thế ấn trên đó, rồi trộn về theo weight để vào/ra mượt.
        private void Animate(HandRig hand)
        {
            if (hand == null || hand.wrist == null)
            {
                return;
            }

            hand.timer -= Time.deltaTime;
            float target = hand.timer > 0f ? 1f : 0f;
            hand.weight = Mathf.MoveTowards(hand.weight, target, Time.deltaTime / blendSeconds);
            if (hand.weight <= 0f || hand.pose == Pose.None)
            {
                return;
            }

            for (int i = 0; i < hand.all.Length; i++)
            {
                hand.saved[i] = hand.all[i].localRotation;
            }

            ApplyPose(hand);

            for (int i = 0; i < hand.all.Length; i++)
            {
                hand.all[i].localRotation = Quaternion.Slerp(hand.saved[i], hand.all[i].localRotation, hand.weight);
            }
        }

        // Dựng tư thế: IK cánh tay tới điểm đích, xoay cổ tay theo hướng ngón/lòng bàn tay mong muốn, rồi uốn từng ngón.
        private void ApplyPose(HandRig hand)
        {
            Vector3 forward = Vector3.ProjectOnPlane(head.forward, Vector3.up);
            forward = forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.forward;
            Vector3 up = Vector3.up;
            Vector3 outward = Vector3.Cross(up, forward) * (hand.isLeft ? -1f : 1f);
            Vector3 shoulder = hand.upper.position;

            Vector3 goal;
            Vector3 fingerDir;
            Vector3 palmDir;
            switch (hand.pose)
            {
                case Pose.SwordFinger:
                {
                    Vector3 aim = forward;
                    PetCombatant enemy = summoner != null ? summoner.EnemyPet : null;
                    if (enemy != null && enemy.gameObject.activeInHierarchy)
                    {
                        Vector3 toEnemy = enemy.transform.position + Vector3.up * 0.4f - shoulder;
                        if (toEnemy.sqrMagnitude > 0.01f) aim = toEnemy.normalized;
                    }

                    goal = shoulder + aim * 0.48f;
                    fingerDir = aim;
                    palmDir = -outward;
                    break;
                }
                case Pose.PalmForward:
                    goal = shoulder + forward * 0.42f + up * 0.06f - outward * 0.05f;
                    fingerDir = (up * 0.85f + forward * 0.15f).normalized;
                    palmDir = forward;
                    break;
                default:
                {
                    // Tâm ngực: giữa hai vai, thấp xuống và nhô ra trước một chút.
                    Vector3 chest = (left.upper.position + right.upper.position) * 0.5f - up * 0.12f + forward * 0.13f;
                    goal = chest + outward * 0.02f;
                    fingerDir = (up * 0.35f - outward * 0.65f).normalized;
                    palmDir = -forward;
                    break;
                }
            }

            TwoBoneIk.Solve(hand.upper, hand.lower, hand.wrist, goal, (Vector3.down * 0.7f + outward * 0.3f).normalized);
            OrientWrist(hand, fingerDir, palmDir);

            bool sword = hand.pose == Pose.SwordFinger;
            // Góc gập (độ) cho 3 đốt: duỗi = 0; gập kín = 75/95/60.
            CurlFinger(hand, Index, 0f, 0f, 0f);
            CurlFinger(hand, Middle, 0f, 0f, 0f);
            CurlFinger(hand, Ring, sword ? 80f : 0f, sword ? 95f : 0f, sword ? 60f : 0f);
            CurlFinger(hand, Pinky, sword ? 80f : 0f, sword ? 95f : 0f, sword ? 60f : 0f);
            CurlFinger(hand, Thumb, sword ? 25f : 0f, sword ? 40f : 0f, sword ? 40f : 0f);
        }

        // Xoay cổ tay: đưa hướng ngón (cổ tay → gốc ngón giữa) về fingerDir, rồi vặn quanh trục đó cho lòng bàn tay nhìn về palmDir.
        private void OrientWrist(HandRig hand, Vector3 fingerDir, Vector3 palmDir)
        {
            Vector3 currentFinger = HandForward(hand);
            hand.wrist.rotation = Quaternion.FromToRotation(currentFinger, fingerDir) * hand.wrist.rotation;
            Vector3 palm = PalmNormal(hand);
            float twist = Vector3.SignedAngle(Vector3.ProjectOnPlane(palm, fingerDir), Vector3.ProjectOnPlane(palmDir, fingerDir), fingerDir);
            hand.wrist.rotation = Quaternion.AngleAxis(twist, fingerDir) * hand.wrist.rotation;
        }

        // Duỗi thẳng ngón (mỗi đốt thẳng hàng với đốt trước) rồi gập từng đốt về phía lòng bàn tay theo góc cho trước.
        private void CurlFinger(HandRig hand, int finger, float a0, float a1, float a2)
        {
            Transform[] chain = hand.fingers[finger];
            if (chain == null)
            {
                return;
            }

            float[] angles = { a0, a1, a2 };
            Vector3 palm = PalmNormal(hand);
            Vector3 previousDir = (chain[0].position - hand.wrist.position).normalized;

            // Trục gập: 4 ngón dài gập quanh trục ngang bàn tay (gốc út → gốc trỏ) – không suy biến kể cả khi đốt trước đã gập
            // tới 90° và đốt sau song song pháp tuyến lòng bàn tay (lỗi cũ: Cross(đốt, pháp tuyến) = 0 nên đốt sau không gập).
            // Ngón cái gập chéo lòng bàn tay nên dùng trục vuông góc với đốt đầu và pháp tuyến. Chiều trục chọn một lần ở đốt đầu.
            Vector3 axis;
            Transform indexRoot = hand.fingers[Index] != null ? hand.fingers[Index][0] : null;
            Transform pinkyRoot = hand.fingers[Pinky] != null ? hand.fingers[Pinky][0] : null;
            if (finger != Thumb && indexRoot != null && pinkyRoot != null)
            {
                axis = (indexRoot.position - pinkyRoot.position).normalized;
            }
            else
            {
                axis = Vector3.Cross(previousDir, palm).normalized;
            }

            // Đầu ngón phải tiến về phía lòng bàn tay khi xoay dương quanh trục.
            if (Vector3.Dot(Quaternion.AngleAxis(10f, axis) * previousDir - previousDir, palm) < 0f)
            {
                axis = -axis;
            }

            for (int j = 0; j < 3; j++)
            {
                Transform joint = chain[j];
                Transform child = chain[j + 1];
                Vector3 segment = child.position - joint.position;
                if (segment.sqrMagnitude < 1e-8f)
                {
                    continue;
                }

                joint.rotation = Quaternion.FromToRotation(segment, previousDir) * joint.rotation;
                if (angles[j] > 0f && axis.sqrMagnitude > 0.5f)
                {
                    joint.rotation = Quaternion.AngleAxis(angles[j], axis) * joint.rotation;
                }

                previousDir = (child.position - joint.position).normalized;
            }
        }

        // Hướng bàn tay: từ cổ tay tới gốc ngón giữa.
        private static Vector3 HandForward(HandRig hand)
        {
            Transform middle = hand.fingers[Middle] != null ? hand.fingers[Middle][0] : null;
            return middle != null ? (middle.position - hand.wrist.position).normalized : hand.wrist.forward;
        }

        // Pháp tuyến lòng bàn tay (nhìn ra khỏi lòng bàn tay) theo dấu đã đo cho từng tay.
        private Vector3 PalmNormal(HandRig hand)
        {
            Transform index = hand.fingers[Index] != null ? hand.fingers[Index][0] : null;
            Transform pinky = hand.fingers[Pinky] != null ? hand.fingers[Pinky][0] : null;
            if (index == null || pinky == null)
            {
                return -hand.wrist.up;
            }

            Vector3 w = hand.wrist.position;
            Vector3 cross = Vector3.Cross(index.position - w, pinky.position - w).normalized;
            return cross * (hand.isLeft ? leftPalmSign : rightPalmSign);
        }

        // Gom xương cánh tay và ngón của một bên; lưu danh sách mọi xương sẽ bị ghi đè để trộn localRotation.
        private HandRig BuildHand(string side, bool isLeft)
        {
            HandRig hand = new HandRig
            {
                isLeft = isLeft,
                upper = FindBone(side + "ArmUpper"),
                lower = FindBone(side + "ArmLower"),
                wrist = FindBone(side + "HandWrist"),
                fingers = new Transform[5][]
            };

            System.Collections.Generic.List<Transform> all = new System.Collections.Generic.List<Transform> { hand.upper, hand.lower, hand.wrist };
            for (int f = 0; f < 5; f++)
            {
                Transform[] chain = new Transform[4];
                bool complete = true;
                for (int j = 0; j < 4; j++)
                {
                    chain[j] = FindBone(side + "Hand" + FingerBones[f][j]);
                    complete &= chain[j] != null;
                }

                hand.fingers[f] = complete ? chain : null;
                if (complete)
                {
                    all.Add(chain[0]);
                    all.Add(chain[1]);
                    all.Add(chain[2]);
                }
            }

            all.RemoveAll(t => t == null);
            hand.all = all.ToArray();
            hand.saved = new Quaternion[hand.all.Length];
            return hand.wrist != null ? hand : null;
        }

        // Tìm xương theo tên chính xác trong nhân vật.
        private Transform FindBone(string boneName)
        {
            Transform[] bones = GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < bones.Length; i++)
            {
                if (bones[i].name == boneName)
                {
                    return bones[i];
                }
            }

            return null;
        }
    }
}
