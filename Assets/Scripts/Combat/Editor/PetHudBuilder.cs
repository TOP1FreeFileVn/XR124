using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace XR124.Combat.EditorTools
{
    // Dựng HUD world-space cho một pet: [biểu tượng hệ][tên] ở trên, thanh máu (lớp trễ, máu, khiên, số HP) và thanh Energy ở dưới.
    // Canvas 320×110 đơn vị với scale 0.0022 → rộng ~0.7 m, đọc được khi pet đứng cách 2–4 m trong VR.
    public static class PetHudBuilder
    {
        private const float CanvasScale = 0.0022f;

        // Xóa HUD/nhãn cũ trên pet rồi tạo HUD mới và nối vào PetHealthBar.
        public static PetHealthBar Build(PetCombatant pet, ElementIconLibrary icons)
        {
            RemoveOld(pet);

            GameObject root = new GameObject("HealthBar", typeof(RectTransform), typeof(Canvas));
            Undo.RegisterCreatedObjectUndo(root, "Create Pet HUD");
            RectTransform rootRect = (RectTransform)root.transform;
            rootRect.SetParent(pet.transform, false);
            rootRect.sizeDelta = new Vector2(320f, 110f);
            rootRect.localScale = Vector3.one * CanvasScale;
            float height = pet.Definition != null ? pet.Definition.bodyHeight : 0.8f;
            rootRect.localPosition = new Vector3(0f, height + 0.4f, 0f);
            Canvas canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;

            Image icon = CreateImage("ElementIcon", rootRect, new Color(1f, 1f, 1f, 1f), new Vector2(-132f, 28f), new Vector2(52f, 52f));
            icon.preserveAspect = true;

            TextMeshProUGUI title = CreateText("Name", rootRect, new Vector2(24f, 28f), new Vector2(252f, 52f), 38f, TextAlignmentOptions.MidlineLeft);
            title.fontStyle = FontStyles.Bold;
            title.text = pet.DisplayName;
            title.outlineWidth = 0.2f;
            title.outlineColor = new Color32(20, 22, 30, 255);

            // Khung thanh máu và các lớp bên trong (trễ → máu → khiên), kéo dài theo anchorMax.x.
            Image hpFrame = CreateImage("HpFrame", rootRect, new Color(0.08f, 0.09f, 0.12f, 0.9f), new Vector2(0f, -14f), new Vector2(304f, 30f));
            RectTransform lag = CreateBar("HpLag", hpFrame.rectTransform, new Color(1f, 1f, 1f, 0.75f), 0f, 1f);
            RectTransform fill = CreateBar("HpFill", hpFrame.rectTransform, new Color(0.36f, 0.85f, 0.42f), 0f, 1f);
            RectTransform shield = CreateBar("Shield", hpFrame.rectTransform, new Color(0.6f, 0.85f, 1f, 0.85f), 0.65f, 1f);
            shield.anchorMax = new Vector2(0f, 1f);
            TextMeshProUGUI hpText = CreateText("HpText", hpFrame.rectTransform, Vector2.zero, new Vector2(300f, 30f), 22f, TextAlignmentOptions.Center);
            hpText.text = "0 / 0";
            hpText.outlineWidth = 0.25f;
            hpText.outlineColor = new Color32(0, 0, 0, 255);

            Image energyFrame = CreateImage("EnergyFrame", rootRect, new Color(0.08f, 0.09f, 0.12f, 0.9f), new Vector2(0f, -40f), new Vector2(304f, 12f));
            RectTransform energy = CreateBar("EnergyFill", energyFrame.rectTransform, new Color(1f, 0.82f, 0.25f), 0f, 1f);
            energy.anchorMax = new Vector2(0f, 1f);

            PresetIdentity(pet, icons, icon, title, hpText);

            PetHealthBar bar = root.AddComponent<PetHealthBar>();
            bar.Configure(pet, icons, icon, title, fill, fill.GetComponent<Image>(), lag, shield, hpText, energy);
            EditorUtility.SetDirty(pet.gameObject);
            return bar;
        }

        // Gắn sẵn icon hệ chính, màu tên và số máu tối đa từ PetDefinition ngay khi dựng, để trong Editor (và khi pet vừa hiện)
        // HUD đã đúng thay vì ô trắng; lúc chạy PetHealthBar vẫn cập nhật lại nếu hệ thay đổi (Zeru chọn hệ).
        private static void PresetIdentity(PetCombatant pet, ElementIconLibrary icons, Image icon, TMP_Text title, TMP_Text hpText)
        {
            PetDefinition definition = pet.Definition;
            if (definition == null)
            {
                return;
            }

            ElementType element = definition.primaryElement;
            if (icons != null)
            {
                icon.sprite = icons.GetIcon(element);
                title.color = Color.Lerp(Color.white, icons.GetColor(element), 0.35f);
            }

            // Không có icon cho hệ này thì ẩn ô ảnh thay vì để ô vuông trắng.
            icon.enabled = icon.sprite != null;
            int maxHp = Mathf.CeilToInt(definition.maxHp);
            hpText.text = $"{maxHp} / {maxHp}";
        }

        // Gỡ nhãn chữ debug cũ (PetCombatStatusLabel + TextMeshPro "StatusLabel") và HUD cũ nếu có.
        private static void RemoveOld(PetCombatant pet)
        {
            MonoBehaviour[] behaviours = pet.GetComponents<MonoBehaviour>();
            for (int i = 0; i < behaviours.Length; i++)
            {
                if (behaviours[i] != null && behaviours[i].GetType().Name == "PetCombatStatusLabel")
                {
                    Undo.DestroyObjectImmediate(behaviours[i]);
                }
            }

            string[] oldChildren = { "StatusLabel", "HealthBar" };
            for (int i = 0; i < oldChildren.Length; i++)
            {
                Transform child = pet.transform.Find(oldChildren[i]);
                if (child != null)
                {
                    Undo.DestroyObjectImmediate(child.gameObject);
                }
            }
        }

        // Tạo Image trắng (không sprite) có kích thước và vị trí cố định quanh tâm canvas.
        private static Image CreateImage(string name, RectTransform parent, Color color, Vector2 position, Vector2 size)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
            RectTransform rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            Image image = go.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        // Tạo thanh con căng theo khung cha (lề 3 đơn vị); độ dài do PetHealthBar đổi qua anchorMax.x.
        private static RectTransform CreateBar(string name, RectTransform parent, Color color, float minY, float maxY)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
            RectTransform rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(0f, minY);
            rect.anchorMax = new Vector2(1f, maxY);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.offsetMin = new Vector2(3f, 3f);
            rect.offsetMax = new Vector2(-3f, -3f);
            Image image = go.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return rect;
        }

        // Tạo chữ TextMeshPro UGUI với font mặc định của TMP.
        private static TextMeshProUGUI CreateText(string name, RectTransform parent, Vector2 position, Vector2 size, float fontSize, TextAlignmentOptions alignment)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            RectTransform rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            TextMeshProUGUI text = go.GetComponent<TextMeshProUGUI>();
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = Color.white;
            text.raycastTarget = false;
            return text;
        }
    }
}
