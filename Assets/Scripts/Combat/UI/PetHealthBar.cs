using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace XR124.Combat
{
    // HUD world-space trên đầu pet: hàng trên là biểu tượng hệ + tên, bên dưới là thanh máu (kèm lớp khiên và số HP)
    // và thanh Energy mảnh. Thanh xoay về camera, độ dài thanh đổi bằng anchor nên không cần sprite kiểu Filled.
    public sealed class PetHealthBar : MonoBehaviour
    {
        [SerializeField] private PetCombatant pet;
        [SerializeField] private ElementIconLibrary icons;
        [SerializeField] private Image elementIcon;
        [SerializeField] private TMP_Text nameText;
        [SerializeField] private RectTransform hpFill;
        [SerializeField] private Image hpFillImage;
        [SerializeField] private RectTransform hpLag;
        [SerializeField] private RectTransform shieldFill;
        [SerializeField] private TMP_Text hpText;
        [SerializeField] private RectTransform energyFill;
        [Tooltip("Độ cao HUD trên gốc pet (m); cộng thêm chiều cao thân trong PetDefinition.")]
        [SerializeField] private float extraHeight = 0.4f;
        [Min(0.1f)] [SerializeField] private float lagSpeed = 0.6f;
        [SerializeField] private Color healthyColor = new Color(0.36f, 0.85f, 0.42f);
        [SerializeField] private Color lowColor = new Color(0.95f, 0.3f, 0.25f);

        private Transform cameraTransform;
        private float lagRatio = 1f;
        private int lastHp = -1;
        private ElementType shownElement = ElementType.None;

        // Gán tham chiếu từ công cụ dựng HUD trong Editor.
        public void Configure(PetCombatant targetPet, ElementIconLibrary library, Image icon, TMP_Text title,
            RectTransform fill, Image fillImage, RectTransform lag, RectTransform shield, TMP_Text hpLabel, RectTransform energy)
        {
            pet = targetPet;
            icons = library;
            elementIcon = icon;
            nameText = title;
            hpFill = fill;
            hpFillImage = fillImage;
            hpLag = lag;
            shieldFill = shield;
            hpText = hpLabel;
            energyFill = energy;
        }

        // Cache camera một lần và đặt độ cao HUD theo kích thước thân pet.
        private void Start()
        {
            if (Camera.main != null)
            {
                cameraTransform = Camera.main.transform;
            }

            if (pet != null && pet.Definition != null)
            {
                transform.localPosition = new Vector3(0f, pet.Definition.bodyHeight + extraHeight, 0f);
            }
        }

        // Cập nhật tên/hệ khi đổi, độ dài các thanh, màu máu, rồi xoay HUD về camera.
        private void LateUpdate()
        {
            if (pet == null)
            {
                return;
            }

            RefreshIdentity();

            float hpRatio = Mathf.Clamp01(pet.HpRatio);
            SetWidth(hpFill, hpRatio);
            // Thanh trễ màu trắng tụt chậm phía sau để thấy rõ lượng máu vừa mất.
            lagRatio = Mathf.Max(hpRatio, Mathf.MoveTowards(lagRatio, hpRatio, lagSpeed * Time.deltaTime));
            SetWidth(hpLag, lagRatio);
            SetWidth(shieldFill, Mathf.Clamp01(pet.ShieldTotal / Mathf.Max(1f, pet.MaxHp)));
            if (hpFillImage != null)
            {
                hpFillImage.color = Color.Lerp(lowColor, healthyColor, Mathf.InverseLerp(0.2f, 0.6f, hpRatio));
            }

            int hp = Mathf.CeilToInt(pet.CurrentHp);
            if (hpText != null && hp != lastHp)
            {
                lastHp = hp;
                // SetText dạng định dạng của TMP không cấp phát chuỗi mới; {0:0} = không lấy số thập phân.
                hpText.SetText("{0:0} / {1:0}", hp, Mathf.CeilToInt(pet.MaxHp));
            }

            if (energyFill != null)
            {
                SetWidth(energyFill, pet.MaxEnergy > 0 ? Mathf.Clamp01((float)pet.CurrentEnergy / pet.MaxEnergy) : 0f);
            }

            FaceCamera();
        }

        // Đặt biểu tượng, màu viền tên và tên pet; chỉ làm lại khi hệ thay đổi (Zeru chọn hệ trước trận).
        private void RefreshIdentity()
        {
            ElementType element = pet.PrimaryElement;
            if (element == shownElement)
            {
                return;
            }

            shownElement = element;
            if (elementIcon != null && icons != null)
            {
                elementIcon.sprite = icons.GetIcon(element);
                elementIcon.enabled = elementIcon.sprite != null;
            }

            if (nameText != null)
            {
                nameText.text = pet.DisplayName;
                nameText.color = icons != null ? Color.Lerp(Color.white, icons.GetColor(element), 0.35f) : Color.white;
            }
        }

        // Đổi độ dài thanh bằng anchorMax.x (0–1) để không phụ thuộc sprite.
        private static void SetWidth(RectTransform bar, float ratio)
        {
            if (bar == null)
            {
                return;
            }

            Vector2 max = bar.anchorMax;
            if (!Mathf.Approximately(max.x, ratio))
            {
                max.x = ratio;
                bar.anchorMax = max;
            }
        }

        // Quay mặt HUD về phía camera (giữ thẳng đứng) để đọc được từ mọi góc.
        private void FaceCamera()
        {
            if (cameraTransform == null)
            {
                return;
            }

            Vector3 away = transform.position - cameraTransform.position;
            away.y = 0f;
            if (away.sqrMagnitude > 0.0001f)
            {
                transform.rotation = Quaternion.LookRotation(away, Vector3.up);
            }
        }
    }
}
