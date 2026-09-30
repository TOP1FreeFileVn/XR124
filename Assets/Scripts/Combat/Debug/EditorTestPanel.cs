using UnityEngine;

namespace XR124.Combat
{
    // Bảng nút kiểm thử nhanh trong Game view khi chạy Play trong Unity Editor (không có kính/tay cầm): mở kho đồ,
    // trang bị/cất/rút kiếm, cắm kiếm trước mặt, kết ấn A/D/H và Ultimate. Mọi nút gọi đúng API gameplay thật
    // (WristInventory, SummonSword, SealComboCaster) nên kết quả giống khi chơi trên kính.
    // Chỉ tự tạo trong Editor (RuntimeInitializeOnLoadMethod bọc #if UNITY_EDITOR) nên không xuất hiện trong bản build.
    public sealed class EditorTestPanel : MonoBehaviour
    {
        [Tooltip("Kiếm được cắm cách người chơi đoạn này (m) theo hướng nhìn khi bấm nút Cắm kiếm.")]
        [SerializeField] private float plantDistance = 1.2f;
        [Tooltip("Phóng to bảng nút cho dễ bấm.")]
        [SerializeField] private float uiScale = 1.4f;

        private WristInventory inventory;
        private SummonSword sword;
        private SealComboCaster caster;
        private BattleSummoner summoner;
        private string lastAction = "";

#if UNITY_EDITOR
        // Tự thêm bảng nút sau khi scene nạp trong Play mode của Editor (scene không cần sửa gì).
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void CreateInEditor()
        {
            if (FindFirstObjectByType<EditorTestPanel>() != null)
            {
                return;
            }

            GameObject host = new GameObject("[EditorTestPanel]");
            DontDestroyOnLoad(host);
            host.AddComponent<EditorTestPanel>();
        }
#endif

        // Tìm lại tham chiếu mỗi lần vẽ nếu còn thiếu (kiếm có thể đang tắt trong túi nên tìm cả object tắt).
        private void ResolveReferences()
        {
            if (inventory == null) inventory = FindFirstObjectByType<WristInventory>();
            if (sword == null) sword = FindFirstObjectByType<SummonSword>(FindObjectsInactive.Include);
            if (caster == null) caster = FindFirstObjectByType<SealComboCaster>();
            if (summoner == null) summoner = FindFirstObjectByType<BattleSummoner>();
        }

        // Vẽ bảng nút ở góc trái màn hình Game view và dòng trạng thái hiện tại của kiếm/trận.
        private void OnGUI()
        {
            ResolveReferences();
            Matrix4x4 previous = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(uiScale, uiScale, 1f));
            GUILayout.BeginArea(new Rect(10f, 10f, 230f, 520f), "XR124 – Test (Editor)", GUI.skin.window);

            if (inventory != null && GUILayout.Button(inventory.IsOpen ? "Đóng kho đồ" : "Mở kho đồ"))
            {
                inventory.SetOpen(!inventory.IsOpen);
                lastAction = "Kho đồ";
            }

            if (sword != null)
            {
                GUILayout.Space(4f);
                if (GUILayout.Button(sword.State == SummonSword.SwordState.Planted ? "Rút kiếm về tay" : "Trang bị kiếm vào tay"))
                {
                    lastAction = sword.EquipToHand() ? "Kiếm đã vào tay phải" : "Không gắn được kiếm (thiếu RightHandAnchor)";
                }

                if (GUILayout.Button("Cắm kiếm trước mặt"))
                {
                    PlantInFront();
                }

                if (GUILayout.Button("Cất kiếm vào túi"))
                {
                    lastAction = sword.Store() ? "Đã cất kiếm" : "Chưa cất được (kiếm đang cắm hoặc đang bị nắm)";
                }
            }

            if (caster != null)
            {
                GUILayout.Space(4f);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Ấn A")) Seal(SealType.A);
                if (GUILayout.Button("Ấn D")) Seal(SealType.D);
                if (GUILayout.Button("Ấn H")) Seal(SealType.H);
                GUILayout.EndHorizontal();
                if (GUILayout.Button("Ultimate"))
                {
                    caster.SubmitUltimate();
                    lastAction = "Ultimate: " + caster.LastResult;
                }

                GUILayout.Label("Chuỗi ấn: " + (string.IsNullOrEmpty(caster.PendingSequence) ? "-" : caster.PendingSequence));
                GUILayout.Label("Kết quả: " + caster.LastResult);
            }

            GUILayout.Space(4f);
            if (sword != null) GUILayout.Label("Kiếm: " + (sword.IsStored ? "trong túi" : sword.DebugState));
            if (summoner != null) GUILayout.Label("Trận: " + summoner.State + "\n" + summoner.LastMessage);
            if (!string.IsNullOrEmpty(lastAction)) GUILayout.Label("› " + lastAction);

            GUILayout.EndArea();
            GUI.matrix = previous;
        }

        // Kết một ấn qua SealComboCaster (ghép combo sau 1 giây như khi chơi thật).
        private void Seal(SealType seal)
        {
            caster.SubmitSeal(seal);
            lastAction = "Kết ấn " + seal;
        }

        // Cắm kiếm tại điểm trên sàn trước mặt camera: kiếm phải đang ở ngoài túi; chạy đúng luồng cắm → triệu hồi.
        private void PlantInFront()
        {
            if (sword.IsStored && !sword.EquipToHand())
            {
                lastAction = "Chưa lấy được kiếm ra";
                return;
            }

            Camera cam = Camera.main;
            Vector3 origin = cam != null ? cam.transform.position : Vector3.zero;
            Vector3 forward = cam != null ? Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up) : Vector3.forward;
            forward = forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.forward;
            Vector3 point = new Vector3(origin.x, 0f, origin.z) + forward * plantDistance;
            lastAction = sword.PlantAt(point) ? "Đã cắm kiếm" : "Cắm bị từ chối: " + sword.DebugState;
        }
    }
}
