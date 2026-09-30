using UnityEngine;

namespace XR124.Combat
{
    public enum PlacementResult : byte
    {
        Valid,
        NotReady,
        NoFloor,
        OutsideArena,
        Blocked
    }

    // Kiểm tra và tìm vị trí hợp lệ trên sàn đấu trường VR cho kiếm, pháp trận và pet.
    // Một vị trí hợp lệ khi: raycast trúng collider sàn, toàn bộ footprint nằm trong bán kính đấu trường,
    // và thân (hình capsule) không chồng collider khác (cột, đá, pet khác...).
    public sealed class ArenaPlacement : MonoBehaviour
    {
        [Header("Đấu trường")]
        [Tooltip("Collider mặt sàn; raycast chỉ tính collider này.")]
        [SerializeField] private Collider floorCollider;
        [Tooltip("Tâm đấu trường; để trống thì dùng transform này.")]
        [SerializeField] private Transform arenaCenter;
        [Min(0.5f)] [SerializeField] private float arenaRadius = 5f;
        [Min(0.1f)] [SerializeField] private float floorRayStartHeight = 3f;
        [SerializeField] private LayerMask collisionMask = ~0;

        [Header("Tìm vị trí gần nhất")]
        [Min(0.1f)] [SerializeField] private float searchRadius = 3f;
        [Min(0.05f)] [SerializeField] private float ringStep = 0.2f;
        [Range(8, 64)] [SerializeField] private int samplesPerRing = 16;

        [Header("Runtime (chỉ đọc)")]
        [SerializeField] private string lastRejection = "";
        [SerializeField] private PlacementResult lastRejectionCode = PlacementResult.Valid;

        private readonly Collider[] overlapBuffer = new Collider[32];

        public string LastRejection => lastRejection;
        public PlacementResult LastRejectionCode => lastRejectionCode;
        public bool IsReady => floorCollider != null && floorCollider.enabled;
        public string ArenaState => IsReady ? "Sẵn sàng" : "Thiếu collider sàn";
        private Vector3 Center => arenaCenter != null ? arenaCenter.position : transform.position;

        // Gán sàn và kích thước đấu trường từ công cụ Editor.
        public void Configure(Collider floor, Transform center, float radius)
        {
            floorCollider = floor;
            arenaCenter = center;
            arenaRadius = radius;
        }

        // Chiếu điểm xuống sàn bằng raycast chỉ vào collider sàn; trả về điểm nằm đúng trên mặt sàn.
        public bool TryProjectToFloor(Vector3 position, out Vector3 floorPoint)
        {
            floorPoint = position;
            if (!IsReady)
            {
                return false;
            }

            Ray ray = new Ray(new Vector3(position.x, Center.y + floorRayStartHeight, position.z), Vector3.down);
            if (!floorCollider.Raycast(ray, out RaycastHit hit, floorRayStartHeight * 2f))
            {
                return false;
            }

            floorPoint = hit.point;
            return true;
        }

        // Kiểm tra footprint hình trụ (bán kính, chiều cao) đặt trên floorPoint; bỏ qua collider sàn và collider thuộc ignoreA/ignoreB.
        public PlacementResult Validate(Vector3 floorPoint, float radius, float height, Transform ignoreA = null, Transform ignoreB = null)
        {
            if (!IsReady)
            {
                return Reject(PlacementResult.NotReady, floorPoint);
            }

            // Toàn bộ thân phải nằm trong vòng đấu trường (khoảng cách ngang + bán kính ≤ bán kính đấu trường).
            Vector3 offset = floorPoint - Center;
            offset.y = 0f;
            float limit = arenaRadius - radius;
            if (limit <= 0f || offset.sqrMagnitude > limit * limit)
            {
                return Reject(PlacementResult.OutsideArena, floorPoint);
            }

            // Capsule bắt đầu cao hơn sàn một chút; collider sàn cũng được bỏ qua tường minh.
            float capsuleRadius = Mathf.Max(0.02f, radius);
            Vector3 bottom = floorPoint + Vector3.up * (capsuleRadius + 0.02f);
            Vector3 top = floorPoint + Vector3.up * Mathf.Max(capsuleRadius + 0.02f, height - capsuleRadius);
            int count = Physics.OverlapCapsuleNonAlloc(bottom, top, capsuleRadius, overlapBuffer, collisionMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Collider hit = overlapBuffer[i];
                if (hit == floorCollider)
                {
                    continue;
                }

                Transform hitTransform = hit.transform;
                if ((ignoreA != null && hitTransform.IsChildOf(ignoreA)) || (ignoreB != null && hitTransform.IsChildOf(ignoreB)))
                {
                    continue;
                }

                return Reject(PlacementResult.Blocked, floorPoint);
            }

            return PlacementResult.Valid;
        }

        // Chiếu điểm yêu cầu xuống sàn rồi kiểm tra; nếu không hợp lệ thì quét các vòng từ gần ra xa để tìm điểm hợp lệ gần nhất.
        public PlacementResult TryFindNearestValid(Vector3 requested, float radius, float height, Transform ignoreA, Transform ignoreB, out Vector3 result)
        {
            result = requested;
            if (!IsReady)
            {
                return Reject(PlacementResult.NotReady, requested);
            }

            PlacementResult first = PlacementResult.NoFloor;
            if (TryProjectToFloor(requested, out Vector3 floorPoint))
            {
                first = Validate(floorPoint, radius, height, ignoreA, ignoreB);
                if (first == PlacementResult.Valid)
                {
                    result = floorPoint;
                    return PlacementResult.Valid;
                }
            }

            int rings = Mathf.CeilToInt(searchRadius / ringStep);
            for (int ring = 1; ring <= rings; ring++)
            {
                float distance = Mathf.Min(ring * ringStep, searchRadius);
                // Lệch góc xen kẽ giữa các vòng để điểm mẫu không thẳng hàng.
                float angleOffset = ring % 2 == 0 ? 0f : Mathf.PI / samplesPerRing;
                for (int sample = 0; sample < samplesPerRing; sample++)
                {
                    float angle = angleOffset + sample * Mathf.PI * 2f / samplesPerRing;
                    Vector3 candidate = requested + new Vector3(Mathf.Cos(angle) * distance, 0f, Mathf.Sin(angle) * distance);
                    if (TryProjectToFloor(candidate, out Vector3 candidateFloor)
                        && Validate(candidateFloor, radius, height, ignoreA, ignoreB) == PlacementResult.Valid)
                    {
                        result = candidateFloor;
                        return PlacementResult.Valid;
                    }
                }
            }

            lastRejectionCode = first == PlacementResult.Valid ? PlacementResult.NoFloor : first;
            lastRejection = $"Không tìm được chỗ hợp lệ trong {searchRadius:0.0} m quanh {requested} (lý do đầu tiên: {first})";
            return lastRejectionCode;
        }

        // Ghi lý do từ chối để hiển thị trong Inspector/log rồi trả về kết quả.
        private PlacementResult Reject(PlacementResult result, Vector3 point)
        {
            lastRejectionCode = result;
            lastRejection = $"{result} tại {point}";
            return result;
        }

        // Vẽ vòng đấu trường trong Scene view để dễ căn chỉnh.
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;
            Vector3 center = Center;
            const int segments = 48;
            Vector3 previous = center + new Vector3(arenaRadius, 0f, 0f);
            for (int i = 1; i <= segments; i++)
            {
                float angle = i * Mathf.PI * 2f / segments;
                Vector3 next = center + new Vector3(Mathf.Cos(angle) * arenaRadius, 0f, Mathf.Sin(angle) * arenaRadius);
                Gizmos.DrawLine(previous, next);
                previous = next;
            }
        }
    }
}
