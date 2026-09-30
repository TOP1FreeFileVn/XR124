using System;
using System.Collections;
using Oculus.Interaction;
using UnityEngine;

namespace XR124.Combat
{
    // Thanh kiếm triệu hồi. Trang bị từ túi đồ thì kiếm tự gắn vào tay phải (RightHandAnchor của OVRCameraRig – theo tay cầm
    // hoặc bàn tay khi hand tracking), lưỡi chĩa theo hướng tay. Đâm mũi kiếm xuống sàn đủ nhanh thì kiếm rời tay, cắm đứng tại
    // chỗ và mở pháp trận. Kiếm đang cắm có thể nắm lại bằng Interaction SDK (Grabbable) hoặc bấm PULL trong túi đồ để rút;
    // thả tay sau khi nắm thì kiếm tự quay về tay phải.
    [RequireComponent(typeof(Grabbable))]
    public sealed class SummonSword : MonoBehaviour
    {
        public enum SwordState : byte
        {
            Resting,
            Held,
            Planted
        }

        [Header("Tham chiếu")]
        [SerializeField] private ArenaPlacement placement;
        [SerializeField] private Grabbable grabbable;
        [Tooltip("GrabInteractable / HandGrabInteractable của kiếm; bị tắt khi kiếm đang gắn trong tay hoặc để ép thả khi cắm.")]
        [SerializeField] private Behaviour[] grabInteractables;
        [Tooltip("Tắt nhận diện ấn kí khi đang cầm kiếm để tư thế cầm không bị hiểu nhầm thành ấn.")]
        [SerializeField] private HandSealRecognizer leftSeal;
        [SerializeField] private HandSealRecognizer rightSeal;
        [Tooltip("Mũi kiếm; dùng để phát hiện chạm sàn.")]
        [SerializeField] private Transform tip;

        [Header("Trang bị vào tay")]
        [Tooltip("Điểm bám tay phải; để trống thì tự lấy OVRCameraRig.rightHandAnchor.")]
        [SerializeField] private Transform handAnchor;
        [Tooltip("Vị trí gốc kiếm trong không gian tay (m): đặt sao cho chuôi (Grip, cao 0.121 m) nằm giữa nắm tay.")]
        [SerializeField] private Vector3 heldLocalPosition = new Vector3(0f, 0.069f, 0.099f);
        [Tooltip("Góc kiếm trong không gian tay: (-125, 0, 0) cho lưỡi (trục -Y của kiếm) chĩa ra trước và ngóc lên ~35° " +
                 "như người cầm kiếm thật (−90 là chĩa thẳng theo tay như dao găm).")]
        [SerializeField] private Vector3 heldLocalEuler = new Vector3(-125f, 0f, 0f);

        [Header("Thả / cắm")]
        [Min(0.1f)] [SerializeField] private float returnSpeed = 3f;
        [Tooltip("Mũi kiếm cách mặt sàn nhỏ hơn giá trị này thì tính là chạm sàn.")]
        [Min(0f)] [SerializeField] private float plantHeightThreshold = 0.05f;
        [Tooltip("Tốc độ đâm xuống tối thiểu (m/s) để tránh cắm nhầm khi chỉ hạ tay chậm.")]
        [Min(0f)] [SerializeField] private float minPlantDownSpeed = 0.4f;
        [Tooltip("Độ lún của mũi kiếm vào sàn khi đã cắm.")]
        [Min(0f)] [SerializeField] private float plantSinkDepth = 0.08f;
        [Min(0.01f)] [SerializeField] private float plantCheckRadius = 0.06f;
        [Tooltip("Sau khi cắm, chờ đoạn này rồi mới cho nắm lại (tránh vừa cắm đã bị rút).")]
        [Min(0f)] [SerializeField] private float regrabDelay = 0.5f;

        [Header("Túi đồ")]
        [Tooltip("Vào game kiếm nằm trong túi đồ (ẩn); lấy ra qua đồng hồ túi đồ trên tay trái.")]
        [SerializeField] private bool startInInventory = true;

        [Header("Runtime (chỉ đọc)")]
        [SerializeField] private SwordState state = SwordState.Resting;
        [SerializeField] private bool isEquipped;
        [SerializeField] private string debugState = "Resting";

        private Pose restPose;
        private Vector3 lastTipPosition;
        private bool isPlanting;
        private bool equipNextFrame;
        private Coroutine reenableRoutine;

        // Kiếm đã cắm: điểm trên sàn nơi mở pháp trận.
        public event Action<SummonSword, Vector3> Planted;
        public event Action<SummonSword> PulledOut;

        public SwordState State => state;
        public string DebugState => debugState;
        public Vector3 PlantedPoint { get; private set; }
        // Kiếm đang nằm trong túi đồ (GameObject tắt).
        public bool IsStored => !gameObject.activeSelf;
        // Kiếm đang gắn trong tay phải (do trang bị từ túi đồ).
        public bool IsEquipped => isEquipped;
        // Cất được khi kiếm đang gắn trong tay hoặc đang nghỉ (không đang cắm, không bị nắm bằng Interaction SDK).
        public bool CanStore => !IsStored && (isEquipped || state == SwordState.Resting);

        // Vào game: cất kiếm vào túi nếu được cấu hình (Awake đã lưu vị trí nghỉ).
        private void Start()
        {
            if (startInInventory)
            {
                Store();
            }
        }

        // Gán tham chiếu từ công cụ Editor.
        public void Configure(ArenaPlacement floorPlacement, Grabbable swordGrabbable, Behaviour[] interactables,
            HandSealRecognizer leftRecognizer, HandSealRecognizer rightRecognizer, Transform tipPoint)
        {
            placement = floorPlacement;
            grabbable = swordGrabbable;
            grabInteractables = interactables;
            leftSeal = leftRecognizer;
            rightSeal = rightRecognizer;
            tip = tipPoint;
        }

        // Lấy Grabbable và lưu vị trí nghỉ ban đầu (dùng khi không tìm được tay để gắn).
        private void Awake()
        {
            if (grabbable == null)
            {
                grabbable = GetComponent<Grabbable>();
            }

            restPose = new Pose(transform.position, transform.rotation);
        }

        // Nghe sự kiện nắm/thả từ Interaction SDK.
        private void OnEnable()
        {
            grabbable.WhenPointerEventRaised += HandlePointerEvent;
        }

        // Hủy nghe sự kiện.
        private void OnDisable()
        {
            grabbable.WhenPointerEventRaised -= HandlePointerEvent;
        }

        // Trang bị: bật kiếm (nếu đang trong túi), gắn vào tay phải và coi như đang cầm để luồng cắm kiếm hoạt động.
        // Kiếm đang cắm thì đây là thao tác rút kiếm (phát PulledOut). Tắt interactable để Interaction SDK không giành kiếm
        // khỏi tay trong lúc đang gắn. Trả về false nếu không tìm thấy điểm bám tay.
        public bool EquipToHand()
        {
            Transform anchor = ResolveHandAnchor();
            if (anchor == null)
            {
                debugState = "Không tìm thấy RightHandAnchor để gắn kiếm";
                Debug.LogWarning($"[Summon] {debugState}", this);
                return false;
            }

            bool wasPlanted = state == SwordState.Planted;
            if (reenableRoutine != null)
            {
                StopCoroutine(reenableRoutine);
                reenableRoutine = null;
            }

            gameObject.SetActive(true);
            isEquipped = true;
            equipNextFrame = false;
            isPlanting = true;
            SetInteractablesEnabled(false);
            isPlanting = false;

            state = SwordState.Held;
            debugState = "Equipped (trong tay)";
            FollowHand(anchor);
            lastTipPosition = tip != null ? tip.position : transform.position;
            SetSealsSuppressed(true);
            if (wasPlanted)
            {
                PulledOut?.Invoke(this);
            }

            return true;
        }

        // Lấy kiếm ra khỏi túi ở một vị trí cho trước (lơ lửng chờ nắm). Giữ lại cho công cụ quay video/kiểm thử.
        public void TakeOut(Vector3 position, Quaternion rotation)
        {
            isEquipped = false;
            restPose = new Pose(position, rotation);
            transform.SetPositionAndRotation(position, rotation);
            state = SwordState.Resting;
            debugState = "Resting (lấy từ túi)";
            gameObject.SetActive(true);
            SetInteractablesEnabled(true);
        }

        // Cất kiếm vào túi (ẩn GameObject); từ chối khi đang cắm hoặc đang bị nắm bằng Interaction SDK.
        public bool Store()
        {
            if (!CanStore)
            {
                return false;
            }

            isEquipped = false;
            state = SwordState.Resting;
            SetSealsSuppressed(false);
            debugState = "Stored";
            gameObject.SetActive(false);
            return true;
        }

        // Cắm kiếm tại một điểm (dùng cho nút kiểm thử trong Editor): chiếu điểm xuống sàn rồi chạy đúng luồng cắm thật.
        public bool PlantAt(Vector3 point)
        {
            if (IsStored || state == SwordState.Planted || placement == null || !placement.TryProjectToFloor(point, out Vector3 floorPoint))
            {
                return false;
            }

            return TryPlant(floorPoint);
        }

        // Select đầu tiên = bắt đầu nắm (rút kiếm nếu đang cắm); Unselect cuối cùng = thả tay → kiếm quay về tay phải
        // (làm ở Update khung sau vì không nên tắt interactable ngay trong callback của Interaction SDK).
        // Bỏ qua mọi sự kiện khi kiếm đang gắn trong tay hoặc do chính việc cắm kiếm gây ra (isPlanting).
        private void HandlePointerEvent(PointerEvent evt)
        {
            if (isEquipped || isPlanting)
            {
                return;
            }

            if (evt.Type == PointerEventType.Select && state != SwordState.Held)
            {
                bool wasPlanted = state == SwordState.Planted;
                state = SwordState.Held;
                debugState = "Held (Interaction SDK)";
                lastTipPosition = tip != null ? tip.position : transform.position;
                SetSealsSuppressed(true);
                if (wasPlanted)
                {
                    PulledOut?.Invoke(this);
                }

                return;
            }

            if ((evt.Type == PointerEventType.Unselect || evt.Type == PointerEventType.Cancel)
                && state == SwordState.Held && grabbable.SelectingPointsCount == 0)
            {
                state = SwordState.Resting;
                debugState = "Resting (thả tay)";
                SetSealsSuppressed(false);
                equipNextFrame = true;
            }
        }

        // Gắn trong tay: bám theo tay rồi kiểm tra cú đâm. Đang nghỉ: quay về tay (hoặc bay về chỗ nghỉ nếu không có tay).
        private void Update()
        {
            if (equipNextFrame)
            {
                equipNextFrame = false;
                if (EquipToHand())
                {
                    return;
                }
            }

            if (isEquipped)
            {
                Transform anchor = ResolveHandAnchor();
                if (anchor != null)
                {
                    FollowHand(anchor);
                }
            }
            else if (state == SwordState.Resting)
            {
                ReturnToRest();
                return;
            }

            if (state != SwordState.Held || tip == null || placement == null)
            {
                return;
            }

            Vector3 tipPosition = tip.position;
            float downSpeed = Time.deltaTime > 0f ? (lastTipPosition.y - tipPosition.y) / Time.deltaTime : 0f;
            lastTipPosition = tipPosition;

            if (downSpeed >= minPlantDownSpeed
                && placement.TryProjectToFloor(tipPosition, out Vector3 floorPoint)
                && tipPosition.y - floorPoint.y <= plantHeightThreshold)
            {
                TryPlant(floorPoint);
            }
        }

        // Cắm kiếm nếu đấu trường xác nhận điểm hợp lệ: rời tay, dựng kiếm thẳng đứng với mũi lún xuống sàn, phát sự kiện
        // Planted. Bị từ chối thì vẫn giữ trong tay và ghi lý do vào debugState/Console.
        private bool TryPlant(Vector3 floorPoint)
        {
            PlacementResult result = placement.Validate(floorPoint, plantCheckRadius, 0.3f, transform);
            if (result != PlacementResult.Valid)
            {
                debugState = $"Cắm bị từ chối: {result}";
                Debug.Log($"[Summon] Không cắm được kiếm: {placement.LastRejection}", this);
                return false;
            }

            isEquipped = false;
            isPlanting = true;
            SetInteractablesEnabled(false);
            isPlanting = false;

            state = SwordState.Planted;
            PlantedPoint = floorPoint;
            SetSealsSuppressed(false);

            Vector3 forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
            if (forward.sqrMagnitude < 0.0001f)
            {
                forward = Vector3.ProjectOnPlane(transform.up, Vector3.up);
            }

            if (forward.sqrMagnitude < 0.0001f)
            {
                forward = Vector3.forward;
            }

            transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
            Vector3 tipOffset = transform.position - tip.position;
            transform.position = floorPoint + tipOffset + Vector3.down * plantSinkDepth;

            debugState = "Planted";
            reenableRoutine = StartCoroutine(ReenableInteractables());
            // Phát sự kiện sau cùng: người nghe (BattleSummoner) có thể gọi ReturnHome để đưa kiếm về tay nếu triệu hồi thất bại.
            Planted?.Invoke(this, floorPoint);
            return state == SwordState.Planted;
        }

        // Bật lại khả năng nắm sau một nhịp để người chơi phải nắm lại mới rút được kiếm (bỏ qua nếu kiếm đã về tay).
        private IEnumerator ReenableInteractables()
        {
            yield return new WaitForSeconds(regrabDelay);
            reenableRoutine = null;
            if (!isEquipped)
            {
                SetInteractablesEnabled(true);
            }
        }

        // Rút kiếm bằng code (ví dụ khi triệu hồi thất bại): đưa kiếm về tay phải; không có tay thì bay về chỗ nghỉ.
        public void ReturnHome()
        {
            if (EquipToHand())
            {
                return;
            }

            bool wasPlanted = state == SwordState.Planted;
            state = SwordState.Resting;
            debugState = "Resting (reset)";
            SetSealsSuppressed(false);
            SetInteractablesEnabled(true);
            if (wasPlanted)
            {
                PulledOut?.Invoke(this);
            }
        }

        // Đặt kiếm theo tay: gốc kiếm ở heldLocalPosition, xoay heldLocalEuler trong không gian của điểm bám tay.
        private void FollowHand(Transform anchor)
        {
            transform.SetPositionAndRotation(anchor.TransformPoint(heldLocalPosition), anchor.rotation * Quaternion.Euler(heldLocalEuler));
        }

        // Lấy điểm bám tay phải: ưu tiên tham chiếu gán sẵn, không có thì lấy từ OVRCameraRig trong scene (cache lại).
        private Transform ResolveHandAnchor()
        {
            if (handAnchor == null)
            {
                OVRCameraRig rig = FindFirstObjectByType<OVRCameraRig>();
                if (rig != null)
                {
                    handAnchor = rig.rightHandAnchor;
                }
            }

            return handAnchor;
        }

        // Bay dần về vị trí nghỉ.
        private void ReturnToRest()
        {
            float step = returnSpeed * Time.deltaTime;
            transform.SetPositionAndRotation(
                Vector3.MoveTowards(transform.position, restPose.position, step),
                Quaternion.RotateTowards(transform.rotation, restPose.rotation, step * 180f));
        }

        // Bật/tắt các interactable; tắt sẽ khiến Interaction SDK gỡ mọi tay/tay cầm đang nắm (Interactable.Disable).
        private void SetInteractablesEnabled(bool enabled)
        {
            if (grabInteractables == null)
            {
                return;
            }

            for (int i = 0; i < grabInteractables.Length; i++)
            {
                if (grabInteractables[i] != null)
                {
                    grabInteractables[i].enabled = enabled;
                }
            }
        }

        // Tắt/bật nhận diện ấn kí trên cả hai tay trong lúc cầm kiếm.
        private void SetSealsSuppressed(bool suppressed)
        {
            if (leftSeal != null) leftSeal.SetSuppressed(suppressed);
            if (rightSeal != null) rightSeal.SetSuppressed(suppressed);
        }
    }
}
