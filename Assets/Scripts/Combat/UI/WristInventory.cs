using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace XR124.Combat
{
    // Túi đồ dạng đồng hồ trên cổ tay trái. Mở/đóng bằng cách chạm tay phải vào mặt đồng hồ hoặc bấm cần analog trái.
    // Bảng túi đồ hiện phía trên đồng hồ, quay về mặt người chơi, bấm bằng tia (ISDK Ray Canvas) từ tay cầm/tay phải.
    // Ô Katana: EQUIP lấy kiếm ra lơ lửng trước ngực để cầm; STORE cất kiếm vào túi (chỉ khi kiếm đang nghỉ).
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
        [Tooltip("Bảng hiện cao hơn đồng hồ đoạn này (m) và lùi về phía mặt người chơi.")]
        [SerializeField] private Vector3 panelOffset = new Vector3(0f, 0.16f, 0f);

        [Header("Lấy kiếm ra")]
        [Tooltip("Kiếm hiện trước mặt người chơi: khoảng cách ra trước và hạ thấp so với mắt (m).")]
        [SerializeField] private float swordForward = 0.35f;
        [SerializeField] private float swordBelowEyes = 0.35f;
        [SerializeField] private float minSwordHeight = 0.7f;

        [Header("Runtime (chỉ đọc)")]
        [SerializeField] private bool isOpen;

        private Transform head;
        private float lastToggleTime = -10f;
        private bool tapperWasInside;
        private Vector3 watchLocalPosition;
        private Quaternion watchLocalRotation;

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
                RefreshSwordSlot();
            }
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

        // Mở: đặt bảng phía trên đồng hồ và quay về phía mặt người chơi (đứng yên trong thế giới để dễ bấm). Đóng: ẩn bảng.
        public void SetOpen(bool open)
        {
            isOpen = open;
            if (panel == null)
            {
                return;
            }

            if (open && watchFace != null)
            {
                Vector3 position = watchFace.position + panelOffset;
                panel.transform.position = position;
                if (head != null)
                {
                    Vector3 away = position - head.position;
                    away.y = 0f;
                    if (away.sqrMagnitude > 0.0001f)
                    {
                        panel.transform.rotation = Quaternion.LookRotation(away, Vector3.up);
                    }
                }

                RefreshSwordSlot();
            }

            panel.SetActive(open);
        }

        // Nút Katana: đang trong túi thì lấy ra trước ngực, đang nghỉ ngoài túi thì cất vào.
        public void ToggleSword()
        {
            if (sword == null)
            {
                return;
            }

            if (sword.IsStored)
            {
                Transform view = head != null ? head : transform;
                Vector3 forward = Vector3.ProjectOnPlane(view.forward, Vector3.up);
                forward = forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.forward;
                Vector3 position = view.position + forward * swordForward + Vector3.down * swordBelowEyes;
                // Không để kiếm hiện dưới sàn khi đầu thấp/chưa tracking (sàn đấu trường ở y = 0).
                position.y = Mathf.Max(position.y, minSwordHeight);
                // Kiếm dựng đứng, chuôi hướng lên, lưỡi quay theo hướng nhìn để dễ nắm.
                sword.TakeOut(position, Quaternion.LookRotation(forward, Vector3.up));
            }
            else
            {
                sword.Store();
            }

            RefreshSwordSlot();
        }

        // Cập nhật chữ trạng thái và nút theo tình trạng kiếm (chữ ASCII vì font TMP mặc định thiếu dấu tiếng Việt).
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
            else if (sword.CanStore)
            {
                status = "Equipped";
                action = "STORE";
            }
            else
            {
                status = sword.State == SummonSword.SwordState.Planted ? "Planted" : "In hand";
                action = "STORE";
                interactable = false;
            }

            if (swordStatus != null && swordStatus.text != status) swordStatus.text = status;
            if (swordButtonLabel != null && swordButtonLabel.text != action) swordButtonLabel.text = action;
            if (swordButton != null) swordButton.interactable = interactable;
        }
    }
}
