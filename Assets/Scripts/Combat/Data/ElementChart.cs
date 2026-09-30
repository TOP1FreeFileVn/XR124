using UnityEngine;

namespace XR124.Combat
{
    [CreateAssetMenu(fileName = "ElementChart", menuName = "XR124/Combat/Element Chart", order = 10)]
    public sealed class ElementChart : ScriptableObject
    {
        // Hàng = hệ tấn công, cột = hệ phòng thủ, theo thứ tự ElementType (Fire..Myth).
        private static readonly float[] DefaultMultipliers =
        {
            //  Fire  Grass Water Elec  Ice   Ground Normal Myth
            1.00f, 1.25f, 0.75f, 1.00f, 1.25f, 0.75f, 1.00f, 1.00f, // Fire
            0.75f, 1.00f, 1.25f, 1.00f, 0.75f, 1.25f, 1.00f, 1.00f, // Grass
            1.25f, 0.75f, 1.00f, 0.75f, 1.00f, 1.25f, 1.00f, 1.00f, // Water
            1.00f, 1.00f, 1.25f, 1.00f, 1.00f, 0.75f, 1.00f, 1.00f, // Electric
            0.75f, 1.25f, 1.00f, 1.00f, 1.00f, 1.00f, 1.00f, 1.00f, // Ice
            1.25f, 0.75f, 0.75f, 1.25f, 1.00f, 1.00f, 1.00f, 1.00f, // Ground
            1.00f, 1.00f, 1.00f, 1.00f, 1.00f, 1.00f, 0.75f, 1.00f, // Normal
            1.00f, 1.00f, 1.00f, 1.00f, 1.00f, 1.00f, 1.00f, 1.25f  // Myth
        };

        [SerializeField] private float[] multipliers = (float[])DefaultMultipliers.Clone();

        // Nạp lại bảng tương khắc mặc định của GDD khi bấm Reset trong Inspector hoặc tạo asset mới.
        private void Reset()
        {
            multipliers = (float[])DefaultMultipliers.Clone();
        }

        // Đảm bảo mảng luôn đủ 8×8 phần tử; nếu dữ liệu hỏng thì khôi phục bảng mặc định để tránh lỗi chỉ số.
        private void OnValidate()
        {
            int expected = CombatConstants.ElementCount * CombatConstants.ElementCount;
            if (multipliers == null || multipliers.Length != expected)
            {
                multipliers = (float[])DefaultMultipliers.Clone();
            }
        }

        // Trả về hệ số của một cặp hệ công → thủ; None hoặc bảng lỗi luôn trả 1 để không làm hỏng sát thương.
        public float GetMultiplier(ElementType attacker, ElementType defender)
        {
            if (attacker == ElementType.None || defender == ElementType.None)
            {
                return 1f;
            }

            int index = (int)attacker * CombatConstants.ElementCount + (int)defender;
            if (multipliers == null || index >= multipliers.Length)
            {
                return 1f;
            }

            return multipliers[index];
        }

        // Tính hệ số cuối cho người nhận có tối đa 2 hệ: nhân hệ số của hệ chính với hệ phụ (GDD mục 5).
        public float GetMultiplier(ElementType attacker, ElementType defenderPrimary, ElementType defenderSecondary)
        {
            return GetMultiplier(attacker, defenderPrimary) * GetMultiplier(attacker, defenderSecondary);
        }
    }
}
