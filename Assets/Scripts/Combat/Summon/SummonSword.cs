using System;
using System.Collections;
using Oculus.Interaction;
using UnityEngine;

namespace XR124.Combat
{
    // Thanh kiếm triệu hồi dùng Interaction SDK của rig (OVRComprehensiveInteractionRig): cầm bằng tay cầm Touch
    // (GrabInteractable) hoặc bằng tay (HandGrabInteractable) qua Grabbable. Đâm mũi kiếm xuống sàn đủ nhanh thì
    // kiếm bị ép thả, cắm đứng tại chỗ và mở pháp trận; cầm lại khi đang cắm để rút lên. Thả tay thì kiếm bay về chỗ nghỉ.
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
        [Tooltip("GrabInteractable / HandGrabInteractable của kiếm; bị tắt tạm thời để ép tay/tay cầm thả kiếm khi cắm.")]
        [SerializeField] private Behaviour[] grabInteractables;
        [Tooltip("Tắt nhận diện ấn kí khi đang cầm kiếm để tư thế cầm không bị hiểu nhầm thành ấn.")]
        [SerializeField] private HandSealRecognizer leftSeal;
        [SerializeField] private HandSealRecognizer rightSeal;
        [Tooltip("Mũi kiếm; dùng để phát hiện chạm sàn.")]
        [SerializeField] private Transform tip;

        [Header("Thả / cắm")]
        [Min(0.1f)] [SerializeField] private float returnSpeed = 3f;
        [Tooltip("Mũi kiếm cách mặt sàn nhỏ hơn giá trị này thì tính là chạm sàn.")]
        [Min(0f)] [SerializeField] private float plantHeightThreshold = 0.05f;
        [Tooltip("Tốc độ đâm xuống tối thiểu (m/s) để tránh cắm nhầm khi chỉ hạ tay chậm.")]
        [Min(0f)] [SerializeField] private float minPlantDownSpeed = 0.4f;
        [Tooltip("Độ lún của mũi kiếm vào sàn khi đã cắm.")]
        [Min(0f)] [SerializeField] private float plantSinkDepth = 0.08f;
        [Min(0.01f)] [SerializeField] private float plantCheckRadius = 0.06f;
        [Tooltip("Sau khi cắm, chờ đoạn này rồi mới cho cầm lại (tránh vừa cắm đã bị rút).")]
        [Min(0f)] [SerializeField] private float regrabDelay = 0.5f;

        [Header("Túi đồ")]
        [Tooltip("Vào game kiếm nằm trong túi đồ (ẩn); lấy ra qua đồng hồ túi đồ trên tay trái.")]
        [SerializeField] private bool startInInventory = true;

        [Header("Runtime (chỉ đọc)")]
        [SerializeField] private SwordState state = SwordState.Resting;
        [SerializeField] private string debugState = "Resting";

        private Pose restPose;
        private Vector3 lastTipPosition;
        private bool isPlanting;

        // Kiếm đã cắm: điểm trên sàn nơi mở pháp trận.
        public event Action<SummonSword, Vector3> Planted;
        public event Action<SummonSword> PulledOut;

        public SwordState State => state;
        public string DebugState => debugState;
        public Vector3 PlantedPoint { get; private set; }
        // Kiếm đang nằm trong túi đồ (GameObject tắt).
        public bool IsStored => !gameObject.activeSelf;
        // Chỉ cất được khi kiếm đang nghỉ (không đang cầm, không đang cắm trong trận).
        public bool CanStore => !IsStored && state == SwordState.Resting;

        // Vào game: cất kiếm vào túi nếu được cấu hình (Awake đã lưu vị trí nghỉ).
        private void Start()
        {
            if (startInInventory)
            {
                Store();
            }
        }

        // Lấy kiếm ra khỏi túi: kiếm hiện ở vị trí cho trước và coi đó là chỗ nghỉ mới (lơ lửng chờ người chơi cầm).
        public void TakeOut(Vector3 position, Quaternion rotation)
        {
            restPose = new Pose(position, rotation);
            transform.SetPositionAndRotation(position, rotation);
            state = SwordState.Resting;
            debugState = "Resting (lấy từ túi)";
            gameObject.SetActive(true);
        }

        // Cất kiếm vào túi (ẩn GameObject); từ chối khi đang cầm hoặc đang cắm.
        public bool Store()
        {
            if (!CanStore)
            {
                return false;
            }

            SetSealsSuppressed(false);
            debugState = "Stored";
            gameObject.SetActive(false);
            return true;
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

        // Lấy Grabbable và lưu vị trí nghỉ ban đầu để kiếm bay về khi thả.
        private void Awake()
        {
            if (grabbable == null)
            {
                grabbable = GetComponent<Grabbable>();
            }

            restPose = new Pose(transform.position, transform.rotation);
        }

        // Nghe sự kiện cầm/thả từ Interaction SDK.
        private void OnEnable()
        {
            grabbable.WhenPointerEventRaised += HandlePointerEvent;
        }

        // Hủy nghe sự kiện.
        private void OnDisable()
        {
            grabbable.WhenPointerEventRaised -= HandlePointerEvent;
        }

        // Select đầu tiên = bắt đầu cầm (rút kiếm nếu đang cắm); Unselect cuối cùng = thả kiếm về chỗ nghỉ.
        // Unselect do chính việc cắm kiếm gây ra (isPlanting) thì bỏ qua.
        private void HandlePointerEvent(PointerEvent evt)
        {
            if (evt.Type == PointerEventType.Select && state != SwordState.Held)
            {
                bool wasPlanted = state == SwordState.Planted;
                state = SwordState.Held;
                debugState = "Held";
                lastTipPosition = tip != null ? tip.position : transform.position;
                SetSealsSuppressed(true);
                if (wasPlanted)
                {
                    PulledOut?.Invoke(this);
                }

                return;
            }

            if ((evt.Type == PointerEventType.Unselect || evt.Type == PointerEventType.Cancel)
                && !isPlanting && state == SwordState.Held && grabbable.SelectingPointsCount == 0)
            {
                state = SwordState.Resting;
                debugState = "Resting (thả tay)";
                SetSealsSuppressed(false);
            }
        }

        // Đang cầm: theo dõi mũi kiếm để phát hiện cú đâm xuống sàn. Đang nghỉ: bay dần về vị trí nghỉ.
        private void Update()
        {
            if (state == SwordState.Resting)
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

        // Cắm kiếm nếu đấu trường xác nhận điểm hợp lệ: ép thả bằng cách tắt tạm interactable, dựng kiếm thẳng đứng
        // với mũi lún xuống sàn, phát sự kiện Planted. Bị từ chối thì vẫn cầm và ghi lý do.
        private void TryPlant(Vector3 floorPoint)
        {
            PlacementResult result = placement.Validate(floorPoint, plantCheckRadius, 0.3f, transform);
            if (result != PlacementResult.Valid)
            {
                debugState = $"Cắm bị từ chối: {result}";
                Debug.Log($"[Summon] Không cắm được kiếm: {placement.LastRejection}", this);
                return;
            }

            isPlanting = true;
            SetInteractablesEnabled(false);
            isPlanting = false;

            state = SwordState.Planted;
            PlantedPoint = floorPoint;
            SetSealsSuppressed(false);

            Vector3 forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
            if (forward.sqrMagnitude < 0.0001f)
            {
                forward = Vector3.forward;
            }

            transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
            Vector3 tipOffset = transform.position - tip.position;
            transform.position = floorPoint + tipOffset + Vector3.down * plantSinkDepth;

            debugState = "Planted";
            Planted?.Invoke(this, floorPoint);
            StartCoroutine(ReenableInteractables());
        }

        // Bật lại khả năng cầm sau một nhịp để người chơi phải bấm/nắm lại mới rút được kiếm.
        private IEnumerator ReenableInteractables()
        {
            yield return new WaitForSeconds(regrabDelay);
            SetInteractablesEnabled(true);
        }

        // Rút kiếm bằng code (ví dụ khi triệu hồi thất bại) và trả về vị trí nghỉ.
        public void ReturnHome()
        {
            bool wasPlanted = state == SwordState.Planted;
            if (state == SwordState.Held)
            {
                isPlanting = true;
                SetInteractablesEnabled(false);
                isPlanting = false;
                SetInteractablesEnabled(true);
            }

            state = SwordState.Resting;
            debugState = "Resting (reset)";
            SetSealsSuppressed(false);
            if (wasPlanted)
            {
                PulledOut?.Invoke(this);
            }
        }

        // Bay dần về vị trí nghỉ.
        private void ReturnToRest()
        {
            float step = returnSpeed * Time.deltaTime;
            transform.SetPositionAndRotation(
                Vector3.MoveTowards(transform.position, restPose.position, step),
                Quaternion.RotateTowards(transform.rotation, restPose.rotation, step * 180f));
        }

        // Bật/tắt các interactable; tắt sẽ khiến Interaction SDK gỡ mọi tay/tay cầm đang cầm (Interactable.Disable).
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
