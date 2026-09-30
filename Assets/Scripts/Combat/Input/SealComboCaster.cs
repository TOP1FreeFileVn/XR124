using System;
using UnityEngine;

namespace XR124.Combat
{
    // Ghép ấn kí từ hai tay thành hành động Orb rồi ra lệnh cho pet của người chơi.
    // Kết 1–3 ấn liên tiếp; sau comboWindow giây không kết thêm (hoặc đủ 3 ấn) thì thi triển.
    // Thứ tự không quan trọng: A→D và D→A đều là A-D. Ultimate: hai tay cùng kết Kiếm Chỉ chỉ vào địch.
    public sealed class SealComboCaster : MonoBehaviour
    {
        [SerializeField] private HandSealRecognizer leftHand;
        [SerializeField] private HandSealRecognizer rightHand;
        [SerializeField] private PetCombatant pet;

        [Header("Ghép combo")]
        [Min(0.1f)] [SerializeField] private float comboWindow = 1f;
        [Min(0f)] [SerializeField] private float ultimateHoldSeconds = 0.5f;
        [Tooltip("Ultimate = hai tay cùng giữ ấn này (mặc định Kiếm Chỉ A, cả hai đều chỉ vào địch).")]
        [SerializeField] private SealType ultimateSeal = SealType.A;

        [Header("Tay cầm Touch")]
        [Tooltip("A = ấn A, B = ấn D, X = ấn H, Y = Ultimate.")]
        [SerializeField] private bool controllerButtons = true;
        [SerializeField] private bool logResults = true;

        [Header("Runtime (chỉ đọc)")]
        [SerializeField] private string pendingSequence = "";
        [SerializeField] private string lastResult = "";

        private readonly SealType[] sequence = new SealType[3];
        private int sequenceLength;
        private float sinceLastSeal;
        private float bothHandsHeldTime;
        private bool ultimateLatched;

        // Phát khi một ấn được ghi nhận (UI hiện ấn vừa kết).
        public event Action<SealType> SealQueued;
        // Phát khi combo được thi triển: hành động và kết quả (thành công / lý do bị từ chối).
        public event Action<CombatActionType, ActionResult> ComboCast;
        // Phát khi chuỗi ấn không tạo thành hành động hợp lệ (ví dụ A-A-D).
        public event Action InvalidCombo;
        public event Action<ActionResult> UltimateCast;

        public string PendingSequence => pendingSequence;
        public string LastResult => lastResult;

        // Gán tay và pet từ công cụ Editor.
        public void Configure(HandSealRecognizer left, HandSealRecognizer right, PetCombatant controlledPet)
        {
            leftHand = left;
            rightHand = right;
            pet = controlledPet;
        }

        // Đổi pet nhận lệnh (khi triệu hồi pet mới) và xóa chuỗi ấn đang dở.
        public void SetPet(PetCombatant controlledPet)
        {
            pet = controlledPet;
            ClearSequence();
        }

        // Đăng ký sự kiện ấn kí của hai tay.
        private void OnEnable()
        {
            if (leftHand != null) leftHand.SealPerformed += HandleSeal;
            if (rightHand != null) rightHand.SealPerformed += HandleSeal;
        }

        // Hủy đăng ký sự kiện ấn kí.
        private void OnDisable()
        {
            if (leftHand != null) leftHand.SealPerformed -= HandleSeal;
            if (rightHand != null) rightHand.SealPerformed -= HandleSeal;
        }

        // Cập nhật mục tiêu ngắm, kiểm tra Ultimate hai tay, đọc nút tay cầm và thi triển combo khi hết thời gian chờ.
        private void Update()
        {
            UpdateAimTarget();
            UpdateUltimateGesture();
            ReadControllerButtons();

            if (sequenceLength == 0)
            {
                return;
            }

            sinceLastSeal += Time.deltaTime;
            if (sinceLastSeal >= comboWindow)
            {
                Commit();
            }
        }

        // Nhận một ấn từ tay bất kỳ: thêm vào chuỗi, đủ 3 ấn thì thi triển ngay.
        private void HandleSeal(HandSealRecognizer source, SealType seal)
        {
            QueueSeal(seal);
        }

        // Kết một ấn từ nguồn ngoài (nút kiểm thử trong Editor); đi qua đúng đường xử lý như ấn tay/nút tay cầm.
        public void SubmitSeal(SealType seal)
        {
            QueueSeal(seal);
        }

        // Tung Ultimate từ nguồn ngoài (nút kiểm thử trong Editor).
        public void SubmitUltimate()
        {
            ClearSequence();
            CastUltimate();
        }

        // Thêm ấn vào chuỗi và làm mới thời gian chờ.
        private void QueueSeal(SealType seal)
        {
            if (seal == SealType.None || sequenceLength >= sequence.Length)
            {
                return;
            }

            sequence[sequenceLength++] = seal;
            sinceLastSeal = 0f;
            RefreshPendingText();
            SealQueued?.Invoke(seal);

            if (sequenceLength == sequence.Length)
            {
                Commit();
            }
        }

        // Cho ấn Kiếm Chỉ ngắm vào pet địch khi đang đánh; chưa có địch thì bộ nhận diện dùng hướng nhìn.
        private void UpdateAimTarget()
        {
            PetCombatant opponent = pet != null ? pet.Opponent : null;
            Transform target = opponent != null && opponent.IsAlive ? opponent.transform : null;
            if (leftHand != null) leftHand.AimTarget = target;
            if (rightHand != null) rightHand.AimTarget = target;
        }

        // Ultimate: hai tay cùng giữ ultimateSeal đủ ultimateHoldSeconds; chuỗi ấn vừa ghi bởi hai tay bị hủy.
        private void UpdateUltimateGesture()
        {
            if (leftHand == null || rightHand == null)
            {
                return;
            }

            bool sameSeal = leftHand.HeldSeal == ultimateSeal && rightHand.HeldSeal == ultimateSeal;
            if (!sameSeal)
            {
                bothHandsHeldTime = 0f;
                ultimateLatched = false;
                return;
            }

            bothHandsHeldTime += Time.deltaTime;
            if (!ultimateLatched && bothHandsHeldTime >= ultimateHoldSeconds)
            {
                ultimateLatched = true;
                ClearSequence();
                CastUltimate();
            }
        }

        // Khi cầm tay cầm Touch: nút mặt thay cho ấn kí tay (tay cầm không tạo được tư thế ngón riêng lẻ).
        // A (phải) = ấn A, B (phải) = ấn D, X (trái) = ấn H, Y (trái) = Ultimate. Grip/trigger để rig ISDK cầm và trỏ.
        private void ReadControllerButtons()
        {
            if (!controllerButtons)
            {
                return;
            }

            if (OVRInput.GetDown(OVRInput.Button.One, OVRInput.Controller.RTouch)) QueueSeal(SealType.A);
            if (OVRInput.GetDown(OVRInput.Button.Two, OVRInput.Controller.RTouch)) QueueSeal(SealType.D);
            if (OVRInput.GetDown(OVRInput.Button.One, OVRInput.Controller.LTouch)) QueueSeal(SealType.H);
            if (OVRInput.GetDown(OVRInput.Button.Two, OVRInput.Controller.LTouch)) CastUltimate();
        }

        // Chuyển chuỗi ấn thành hành động và ra lệnh cho pet; chuỗi sai thì báo và bỏ.
        private void Commit()
        {
            bool valid = TryResolve(out CombatActionType action);
            string sequenceText = pendingSequence;
            ClearSequence();

            if (!valid)
            {
                Report($"Invalid {sequenceText}", $"Chuỗi ấn không hợp lệ: {sequenceText}");
                InvalidCombo?.Invoke();
                return;
            }

            ActionResult result = pet != null ? pet.TryUseAction(action) : ActionResult.MatchNotRunning;
            Report($"{sequenceText} -> {action}: {result}", $"{sequenceText} → {action}: {result}");
            ComboCast?.Invoke(action, result);
        }

        // Ra lệnh Ultimate cho pet và báo kết quả.
        private void CastUltimate()
        {
            ActionResult result = pet != null ? pet.TryUseUltimate() : ActionResult.MatchNotRunning;
            Report($"Ultimate: {result}", $"Ultimate: {result}");
            UltimateCast?.Invoke(result);
        }

        // Đếm số ấn A/D/H rồi tra bảng: 1 ấn = orb đơn, 2 ấn = combo đôi, 3 ấn khác nhau = A-D-H (Skill).
        private bool TryResolve(out CombatActionType action)
        {
            action = CombatActionType.A;
            int a = 0, d = 0, h = 0;
            for (int i = 0; i < sequenceLength; i++)
            {
                switch (sequence[i])
                {
                    case SealType.A: a++; break;
                    case SealType.D: d++; break;
                    case SealType.H: h++; break;
                }
            }

            switch (sequenceLength)
            {
                case 1:
                    action = a == 1 ? CombatActionType.A : d == 1 ? CombatActionType.D : CombatActionType.H;
                    return true;
                case 2:
                    if (a == 2) { action = CombatActionType.AA; return true; }
                    if (d == 2) { action = CombatActionType.DD; return true; }
                    if (h == 2) { action = CombatActionType.HH; return true; }
                    if (a == 1 && d == 1) { action = CombatActionType.AD; return true; }
                    if (a == 1 && h == 1) { action = CombatActionType.AH; return true; }
                    action = CombatActionType.DH;
                    return true;
                case 3:
                    action = CombatActionType.ADH;
                    return a == 1 && d == 1 && h == 1;
                default:
                    return false;
            }
        }

        // Xóa chuỗi ấn đang chờ.
        private void ClearSequence()
        {
            sequenceLength = 0;
            sinceLastSeal = 0f;
            pendingSequence = "";
        }

        // Cập nhật chuỗi hiển thị dạng "A-D" cho Inspector và UI debug.
        private void RefreshPendingText()
        {
            pendingSequence = sequenceLength switch
            {
                1 => $"{sequence[0]}",
                2 => $"{sequence[0]}-{sequence[1]}",
                3 => $"{sequence[0]}-{sequence[1]}-{sequence[2]}",
                _ => ""
            };
        }

        // Lưu kết quả gần nhất (ASCII để nhãn TMP trong kính hiển thị được) và ghi log tiếng Việt cho Console/Logcat.
        private void Report(string asciiSummary, string logMessage)
        {
            lastResult = asciiSummary;
            if (logResults)
            {
                Debug.Log($"[Seal] {logMessage}", this);
            }
        }
    }
}
