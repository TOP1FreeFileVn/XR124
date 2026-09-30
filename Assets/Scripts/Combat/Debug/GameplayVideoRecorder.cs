using System.Collections;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace XR124.Combat
{
    // Công cụ quay video gameplay trong Play mode (chỉ dùng để làm video giới thiệu/kiểm thử, không phải gameplay).
    // Chạy một kịch bản theo đúng luồng thật: lấy kiếm ra → đâm xuống sàn (SummonSword tự phát hiện cắm) → BattleSummoner
    // triệu hồi → kết ấn qua SealComboCaster → trận tự đánh. Camera điện ảnh riêng render vào RenderTexture, mỗi frame ghi
    // một ảnh JPG; Time.captureFramerate khóa thời gian game theo fps nên video mượt dù máy render chậm.
    // Sự kiện (ấn, combo, ultimate, kết thúc) được ghi ra file phụ đề SRT để ghép vào video bằng ffmpeg.
    public sealed class GameplayVideoRecorder : MonoBehaviour
    {
        [Header("Xuất video")]
        [SerializeField] private int width = 1280;
        [SerializeField] private int height = 720;
        [SerializeField] private int fps = 30;
        [Range(50, 100)] [SerializeField] private int jpgQuality = 90;
        [Tooltip("Thời lượng tối đa của pha đánh nhau (giây game) trước khi dừng quay.")]
        [SerializeField] private float maxFightSeconds = 60f;
        [Tooltip("Camera không ra xa tâm đấu trường quá bán kính này (m) để khỏi chui vào lan can/cột.")]
        [SerializeField] private float cameraMaxRadius = 4.1f;

        [Header("Runtime (chỉ đọc)")]
        [SerializeField] private string outputFolder;
        [SerializeField] private int frameCount;
        [SerializeField] private string phase = "Chưa chạy";

        private enum Shot { Intro, Sword, Summon, Fight, Outro }

        private BattleSummoner summoner;
        private SummonSword sword;
        private SealComboCaster caster;
        private Camera shotCamera;
        private RenderTexture target;
        private Texture2D readback;
        private Shot shot = Shot.Intro;
        private float shotTime;
        private float orbitAngle;
        private bool finished;
        private readonly StringBuilder subtitles = new StringBuilder();
        private int subtitleIndex;

        public bool IsFinished => finished;
        public string OutputFolder => outputFolder;
        public int FrameCount => frameCount;

        // Tìm các thành phần trận, tạo camera + RenderTexture, khóa fps và bắt đầu kịch bản.
        private void Start()
        {
            summoner = FindFirstObjectByType<BattleSummoner>();
            sword = FindFirstObjectByType<SummonSword>(FindObjectsInactive.Include);
            caster = FindFirstObjectByType<SealComboCaster>();
            if (summoner == null || sword == null || caster == null)
            {
                Debug.LogError("[Video] Thiếu BattleSummoner/SummonSword/SealComboCaster trong scene; không quay được.", this);
                enabled = false;
                return;
            }

            outputFolder = Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", "Recordings",
                System.DateTime.Now.ToString("yyyyMMdd_HHmmss"));
            Directory.CreateDirectory(outputFolder);

            target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            readback = new Texture2D(width, height, TextureFormat.RGB24, false);
            GameObject cameraObject = new GameObject("VideoCamera");
            shotCamera = cameraObject.AddComponent<Camera>();
            shotCamera.targetTexture = target;
            shotCamera.fieldOfView = 50f;
            shotCamera.nearClipPlane = 0.05f;

            caster.SealQueued += HandleSealQueued;
            caster.ComboCast += HandleComboCast;
            caster.UltimateCast += HandleUltimateCast;

            Time.captureFramerate = fps;
            StartCoroutine(RunScript());
            StartCoroutine(CaptureFrames());
        }

        // Trả lại thời gian thực, giải phóng texture và hủy nghe sự kiện.
        private void OnDestroy()
        {
            Time.captureFramerate = 0;
            if (caster != null)
            {
                caster.SealQueued -= HandleSealQueued;
                caster.ComboCast -= HandleComboCast;
                caster.UltimateCast -= HandleUltimateCast;
            }

            if (target != null) target.Release();
            if (shotCamera != null) Destroy(shotCamera.gameObject);
        }

        // Kịch bản: toàn cảnh → lấy kiếm → đâm kiếm → triệu hồi → chuỗi ấn/ultimate → kết thúc.
        private IEnumerator RunScript()
        {
            SetShot(Shot.Intro, "Toàn cảnh đấu trường");
            AddSubtitle(0.3f, 2.6f, "XR124 – Đấu trường triệu hồi · quái địch tự xuất hiện");
            yield return new WaitForSeconds(3f);

            while (summoner.State == BattleSummoner.SummonState.WaitingForArena)
            {
                yield return null;
            }

            // Lấy kiếm từ túi đồ: đặt trước mặt người chơi, mũi kiếm cao hơn sàn.
            Transform head = Camera.main != null ? Camera.main.transform : transform;
            Vector3 forward = Vector3.ProjectOnPlane(head.forward, Vector3.up);
            forward = forward.sqrMagnitude > 0.001f ? forward.normalized : Vector3.forward;
            Vector3 swordPos = new Vector3(head.position.x, 0f, head.position.z) + forward * 1.1f + Vector3.up * 1.0f;
            sword.TakeOut(swordPos, Quaternion.LookRotation(forward, Vector3.up));
            SetShot(Shot.Sword, "Lấy kiếm");
            AddSubtitle(Now, 1.5f, "Lấy kiếm triệu hồi từ túi đồ trên cổ tay");
            yield return new WaitForSeconds(1.2f);

            // Giả lập tay cầm kiếm (trạng thái Held) rồi đâm xuống; SummonSword tự cắm khi mũi chạm sàn đủ nhanh.
            SetSwordHeld();
            AddSubtitle(Now, 1.6f, "Đâm kiếm xuống sàn → mở pháp trận");
            float plungeTimeout = 3f;
            while (sword.State != SummonSword.SwordState.Planted && plungeTimeout > 0f)
            {
                sword.transform.position += Vector3.down * 1.6f * Time.deltaTime;
                plungeTimeout -= Time.deltaTime;
                yield return null;
            }

            if (sword.State != SummonSword.SwordState.Planted)
            {
                Debug.LogWarning($"[Video] Kiếm không cắm được: {sword.DebugState}", this);
            }

            SetShot(Shot.Summon, "Triệu hồi");
            AddSubtitle(Now + 0.5f, 3f, "Pháp trận mở – chó của bạn nhảy lên, đáp đất và gầm");
            float summonTimeout = 10f;
            while (summoner.State != BattleSummoner.SummonState.Fighting && summonTimeout > 0f)
            {
                summonTimeout -= Time.deltaTime;
                yield return null;
            }

            SetShot(Shot.Fight, "Chiến đấu");
            AddSubtitle(Now, 2f, "Pet tự di chuyển và đánh thường – người chơi kết ấn để ra lệnh");
            // Chuỗi ấn mẫu (giây sau khi vào trận, chuỗi ấn); 'U' = Ultimate hai tay Kiếm Chỉ.
            (float at, string seals)[] plan =
            {
                (2.5f, "AA"), (6f, "D"), (9.5f, "ADH"), (13f, "HH"), (16.5f, "AD"), (20f, "U"), (23.5f, "AA"), (27f, "DH"),
                (31f, "ADH"), (35f, "U"), (39f, "AA"), (43f, "HH"), (47f, "U"), (51f, "AD"), (55f, "U")
            };
            float fightStart = Time.time;
            int next = 0;
            while (summoner.State == BattleSummoner.SummonState.Fighting && Time.time - fightStart < maxFightSeconds)
            {
                if (next < plan.Length && Time.time - fightStart >= plan[next].at)
                {
                    yield return CastSeals(plan[next].seals);
                    next++;
                }

                yield return null;
            }

            SetShot(Shot.Outro, "Kết thúc");
            PetCombatant player = summoner.PlayerPet;
            string ending = summoner.State == BattleSummoner.SummonState.Finished
                ? (player != null && player.IsAlive ? "Chiến thắng!" : "Thất bại!")
                : "Trận đấu tiếp diễn…";
            AddSubtitle(Now, 3f, ending);
            yield return new WaitForSeconds(3.2f);

            finished = true;
            File.WriteAllText(Path.Combine(outputFolder, "events.srt"), subtitles.ToString(), new UTF8Encoding(false));
            Debug.Log($"[Video] Xong: {frameCount} frame tại {outputFolder}", this);
            Time.captureFramerate = 0;
        }

        // Kết ấn từng ký tự qua đúng hàm xử lý ấn của SealComboCaster (giống khi tay/tay cầm kết ấn); 'U' = Ultimate.
        private IEnumerator CastSeals(string seals)
        {
            for (int i = 0; i < seals.Length; i++)
            {
                if (seals[i] == 'U')
                {
                    InvokePrivate(caster, "CastUltimate");
                }
                else
                {
                    SealType seal = seals[i] == 'A' ? SealType.A : seals[i] == 'D' ? SealType.D : SealType.H;
                    InvokePrivate(caster, "QueueSeal", seal);
                }

                yield return new WaitForSeconds(0.35f);
            }
        }

        // Đặt kiếm vào trạng thái đang cầm (thay cho sự kiện Select của Interaction SDK) để luồng cắm kiếm thật chạy.
        private void SetSwordHeld()
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            FieldInfo stateField = typeof(SummonSword).GetField("state", flags);
            FieldInfo tipField = typeof(SummonSword).GetField("tip", flags);
            FieldInfo lastTipField = typeof(SummonSword).GetField("lastTipPosition", flags);
            stateField?.SetValue(sword, SummonSword.SwordState.Held);
            Transform tip = tipField?.GetValue(sword) as Transform;
            lastTipField?.SetValue(sword, tip != null ? tip.position : sword.transform.position);
        }

        // Gọi hàm private theo tên (công cụ quay không sửa API của lớp gameplay).
        private static void InvokePrivate(object instance, string method, params object[] args)
        {
            MethodInfo info = instance.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic);
            if (info == null)
            {
                Debug.LogWarning($"[Video] Không tìm thấy hàm {method}");
                return;
            }

            info.Invoke(instance, args);
        }

        // Đổi cảnh quay và ghi lại mốc thời gian để camera chuyển mượt.
        private void SetShot(Shot next, string label)
        {
            shot = next;
            shotTime = 0f;
            phase = label;
        }

        // Điều khiển camera theo cảnh: bay vòng toàn cảnh, cận kiếm, nhìn cổng triệu hồi, xoay quanh hai pet.
        // Vị trí/hướng được nội suy mượt về đích mỗi frame để các cú chuyển cảnh không bị giật.
        private void LateUpdate()
        {
            if (shotCamera == null)
            {
                return;
            }

            shotTime += Time.deltaTime;
            Vector3 desiredPos;
            Vector3 lookAt;
            Vector3 center = summoner.transform.position;
            PetCombatant a = summoner.PlayerPet;
            PetCombatant b = summoner.EnemyPet;
            Vector3 fightCenter = a != null && b != null ? (a.transform.position + b.transform.position) * 0.5f : sword.transform.position;
            fightCenter.y = 0f;

            switch (shot)
            {
                case Shot.Intro:
                    orbitAngle = 200f + shotTime * 14f;
                    desiredPos = center + Quaternion.Euler(0f, orbitAngle, 0f) * new Vector3(0f, 3.6f, -7.5f);
                    lookAt = center + Vector3.up * 0.4f;
                    break;
                case Shot.Sword:
                    desiredPos = sword.transform.position + new Vector3(1.2f, 0.1f, -1.4f);
                    lookAt = sword.transform.position + Vector3.down * 0.4f;
                    break;
                case Shot.Summon:
                    // Pet người chơi chưa hiện: nhắm giữa kiếm (pháp trận) và quái địch đang đứng chờ.
                    Vector3 summonCenter = b != null ? (sword.transform.position + b.transform.position) * 0.5f : sword.transform.position;
                    summonCenter.y = 0f;
                    desiredPos = summonCenter + new Vector3(2.4f, 1.5f, -2.2f);
                    lookAt = summonCenter + Vector3.up * 0.3f;
                    break;
                case Shot.Fight:
                    orbitAngle += Time.deltaTime * 9f;
                    desiredPos = fightCenter + Quaternion.Euler(0f, orbitAngle, 0f) * new Vector3(0f, 1.3f, -3.1f);
                    lookAt = fightCenter + Vector3.up * 0.45f;
                    break;
                default:
                    desiredPos = fightCenter + Quaternion.Euler(0f, orbitAngle, 0f) * new Vector3(0f, 2.6f, -5f);
                    lookAt = fightCenter + Vector3.up * 0.4f;
                    break;
            }

            // Cảnh trong trận: giữ camera bên trong lan can, rồi kéo lại gần nếu có vật cản (cột, lan can) chắn giữa camera và pet.
            if (shot != Shot.Intro)
            {
                Vector3 flat = desiredPos - center;
                float y = flat.y;
                flat.y = 0f;
                if (flat.magnitude > cameraMaxRadius)
                {
                    flat = flat.normalized * cameraMaxRadius;
                }

                desiredPos = center + flat + Vector3.up * y;
                Vector3 toCamera = desiredPos - lookAt;
                if (Physics.SphereCast(lookAt, 0.15f, toCamera.normalized, out RaycastHit hit, toCamera.magnitude,
                        Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                    && hit.transform.root != summoner.transform.root)
                {
                    desiredPos = lookAt + toCamera.normalized * Mathf.Max(0.8f, hit.distance - 0.1f);
                }
            }

            // Frame đầu đặt thẳng tới đích; sau đó bám theo với độ trễ ~0.3 s.
            float blend = frameCount == 0 ? 1f : 1f - Mathf.Exp(-Time.deltaTime * 3.5f);
            Transform cam = shotCamera.transform;
            cam.position = Vector3.Lerp(cam.position, desiredPos, blend);
            Quaternion desiredRot = Quaternion.LookRotation(lookAt - cam.position, Vector3.up);
            cam.rotation = Quaternion.Slerp(cam.rotation, desiredRot, blend);
        }

        // Cuối mỗi frame: đọc RenderTexture của camera quay và ghi ra JPG đánh số liên tục.
        private IEnumerator CaptureFrames()
        {
            WaitForEndOfFrame endOfFrame = new WaitForEndOfFrame();
            while (!finished)
            {
                yield return endOfFrame;
                RenderTexture previous = RenderTexture.active;
                RenderTexture.active = target;
                readback.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
                readback.Apply(false);
                RenderTexture.active = previous;
                File.WriteAllBytes(Path.Combine(outputFolder, $"frame_{frameCount:D5}.jpg"), readback.EncodeToJPG(jpgQuality));
                frameCount++;
            }
        }

        private float Now => frameCount / (float)fps;

        // Ghi sự kiện ấn/combo/ultimate thành phụ đề.
        private void HandleSealQueued(SealType seal)
        {
            string name = seal == SealType.A ? "Kiếm Chỉ (A)" : seal == SealType.D ? "Thuẫn Chưởng (D)" : "Tâm Ấn (H)";
            AddSubtitle(Now, 0.9f, $"Kết ấn: {name}");
        }

        private void HandleComboCast(CombatActionType action, ActionResult result)
        {
            AddSubtitle(Now, 1.6f, $"Combo {action} → {result}");
        }

        private void HandleUltimateCast(ActionResult result)
        {
            AddSubtitle(Now, 1.8f, $"ULTIMATE (hai tay Kiếm Chỉ) → {result}");
        }

        // Thêm một dòng phụ đề SRT (thời gian theo frame đã quay).
        private void AddSubtitle(float start, float duration, string text)
        {
            subtitleIndex++;
            subtitles.Append(subtitleIndex).Append('\n')
                .Append(FormatTime(start)).Append(" --> ").Append(FormatTime(start + duration)).Append('\n')
                .Append(text).Append("\n\n");
        }

        private static string FormatTime(float seconds)
        {
            System.TimeSpan t = System.TimeSpan.FromSeconds(Mathf.Max(0f, seconds));
            return $"{t.Hours:00}:{t.Minutes:00}:{t.Seconds:00},{t.Milliseconds:000}";
        }
    }
}
