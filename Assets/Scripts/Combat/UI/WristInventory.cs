using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace XR124.Combat
{
    // Túi đồ dạng đồng hồ trên cổ tay trái. Mở/đóng bằng cách chạm tay phải vào mặt đồng hồ hoặc bấm cần analog trái.
    // Bảng túi đồ bám theo đồng hồ (phía trên cổ tay), luôn quay về mặt người chơi, bấm bằng tia (ISDK Ray Canvas) từ
    // tay cầm/tay phải. Ô Katana: EQUIP gắn kiếm thẳng vào tay phải; STORE cất kiếm; PULL rút kiếm đang cắm về tay.
    public sealed class WristInventory : MonoBehaviour
    {
        [Header("Tham chiếu")]
        [Tooltip("Mặt đồng hồ (con của LeftHandAnchor) – cũng là điểm chạm để mở túi.")]
        [SerializeField] private Transform watchFace;
        [Tooltip("Điểm dùng để chạm vào đồng hồ (RightHandAnchor: theo tay cầm hoặc tay phải).")]
        [SerializeField] private Transform tapper;
        [SerializeField] private GameObject panel;
        [SerializeField] private SummonSword sword;
        [SerializeField] private TMP_Text swordStatus;
        [SerializeField] private TMP_Text swordButtonLabel;
        [SerializeField] private Button swordButton;

        [Header("Hand tracking (tự tìm nếu để trống)")]
        [Tooltip("Tay trái: khi đang tracking, đồng hồ bám lên mu cổ tay trái thật.")]
        [SerializeField] private HandSkeletonReader leftHand;
        [Tooltip("Tay phải: khi đang tracking, chạm đồng hồ bằng đầu ngón trỏ.")]
        [SerializeField] private HandSkeletonReader rightHand;
        [Tooltip("Khoảng nhô của mặt đồng hồ ra khỏi mu cổ tay (m).")]
        [Min(0f)] [SerializeField] private float watchLiftFromWrist = 0.03f;

        [Header("Mở túi")]
        [Min(0.01f)] [SerializeField] private float tapRadius = 0.06f;
        [Min(0f)] [SerializeField] private float toggleCooldown = 0.6f;
        [SerializeField] private bool thumbstickToggle = true;
        [Tooltip("Bảng hiện cao hơn đồng hồ đoạn này (m); bám theo đồng hồ khi đang mở.")]
        [SerializeField] private Vector3 panelOffset = new Vector3(0f, 0.2f, 0f);
        [Tooltip("Độ mượt khi bảng bám theo tay (càng lớn càng bám sát, 0 = dính cứng).")]
        [Min(0f)] [SerializeField] private float panelFollowSharpness = 10f;
        [Tooltip("Bảng chỉ dời theo tay khi vị trí đích lệch quá khoảng này (m) – cử động nhỏ của cổ tay không làm bảng rung.")]
        [Min(0f)] [SerializeField] private float followDeadzone = 0.08f;
        [Tooltip("…hoặc khi hướng nhìn lệch quá góc này (độ).")]
        [Min(0f)] [SerializeField] private float followAngleDeadzone = 20f;

        [Header("Chạm để bấm nút")]
        [Tooltip("Chọc đầu ngón trỏ phải (hand tracking) hoặc mũi tay cầm phải vào nút là bấm, không cần ngắm tia.")]
        [SerializeField] private bool touchPress = true;
        [Tooltip("Khoảng trước/sau mặt nút (m) vẫn tính là chạm.")]
        [Min(0.005f)] [SerializeField] private float touchDepth = 0.03f;
        [Tooltip("Nới viền nút (m) cho dễ trúng.")]
        [Min(0f)] [SerializeField] private float touchPadding = 0.01f;
        [Tooltip("Mũi tay cầm cách anchor tay phải đoạn này về phía trước (m).")]
        [Min(0f)] [SerializeField] private float controllerTipOffset = 0.06f;
        [Min(0f)] [SerializeField] private float pressCooldown = 0.5f;

        [Header("Runtime (chỉ đọc)")]
        [SerializeField] private bool isOpen;

        private Transform head;
        private float lastToggleTime = -10f;
        private bool tapperWasInside;
        private Vector3 watchLocalPosition;
        private Quaternion watchLocalRotation;
        private Vector3 panelTargetPosition;
        private Quaternion panelTargetRotation = Quaternion.identity;
        private bool touchWasInside;
        private float lastPressTime = -10f;

        public bool IsOpen => isOpen;

        // Gán tham chiếu từ công cụ dựng túi đồ trong Editor.
        public void Configure(Transform watch, Transform tapPoint, GameObject inventoryPanel, SummonSword summonSword,
            TMP_Text status, TMP_Text buttonLabel, Button button)
        {
            watchFace = watch;
            tapper = tapPoint;
            panel = inventoryPanel;
            sword = summonSword;
            swordStatus = status;
            swordButtonLabel = buttonLabel;
            swordButton = button;
        }

        // Nối nút Katana, đóng bảng lúc đầu, cache camera.
        private void Start()
        {
            if (swordButton != null)
            {
                swordButton.onClick.AddListener(ToggleSword);
            }

            if (Camera.main != null)
            {
                head = Camera.main.transform;
            }

            // Nhớ tư thế đồng hồ trên tay cầm để trả về khi không còn hand tracking.
            if (watchFace != null)
            {
                watchLocalPosition = watchFace.localPosition;
                watchLocalRotation = watchFace.localRotation;
            }

            FindHandReaders();
            SetOpen(false);
        }

        // Tìm bộ đọc xương hai tay theo SkeletonType đã cấu hình của OVRSkeleton (XRHandLeft/HandLeft là tay trái).
        private void FindHandReaders()
        {
            if (leftHand != null && rightHand != null)
            {
                return;
            }

            HandSkeletonReader[] readers = FindObjectsByType<HandSkeletonReader>(FindObjectsSortMode.None);
            for (int i = 0; i < readers.Length; i++)
            {
                OVRSkeleton skeleton = readers[i].Skeleton;
                if (skeleton == null)
                {
                    continue;
                }

                OVRSkeleton.SkeletonType type = skeleton.GetSkeletonType();
                bool isLeft = type == OVRSkeleton.SkeletonType.XRHandLeft || type == OVRSkeleton.SkeletonType.HandLeft;
                if (isLeft && leftHand == null) leftHand = readers[i];
                else if (!isLeft && rightHand == null) rightHand = readers[i];
            }
        }

        // Hủy nối nút khi bị hủy.
        private void OnDestroy()
        {
            if (swordButton != null)
            {
                swordButton.onClick.RemoveListener(ToggleSword);
            }
        }

        // Đặt đồng hồ theo tay đang dùng, theo dõi chạm vào đồng hồ (sườn lên khi điểm chạm đi vào vùng chạm) và nút cần
        // analog trái; cập nhật trạng thái ô kiếm. Điểm chạm = đầu ngón trỏ phải khi hand tracking, ngược lại = RightHandAnchor.
        private void Update()
        {
            UpdateWatchPose();

            bool rightTracked = rightHand != null && rightHand.IsValid;
            if (watchFace != null && (tapper != null || rightTracked))
            {
                Vector3 tapPoint = rightTracked ? rightHand.IndexTip : tapper.position;
                bool inside = (tapPoint - watchFace.position).sqrMagnitude <= tapRadius * tapRadius;
                if (inside && !tapperWasInside)
                {
                    TryToggle();
                }

                tapperWasInside = inside;
            }

            if (thumbstickToggle && OVRInput.GetDown(OVRInput.Button.PrimaryThumbstick, OVRInput.Controller.LTouch))
            {
                TryToggle();
            }

            if (isOpen)
            {
                PositionPanel(false);
                RefreshSwordSlot();
                CheckTouchPress();
            }
        }

        // Đặt bảng phía trên đồng hồ và quay mặt về người chơi. snap = true: đặt ngay (lúc mở). false: chỉ đổi đích khi đồng hồ
        // đã dời quá followDeadzone hoặc hướng quá followAngleDeadzone, rồi trượt mượt tới đích – cử động nhỏ thì bảng đứng yên.
        private void PositionPanel(bool snap)
        {
            if (panel == null || watchFace == null)
            {
                return;
            }

            Vector3 desiredPosition = watchFace.position + panelOffset;
            Quaternion desiredRotation = panelTargetRotation;
            if (head != null)
            {
                Vector3 away = desiredPosition - head.position;
                away.y = 0f;
                if (away.sqrMagnitude > 0.0001f)
                {
                    desiredRotation = Quaternion.LookRotation(away, Vector3.up);
                }
            }

            if (snap || Vector3.Distance(desiredPosition, panelTargetPosition) > followDeadzone
                || Quaternion.Angle(desiredRotation, panelTargetRotation) > followAngleDeadzone)
            {
                panelTargetPosition = desiredPosition;
                panelTargetRotation = desiredRotation;
            }

            float blend = snap || panelFollowSharpness <= 0f ? 1f : 1f - Mathf.Exp(-panelFollowSharpness * Time.deltaTime);
            panel.transform.SetPositionAndRotation(
                Vector3.Lerp(panel.transform.position, panelTargetPosition, blend),
                Quaternion.Slerp(panel.transform.rotation, panelTargetRotation, blend));
        }

        // Chạm để bấm: điểm chạm (đầu ngón trỏ phải khi hand tracking, mũi tay cầm phải khi cầm tay cầm) đi vào khối hộp
        // quanh nút (mặt nút nới touchPadding, dày ±touchDepth) thì bấm một lần (bắt sườn lên + thời gian chờ).
        private void CheckTouchPress()
        {
            if (!touchPress || swordButton == null || !swordButton.interactable)
            {
                touchWasInside = false;
                return;
            }

            bool rightTracked = rightHand != null && rightHand.IsValid;
            Vector3 point;
            if (rightTracked)
            {
                point = rightHand.IndexTip;
            }
            else if (tapper != null)
            {
                point = tapper.position + tapper.forward * controllerTipOffset;
            }
            else
            {
                touchWasInside = false;
                return;
            }

            RectTransform rect = (RectTransform)swordButton.transform;
            Vector3 local = rect.InverseTransformPoint(point);
            float unitsPerMeter = 1f / Mathf.Max(rect.lossyScale.x, 1e-6f);
            Vector2 half = rect.rect.size * 0.5f + Vector2.one * (touchPadding * unitsPerMeter);
            Vector2 center = rect.rect.center;
            bool inside = Mathf.Abs(local.x - center.x) <= half.x
                          && Mathf.Abs(local.y - center.y) <= half.y
                          && Mathf.Abs(local.z) <= touchDepth * unitsPerMeter;

            if (inside && !touchWasInside && Time.time - lastPressTime >= pressCooldown)
            {
                lastPressTime = Time.time;
                ToggleSword();
            }

            touchWasInside = inside;
        }

        // Tay trái đang hand tracking: đặt mặt đồng hồ lên mu cổ tay (ngược hướng lòng bàn tay), mặt đồng hồ quay ra ngoài.
        // Không tracking (đang cầm tay cầm): trả đồng hồ về tư thế cũ gắn theo LeftHandAnchor.
        private void UpdateWatchPose()
        {
            if (watchFace == null)
            {
                return;
            }

            if (leftHand != null && leftHand.IsValid && leftHand.Wrist != null)
            {
                Vector3 backOfHand = -leftHand.PalmNormal;
                watchFace.SetPositionAndRotation(leftHand.Wrist.position + backOfHand * watchLiftFromWrist,
                    Quaternion.FromToRotation(Vector3.up, backOfHand));
            }
            else
            {
                watchFace.localPosition = watchLocalPosition;
                watchFace.localRotation = watchLocalRotation;
            }
        }

        // Đổi trạng thái mở/đóng, có thời gian chờ để một lần chạm không bật tắt liên tục.
        private void TryToggle()
        {
            if (Time.time - lastToggleTime < toggleCooldown)
            {
                return;
            }

            lastToggleTime = Time.time;
            SetOpen(!isOpen);
        }

        // Mở: đặt ngay bảng phía trên đồng hồ (sau đó bảng bám theo tay trong Update). Đóng: ẩn bảng.
        public void SetOpen(bool open)
        {
            isOpen = open;
            if (panel == null)
            {
                return;
            }

            if (open)
            {
                PositionPanel(true);
                RefreshSwordSlot();
            }

            panel.SetActive(open);
        }

        // Nút Katana: trong túi hoặc đang cắm → gắn kiếm vào tay phải (EQUIP/PULL); đang trong tay → cất vào túi (STORE).
        public void ToggleSword()
        {
            if (sword == null)
            {
                return;
            }

            if (sword.IsStored || sword.State == SummonSword.SwordState.Planted)
            {
                sword.EquipToHand();
            }
            else if (sword.CanStore)
            {
                sword.Store();
            }

            RefreshSwordSlot();
        }

        // Cập nhật chữ trạng thái và nút theo tình trạng kiếm: trong túi → EQUIP, đang cắm → PULL, trong tay → STORE
        // (chữ ASCII vì font TMP mặc định thiếu dấu tiếng Việt).
        private void RefreshSwordSlot()
        {
            if (sword == null)
            {
                return;
            }

            string status;
            string action;
            bool interactable = true;
            if (sword.IsStored)
            {
                status = "In bag";
                action = "EQUIP";
            }
            else if (sword.State == SummonSword.SwordState.Planted)
            {
                status = "Planted";
                action = "PULL";
            }
            else if (sword.CanStore)
            {
                status = "In hand";
                action = "STORE";
            }
            else
            {
                // Đang bị nắm bằng Interaction SDK: đợi thả tay (kiếm tự về tay phải) rồi mới cất được.
                status = "Grabbed";
                action = "STORE";
                interactable = false;
            }

            if (swordStatus != null && swordStatus.text != status) swordStatus.text = status;
            if (swordButtonLabel != null && swordButtonLabel.text != action) swordButtonLabel.text = action;
            if (swordButton != null) swordButton.interactable = interactable;
        }
    }
}
