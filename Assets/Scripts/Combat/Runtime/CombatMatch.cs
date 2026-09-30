using System;
using UnityEngine;

namespace XR124.Combat
{
    // Điều phối một trận 1v1: gán đối thủ, luật và bảng tương khắc cho hai pet, xác định người thắng khi một bên bị hạ.
    public sealed class CombatMatch : MonoBehaviour
    {
        [Header("Dữ liệu")]
        [SerializeField] private CombatRules rules;
        [SerializeField] private ElementChart elementChart;

        [Header("Hai pet")]
        [SerializeField] private PetCombatant petA;
        [SerializeField] private PetCombatant petB;

        [Header("Tùy chọn")]
        [SerializeField] private bool beginOnStart = true;
        [SerializeField] private bool logEvents = true;

        [Header("Runtime (chỉ đọc)")]
        [SerializeField] private bool isRunning;
        [SerializeField] private PetCombatant winner;

        public event Action<CombatMatch> MatchStarted;
        public event Action<CombatMatch, PetCombatant> MatchEnded;

        public bool IsRunning => isRunning;
        public PetCombatant Winner => winner;
        public PetCombatant PetA => petA;
        public PetCombatant PetB => petB;

        // Tự bắt đầu trận khi scene chạy nếu bật beginOnStart.
        private void Start()
        {
            if (beginOnStart)
            {
                BeginMatch();
            }
        }

        // Hủy đăng ký event khi object bị hủy để không giữ tham chiếu treo.
        private void OnDestroy()
        {
            Unsubscribe();
        }

        // Gán dữ liệu từ công cụ dựng scene trong Editor; startAutomatically = false khi trận do BattleSummoner bắt đầu.
        public void Configure(CombatRules matchRules, ElementChart chart, PetCombatant first, PetCombatant second, bool startAutomatically)
        {
            rules = matchRules;
            elementChart = chart;
            petA = first;
            petB = second;
            beginOnStart = startAutomatically;
        }

        // Bắt đầu (hoặc bắt đầu lại) trận; báo lỗi rõ ràng khi thiếu dữ liệu thay vì chạy với giá trị rỗng.
        public void BeginMatch()
        {
            if (rules == null || petA == null || petB == null || petA == petB)
            {
                Debug.LogError("[Combat] CombatMatch thiếu CombatRules hoặc hai PetCombatant hợp lệ.", this);
                return;
            }

            if (petA.Definition == null || petB.Definition == null)
            {
                Debug.LogError("[Combat] Một pet chưa được gán PetDefinition.", this);
                return;
            }

            Unsubscribe();
            winner = null;
            petA.BeginMatch(petB, rules, elementChart);
            petB.BeginMatch(petA, rules, elementChart);
            Subscribe();
            isRunning = true;

            if (logEvents)
            {
                Debug.Log($"[Combat] Bắt đầu trận: {petA.DisplayName} ({petA.PrimaryElement}) vs {petB.DisplayName} ({petB.PrimaryElement})", this);
            }

            MatchStarted?.Invoke(this);
        }

        // Kết thúc trận khi một pet bị hạ; bên còn lại là người thắng.
        private void HandleDefeated(PetCombatant defeated)
        {
            if (!isRunning)
            {
                return;
            }

            isRunning = false;
            winner = defeated == petA ? petB : petA;
            petA.EndMatch();
            petB.EndMatch();

            if (logEvents)
            {
                Debug.Log($"[Combat] {defeated.DisplayName} bị hạ. Người thắng: {winner.DisplayName}", this);
            }

            MatchEnded?.Invoke(this, winner);
        }

        // Ghi log sát thương để debug cân bằng (có thể tắt bằng logEvents).
        private void HandleDamageTaken(PetCombatant target, DamageInfo info)
        {
            if (!logEvents)
            {
                return;
            }

            string source = info.source != null ? info.source.DisplayName : "?";
            string tag = info.isBurnTick ? "cháy" : info.kind == DamageKind.True ? "chuẩn" : $"x{info.elementMultiplier:0.##}";
            Debug.Log($"[Combat] {source} → {target.DisplayName}: {info.amount:0} ({tag}, khiên đỡ {info.absorbedByShield:0}) | HP {target.CurrentHp:0}/{target.MaxHp:0}", this);
        }

        // Đăng ký event của cả hai pet.
        private void Subscribe()
        {
            petA.Defeated += HandleDefeated;
            petB.Defeated += HandleDefeated;
            petA.DamageTaken += HandleDamageTaken;
            petB.DamageTaken += HandleDamageTaken;
        }

        // Hủy đăng ký event của cả hai pet; an toàn khi gọi nhiều lần.
        private void Unsubscribe()
        {
            if (petA != null)
            {
                petA.Defeated -= HandleDefeated;
                petA.DamageTaken -= HandleDamageTaken;
            }

            if (petB != null)
            {
                petB.Defeated -= HandleDefeated;
                petB.DamageTaken -= HandleDamageTaken;
            }
        }
    }
}
