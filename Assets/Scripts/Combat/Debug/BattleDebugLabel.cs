using System.Text;
using TMPro;
using UnityEngine;

namespace XR124.Combat
{
    // Bảng debug world-space trôi trước mặt người chơi: trạng thái đấu trường, kiếm, triệu hồi, chuỗi ấn và lý do bị từ chối.
    public sealed class BattleDebugLabel : MonoBehaviour
    {
        [SerializeField] private TextMeshPro label;
        [SerializeField] private ArenaPlacement placement;
        [SerializeField] private SummonSword sword;
        [SerializeField] private BattleSummoner summoner;
        [SerializeField] private SealComboCaster caster;
        [Min(0.02f)] [SerializeField] private float refreshInterval = 0.2f;
        [Tooltip("Vị trí so với camera: phải/trên/trước (m).")]
        [SerializeField] private Vector3 cameraOffset = new Vector3(0.35f, -0.15f, 0.8f);
        [Min(0f)] [SerializeField] private float followSharpness = 4f;

        private readonly StringBuilder builder = new StringBuilder(512);
        private Transform cameraTransform;
        private float refreshTimer;

        // Gán tham chiếu từ công cụ Editor.
        public void Configure(TextMeshPro text, ArenaPlacement floorPlacement, SummonSword summonSword, BattleSummoner battleSummoner, SealComboCaster sealCaster)
        {
            label = text;
            placement = floorPlacement;
            sword = summonSword;
            summoner = battleSummoner;
            caster = sealCaster;
        }

        // Cache camera chính một lần.
        private void Start()
        {
            if (Camera.main != null)
            {
                cameraTransform = Camera.main.transform;
            }
        }

        // Bám theo camera mượt và cập nhật chữ theo nhịp cố định.
        private void LateUpdate()
        {
            if (label == null)
            {
                return;
            }

            FollowCamera();

            refreshTimer -= Time.deltaTime;
            if (refreshTimer > 0f)
            {
                return;
            }

            refreshTimer = refreshInterval;
            RebuildText();
        }

        // Đặt bảng ở góc tầm nhìn và xoay về phía camera; nội suy theo thời gian để không rung theo đầu.
        private void FollowCamera()
        {
            if (cameraTransform == null)
            {
                return;
            }

            Vector3 target = cameraTransform.TransformPoint(cameraOffset);
            float blend = 1f - Mathf.Exp(-followSharpness * Time.deltaTime);
            transform.position = Vector3.Lerp(transform.position, target, blend);
            Vector3 away = transform.position - cameraTransform.position;
            if (away.sqrMagnitude > 0.0001f)
            {
                transform.rotation = Quaternion.LookRotation(away, Vector3.up);
            }
        }

        // Ghép các dòng trạng thái; dùng ASCII vì font TMP mặc định có thể thiếu dấu tiếng Việt.
        private void RebuildText()
        {
            builder.Clear();
            if (placement != null)
            {
                builder.Append("Arena: ").Append(placement.IsReady ? "ready" : "not ready").Append('\n');
                if (placement.LastRejectionCode != PlacementResult.Valid)
                {
                    builder.Append("Last reject: ").Append(placement.LastRejectionCode).Append('\n');
                }
            }

            if (sword != null)
            {
                builder.Append("Sword: ").Append(sword.State).Append('\n');
            }

            if (summoner != null)
            {
                builder.Append("Summon: ").Append(summoner.State).Append('\n');
            }

            if (caster != null)
            {
                builder.Append("Seals: ").Append(caster.PendingSequence).Append('\n');
                builder.Append("Last: ").Append(caster.LastResult).Append('\n');
            }

            label.SetText(builder);
        }
    }
}
