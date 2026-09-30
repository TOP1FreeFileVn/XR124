using System.Collections;
using UnityEngine;

namespace XR124.Combat
{
    // Điều phối luồng: chờ đấu trường sẵn sàng → quái địch tự xuất hiện đứng chờ trước mặt người chơi → chờ cắm kiếm
    // → mở pháp trận (shader MagicCircle) → pet người chơi nhảy lên từ pháp trận → bắt đầu trận → kết thúc → rút kiếm để gọi lại.
    // Chỉ pet người chơi được triệu hồi; địch không cần gọi. Mọi vị trí spawn đều qua ArenaPlacement trước khi dùng.
    public sealed class BattleSummoner : MonoBehaviour
    {
        public enum SummonState : byte
        {
            WaitingForArena,
            WaitingForSword,
            Summoning,
            Fighting,
            Finished
        }

        [Header("Tham chiếu")]
        [SerializeField] private ArenaPlacement placement;
        [SerializeField] private SummonSword sword;
        [SerializeField] private CombatMatch match;
        [SerializeField] private SealComboCaster caster;
        [SerializeField] private PetCombatant playerPet;
        [SerializeField] private PetCombatant enemyPet;
        [SerializeField] private SummonPortal playerPortal;

        [Header("Vị trí")]
        [Tooltip("Pet người chơi xuất hiện cách kiếm đoạn này, về phía xa người chơi.")]
        [Min(0f)] [SerializeField] private float playerPetOffsetFromSword = 0.5f;
        [Tooltip("Quái địch tự đứng chờ cách người chơi đoạn này (m) theo hướng nhìn lúc đấu trường sẵn sàng.")]
        [Min(0.5f)] [SerializeField] private float enemyDistanceFromOwner = 3f;

        [Header("Nhảy ra từ cổng (khớp clip Zeru ở 30 fps)")]
        [Tooltip("Pet nằm dưới sàn trong cổng lúc lấy đà (start_jumping 0.5s).")]
        [Min(0f)] [SerializeField] private float prepareSeconds = 0.5f;
        [Tooltip("Bay vọt lên từ dưới sàn tới đỉnh (jump 0.87s).")]
        [Min(0.05f)] [SerializeField] private float riseSeconds = 0.87f;
        [Tooltip("Rơi từ đỉnh xuống sàn; animation falling lặp trong suốt pha này.")]
        [Min(0.05f)] [SerializeField] private float fallSeconds = 0.5f;
        [Min(0f)] [SerializeField] private float emergeDepth = 0.6f;
        [Tooltip("Độ cao đỉnh cú nhảy so với mặt sàn (m).")]
        [Min(0f)] [SerializeField] private float apexHeight = 0.6f;
        [Tooltip("Tiếp đất và xoay mặt về phía chủ (landing 0.47s).")]
        [Min(0f)] [SerializeField] private float landingSeconds = 0.47f;
        [Tooltip("Gầm trước khi vào trận (roar 2.2s).")]
        [Min(0f)] [SerializeField] private float roarSeconds = 2.2f;

        [Header("Runtime (chỉ đọc)")]
        [SerializeField] private SummonState state = SummonState.WaitingForArena;
        [SerializeField] private string lastMessage = "";

        public SummonState State => state;
        public string LastMessage => lastMessage;
        public PetCombatant PlayerPet => playerPet;
        public PetCombatant EnemyPet => enemyPet;

        // Gán tham chiếu từ công cụ Editor.
        public void Configure(ArenaPlacement floorPlacement, SummonSword summonSword, CombatMatch combatMatch, SealComboCaster sealCaster,
            PetCombatant player, PetCombatant enemy, SummonPortal portalPlayer)
        {
            placement = floorPlacement;
            sword = summonSword;
            match = combatMatch;
            caster = sealCaster;
            playerPet = player;
            enemyPet = enemy;
            playerPortal = portalPlayer;
        }

        // Ẩn pet người chơi tới khi được triệu hồi; địch cũng ẩn tới khi đấu trường sẵn sàng rồi mới tự xuất hiện.
        private void Awake()
        {
            SetVisible(playerPet, false);
            SetVisible(enemyPet, false);
        }

        // Đăng ký sự kiện kiếm và trận.
        private void OnEnable()
        {
            if (sword != null)
            {
                sword.Planted += HandleSwordPlanted;
                sword.PulledOut += HandleSwordPulledOut;
            }

            if (match != null)
            {
                match.MatchEnded += HandleMatchEnded;
            }
        }

        // Hủy đăng ký sự kiện.
        private void OnDisable()
        {
            if (sword != null)
            {
                sword.Planted -= HandleSwordPlanted;
                sword.PulledOut -= HandleSwordPulledOut;
            }

            if (match != null)
            {
                match.MatchEnded -= HandleMatchEnded;
            }
        }

        // Chỉ chuyển sang chờ kiếm khi đấu trường đã có collider sàn.
        private void Update()
        {
            if (state == SummonState.WaitingForArena)
            {
                if (placement != null && placement.IsReady)
                {
                    SetState(SummonState.WaitingForSword, "Đấu trường sẵn sàng. Cầm kiếm và cắm xuống sàn để triệu hồi.");
                    PlaceEnemy();
                }
                else if (placement != null)
                {
                    lastMessage = $"Chờ đấu trường: {placement.ArenaState}";
                }
            }
        }

        // Quái địch tự xuất hiện: đứng trước mặt người chơi (theo hướng nhìn) ở chỗ hợp lệ gần nhất, quay mặt về người chơi.
        // Tắt rồi bật lại GameObject để Animator về trạng thái Idle (hồi lại sau trận trước). Không có chỗ thì ghi lý do.
        private void PlaceEnemy()
        {
            if (enemyPet == null)
            {
                return;
            }

            Vector3 owner = GetOwnerPosition(Vector3.zero);
            Camera cam = Camera.main;
            Vector3 forward = cam != null ? Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up) : Vector3.forward;
            forward = forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.forward;
            Vector3 requested = new Vector3(owner.x, 0f, owner.z) + forward * enemyDistanceFromOwner;

            SetVisible(enemyPet, false);
            if (!TryFindSpawn(enemyPet, requested, null, out Vector3 enemySpawn))
            {
                lastMessage = "Quái địch chưa có chỗ đứng hợp lệ (xem Console)";
                return;
            }

            enemyPet.transform.SetPositionAndRotation(enemySpawn, LookRotationFlat(enemySpawn, owner, enemyPet.transform.rotation));
            SetVisible(enemyPet, true);
        }

        // Kiếm vừa cắm: tìm chỗ đứng hợp lệ cho pet người chơi gần kiếm (quái địch đang đứng sẵn nên collider của nó
        // được tính là vật cản); không tìm được hoặc quá sát địch thì rút kiếm và báo lý do.
        private void HandleSwordPlanted(SummonSword source, Vector3 floorPoint)
        {
            if (state != SummonState.WaitingForSword)
            {
                lastMessage = $"Bỏ qua cắm kiếm ở trạng thái {state}";
                return;
            }

            if (enemyPet == null || !enemyPet.gameObject.activeInHierarchy)
            {
                FailSummon("Quái địch chưa xuất hiện trên đấu trường");
                return;
            }

            Vector3 away = GetDirectionAwayFromViewer(floorPoint);
            if (!TryFindSpawn(playerPet, floorPoint + away * playerPetOffsetFromSword, null, out Vector3 playerSpawn))
            {
                FailSummon("Không có chỗ đứng hợp lệ cho pet của bạn gần kiếm");
                return;
            }

            float minSeparation = GetBodyRadius(playerPet) + GetBodyRadius(enemyPet) + 0.1f;
            Vector3 separation = enemyPet.transform.position - playerSpawn;
            separation.y = 0f;
            if (separation.sqrMagnitude < minSeparation * minSeparation)
            {
                FailSummon("Cắm kiếm quá sát quái địch: pet sẽ đứng chồng lên nhau");
                return;
            }

            StartCoroutine(SummonRoutine(playerSpawn));
        }

        // Tìm vị trí FLOOR hợp lệ gần nhất cho pet theo kích thước thân trong PetDefinition.
        private bool TryFindSpawn(PetCombatant pet, Vector3 requested, Transform ignoreOther, out Vector3 spawn)
        {
            PetDefinition definition = pet.Definition;
            float radius = definition != null ? definition.bodyRadius : 0.3f;
            float height = definition != null ? definition.bodyHeight : 0.8f;
            PlacementResult result = placement.TryFindNearestValid(requested, radius, height, pet.transform, ignoreOther != null ? ignoreOther : sword.transform, out spawn);
            if (result != PlacementResult.Valid)
            {
                Debug.Log($"[Summon] {pet.DisplayName}: {placement.LastRejection}", this);
                return false;
            }

            return true;
        }

        // Chuỗi triệu hồi pet người chơi: mở pháp trận → lấy đà dưới sàn (start_jumping) → vọt lên (jump) → rơi (falling, lặp)
        // → chạm sàn thì bắn trigger Land (landing), pet xoay mặt về chủ, quái địch quay sang nhìn pet → gầm (roar) → bắt đầu trận.
        private IEnumerator SummonRoutine(Vector3 playerSpawn)
        {
            SetState(SummonState.Summoning, "Mở pháp trận...");
            if (playerPortal != null) playerPortal.Open(playerSpawn);

            float wait = playerPortal != null ? playerPortal.OpenSeconds : 0.3f;
            yield return new WaitForSeconds(wait);

            Vector3 enemySpawn = enemyPet.transform.position;
            PlaceBelowFloor(playerPet, playerSpawn, enemySpawn);
            SetVisible(playerPet, true);
            PlaySummonAnimation(playerPet);

            // Lấy đà: pet vẫn nằm dưới sàn trong lòng pháp trận.
            yield return new WaitForSeconds(prepareSeconds);

            // Vọt lên: giảm tốc dần khi gần đỉnh (ease-out), như bị trọng lực hãm lại.
            for (float elapsed = 0f; elapsed < riseSeconds; elapsed += Time.deltaTime)
            {
                float t = elapsed / riseSeconds;
                SetHeight(playerPet, playerSpawn, Mathf.Lerp(-emergeDepth, apexHeight, 1f - (1f - t) * (1f - t)));
                yield return null;
            }

            // Rơi: tăng tốc dần (ease-in); animation falling đang lặp cho tới khi chạm sàn.
            for (float elapsed = 0f; elapsed < fallSeconds; elapsed += Time.deltaTime)
            {
                float t = elapsed / fallSeconds;
                SetHeight(playerPet, playerSpawn, apexHeight * (1f - t * t));
                yield return null;
            }

            playerPet.transform.position = playerSpawn;
            PlayLandingAnimation(playerPet);
            if (playerPortal != null) playerPortal.Close();

            // Tiếp đất: pet người chơi xoay mặt về phía chủ (đầu người chơi), quái địch quay sang nhìn pet người chơi.
            Quaternion playerStart = playerPet.transform.rotation;
            Quaternion enemyStart = enemyPet.transform.rotation;
            Quaternion playerTarget = LookRotationFlat(playerSpawn, GetOwnerPosition(playerSpawn), playerStart);
            Quaternion enemyTarget = LookRotationFlat(enemySpawn, playerSpawn, enemyStart);
            for (float elapsed = 0f; elapsed < landingSeconds; elapsed += Time.deltaTime)
            {
                float t = Mathf.SmoothStep(0f, 1f, elapsed / landingSeconds);
                playerPet.transform.rotation = Quaternion.Slerp(playerStart, playerTarget, t);
                enemyPet.transform.rotation = Quaternion.Slerp(enemyStart, enemyTarget, t);
                yield return null;
            }

            playerPet.transform.rotation = playerTarget;
            enemyPet.transform.rotation = enemyTarget;

            // Gầm về phía chủ; Animator tự chuyển landing → roar → idle theo exit time.
            yield return new WaitForSeconds(roarSeconds);

            if (caster != null)
            {
                caster.SetPet(playerPet);
            }

            match.BeginMatch();
            SetState(match.IsRunning ? SummonState.Fighting : SummonState.WaitingForSword,
                match.IsRunning ? "Chiến đấu! Kết ấn để ra lệnh." : "Không bắt đầu được trận (xem Console).");
        }

        // Đặt pet dưới sàn tại cổng và quay mặt về phía đối thủ.
        private void PlaceBelowFloor(PetCombatant pet, Vector3 spawn, Vector3 lookAt)
        {
            Vector3 look = lookAt - spawn;
            look.y = 0f;
            Quaternion rotation = look.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(look, Vector3.up) : pet.transform.rotation;
            pet.transform.SetPositionAndRotation(spawn + Vector3.down * emergeDepth, rotation);
        }

        // Đặt pet ở độ cao tương đối so với điểm sàn tại cổng (âm = còn dưới sàn).
        private static void SetHeight(PetCombatant pet, Vector3 spawn, float height)
        {
            pet.transform.position = spawn + Vector3.up * height;
        }

        // Hướng quay mặt trên mặt phẳng ngang từ from tới to; trùng vị trí thì giữ hướng cũ.
        private static Quaternion LookRotationFlat(Vector3 from, Vector3 to, Quaternion fallback)
        {
            Vector3 look = to - from;
            look.y = 0f;
            return look.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(look, Vector3.up) : fallback;
        }

        // Vị trí "chủ" = đầu người chơi (camera chính); không có camera thì nhìn ra sau điểm spawn.
        private static Vector3 GetOwnerPosition(Vector3 spawn)
        {
            Camera cam = Camera.main;
            return cam != null ? cam.transform.position : spawn - Vector3.forward;
        }

        // Báo pet đã chạm sàn để Animator chuyển falling → landing.
        private static void PlayLandingAnimation(PetCombatant pet)
        {
            PetAutoBattler battler = pet != null ? pet.GetComponent<PetAutoBattler>() : null;
            if (battler != null)
            {
                battler.PlayLanding();
            }
        }

        // Trận kết thúc: chờ người chơi rút kiếm để gọi trận mới.
        private void HandleMatchEnded(CombatMatch endedMatch, PetCombatant winner)
        {
            string result = winner == playerPet ? "Bạn thắng!" : "Bạn thua!";
            SetState(SummonState.Finished, $"{result} Rút kiếm để triệu hồi lại.");
        }

        // Rút kiếm: sau trận thì thu pet người chơi, quái địch tự xuất hiện lại (hồi trạng thái) và chờ cắm kiếm;
        // đang đánh thì bỏ qua (kiếm có thể rút ra cầm).
        private void HandleSwordPulledOut(SummonSword source)
        {
            if (state != SummonState.Finished)
            {
                return;
            }

            SetVisible(playerPet, false);
            SetState(SummonState.WaitingForSword, "Cắm kiếm xuống sàn để triệu hồi.");
            PlaceEnemy();
        }

        // Triệu hồi thất bại: rút kiếm về, giữ trạng thái chờ và ghi lý do (không lặp lại vị trí cũ một cách im lặng).
        private void FailSummon(string reason)
        {
            lastMessage = $"Triệu hồi thất bại: {reason}";
            Debug.LogWarning($"[Summon] {lastMessage}", this);
            sword.ReturnHome();
        }

        // Hướng từ camera tới điểm kiếm trên mặt phẳng ngang, để pet xuất hiện phía trước người chơi.
        private static Vector3 GetDirectionAwayFromViewer(Vector3 point)
        {
            Camera cam = Camera.main;
            Vector3 away = cam != null ? point - cam.transform.position : Vector3.forward;
            away.y = 0f;
            return away.sqrMagnitude > 0.0001f ? away.normalized : Vector3.forward;
        }

        // Gọi chuỗi animation triệu hồi của pet nếu pet có PetAutoBattler.
        private static void PlaySummonAnimation(PetCombatant pet)
        {
            PetAutoBattler battler = pet != null ? pet.GetComponent<PetAutoBattler>() : null;
            if (battler != null)
            {
                battler.PlaySummon();
            }
        }

        // Bán kính thân pet từ PetDefinition (mặc định 0.3 m khi chưa gán).
        private static float GetBodyRadius(PetCombatant pet)
        {
            return pet.Definition != null ? pet.Definition.bodyRadius : 0.3f;
        }

        // Bật/tắt GameObject một pet (null thì bỏ qua).
        private static void SetVisible(PetCombatant pet, bool visible)
        {
            if (pet != null) pet.gameObject.SetActive(visible);
        }

        // Đổi trạng thái và ghi thông điệp debug.
        private void SetState(SummonState newState, string message)
        {
            state = newState;
            lastMessage = message;
            Debug.Log($"[Summon] {message}", this);
        }
    }
}
