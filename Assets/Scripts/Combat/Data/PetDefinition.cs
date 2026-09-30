using UnityEngine;

namespace XR124.Combat
{
    [CreateAssetMenu(fileName = "Pet_", menuName = "XR124/Combat/Pet Definition", order = 12)]
    public sealed class PetDefinition : ScriptableObject
    {
        [Header("Nhận dạng")]
        public string petId = "PET-000";
        public string displayName = "New Pet";

        [Header("Hệ (GDD mục 5, 7)")]
        public ElementType primaryElement = ElementType.Normal;
        public ElementType secondaryElement = ElementType.None;
        [Tooltip("Zeru: được chọn hệ chính trước trận, không đổi trong trận.")]
        public bool canChooseElement;

        [Header("Chỉ số (GDD mục 7)")]
        [Min(1f)] public float maxHp = 1000f;
        [Min(0f)] public float attack = 100f;
        [Min(0f)] public float defense = 100f;
        [Min(0f)] public float speed = 100f;

        [Header("Tự di chuyển & đánh thường (bổ sung ngoài GDD 1.1)")]
        [Tooltip("Tốc độ di chuyển thực trên đấu trường (m/s).")]
        [Min(0f)] public float moveSpeed = 0.8f;
        [Tooltip("Khoảng cách tối đa để đánh thường (m), tính từ tâm tới tâm.")]
        [Min(0.1f)] public float attackRange = 1f;
        [Min(0.05f)] public float attacksPerSecond = 1f;
        [Tooltip("Hệ số ATK mỗi đòn đánh thường; đánh thường không tạo AP/Energy.")]
        [Min(0f)] public float basicAttackRatio = 0.2f;

        [Header("Kích thước thân (dùng kiểm tra chỗ đứng trên đấu trường)")]
        [Min(0.02f)] public float bodyRadius = 0.3f;
        [Min(0.05f)] public float bodyHeight = 0.85f;

        [Header("Ultimate")]
        [Min(1)] public int ultimateEnergyCost = 10;
        [Tooltip("Số lần Ultimate tích trữ được; Energy tối đa = chi phí × số lần.")]
        [Min(1)] public int ultimateMaxCharges = 1;

        [Header("Asset (bổ sung sau)")]
        [Tooltip("Model/prefab hiển thị; được tạo dưới PetCombatant khi khởi tạo nếu chưa có model.")]
        public GameObject modelPrefab;
        [Tooltip("Animator Controller gán cho model khi tạo; cần các tham số Speed, Attack, Cast, Ultimate, Hit, Die, Summon.")]
        public RuntimeAnimatorController animatorController;
        [Tooltip("Xoay model quanh trục Y (độ) để mặt model trùng hướng +Z của pet.")]
        public float modelYawOffset;
        [Min(0.01f)] public float modelScale = 1f;
        [Tooltip("Material thay cho mọi renderer của model (dùng chung mesh nhưng khác màu theo hệ); để trống thì giữ material gốc.")]
        public Material overrideMaterial;
        public Sprite icon;
        public GameObject skillVfxPrefab;
        public GameObject ultimateVfxPrefab;
        public AudioClip skillSfx;
        public AudioClip ultimateSfx;

        // Energy tối đa pet giữ được, đủ cho số lần Ultimate được phép tích trữ (GDD mục 2).
        public int MaxEnergy => ultimateEnergyCost * ultimateMaxCharges;
    }
}
