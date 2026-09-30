using Oculus.Interaction;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace XR124.Combat.EditorTools
{
    // Dựng túi đồ dạng đồng hồ: mặt đồng hồ gắn vào LeftHandAnchor của OVRCameraRig, bảng túi đồ world-space có
    // template ISDK Ray Canvas (giống Performance Monitor Panel) để bấm bằng tia từ tay cầm/tay phải, ô Katana có icon.
    public static class WristInventoryBuilder
    {
        // Cùng template Ray Canvas Interaction của Meta mà PerformanceMonitorPanelBuilder đang dùng.
        private const string RayCanvasTemplateGuid = "8369d93f7b6b99742bbea0649a41b7b1";
        private const string IconSource = "Assets/Art/Weapons/Katana/Katana_HandleColor.jpeg";
        private const string IconPath = "Assets/Art/Weapons/Katana/Katana_Icon.jpeg";
        private static readonly Color PanelColor = new Color32(13, 19, 28, 240);
        private static readonly Color SlotColor = new Color32(30, 42, 60, 255);
        private static readonly Color AccentColor = new Color32(90, 170, 255, 255);

        // Xóa bản cũ rồi dựng lại đồng hồ + bảng túi đồ, nối vào SummonSword trong scene.
        [MenuItem("XR124/Combat/Create Wrist Inventory", priority = 130)]
        public static void Build()
        {
            Transform left = FindAnchor("LeftHandAnchor");
            Transform right = FindAnchor("RightHandAnchor");
            SummonSword sword = Object.FindFirstObjectByType<SummonSword>(FindObjectsInactive.Include);
            if (left == null || right == null || sword == null)
            {
                Debug.LogError("[Inventory] Thiếu LeftHandAnchor/RightHandAnchor của OVRCameraRig hoặc SummonSword trong scene.");
                return;
            }

            RemoveOld(left);
            Transform watch = CreateWatch(left);

            GameObject root = new GameObject("WristInventory");
            Undo.RegisterCreatedObjectUndo(root, "Create Wrist Inventory");
            GameObject panel = CreatePanel(root.transform, out TMP_Text status, out TMP_Text buttonLabel, out Button button);

            WristInventory inventory = root.AddComponent<WristInventory>();
            inventory.Configure(watch, right, panel, sword, status, buttonLabel, button);
            EditorSceneManager.MarkSceneDirty(root.scene);
            Debug.Log("[Inventory] Đã tạo túi đồ đồng hồ trên tay trái. Chạm tay phải vào đồng hồ hoặc bấm cần analog trái để mở.", root);
        }

        // Tìm anchor tay trong OVRCameraRig theo đường dẫn chuẩn TrackingSpace/<tên>.
        private static Transform FindAnchor(string anchorName)
        {
            OVRCameraRig rig = Object.FindFirstObjectByType<OVRCameraRig>(FindObjectsInactive.Include);
            return rig != null ? rig.transform.Find("TrackingSpace/" + anchorName) : null;
        }

        // Gỡ đồng hồ và túi đồ cũ nếu đã dựng trước đó.
        private static void RemoveOld(Transform left)
        {
            Transform oldWatch = left.Find("WristWatch");
            if (oldWatch != null) Undo.DestroyObjectImmediate(oldWatch.gameObject);
            GameObject oldRoot = GameObject.Find("WristInventory");
            if (oldRoot != null) Undo.DestroyObjectImmediate(oldRoot);
        }

        // Mặt đồng hồ: dây đeo tối + mặt tròn phát sáng xanh, nằm trên mu cổ tay (sau tay cầm), không collider.
        private static Transform CreateWatch(Transform left)
        {
            GameObject watch = new GameObject("WristWatch");
            Undo.RegisterCreatedObjectUndo(watch, "Create Wrist Watch");
            watch.transform.SetParent(left, false);
            watch.transform.localPosition = new Vector3(0f, 0.03f, -0.1f);

            Material band = LoadOrCreateMaterial("Assets/Art/UI/Watch_Band.mat", new Color(0.12f, 0.14f, 0.2f), Color.black);
            Material face = LoadOrCreateMaterial("Assets/Art/UI/Watch_Face.mat", new Color(0.35f, 0.65f, 1f), new Color(0.3f, 0.6f, 1f) * 1.6f);
            CreatePart(watch.transform, "Band", new Vector3(0f, -0.004f, 0f), new Vector3(0.05f, 0.004f, 0.05f), band);
            CreatePart(watch.transform, "Face", Vector3.zero, new Vector3(0.036f, 0.004f, 0.036f), face);
            return watch.transform;
        }

        // Tạo đĩa (cylinder dẹt) không collider làm một phần của đồng hồ.
        private static void CreatePart(Transform parent, string name, Vector3 localPosition, Vector3 scale, Material material)
        {
            GameObject part = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            part.name = name;
            Object.DestroyImmediate(part.GetComponent<Collider>());
            part.transform.SetParent(parent, false);
            part.transform.localPosition = localPosition;
            part.transform.localScale = scale;
            part.GetComponent<MeshRenderer>().sharedMaterial = material;
        }

        // Material URP/Lit màu trơn, có emission nếu màu phát sáng khác đen.
        private static Material LoadOrCreateMaterial(string path, Color color, Color emission)
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = System.IO.Path.GetFileNameWithoutExtension(path) };
                AssetDatabase.CreateAsset(material, path);
            }

            material.SetColor("_BaseColor", color);
            if (emission.maxColorComponent > 0f)
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", emission);
            }

            EditorUtility.SetDirty(material);
            return material;
        }

        // Bảng túi đồ 380×240 (scale 0.001 → rộng 0.38 m): tiêu đề, ô Katana (icon, tên, trạng thái, nút) và 3 ô trống.
        private static GameObject CreatePanel(Transform parent, out TMP_Text status, out TMP_Text buttonLabel, out Button button)
        {
            GameObject panel = new GameObject("Panel", typeof(RectTransform));
            RectTransform rect = (RectTransform)panel.transform;
            rect.SetParent(parent, false);
            rect.sizeDelta = new Vector2(380f, 240f);
            rect.localScale = Vector3.one * 0.001f;
            Canvas canvas = panel.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 25;
            panel.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 2f;
            panel.AddComponent<GraphicRaycaster>();
            AddRayInteraction(panel, canvas);

            Image background = CreateImage("Background", rect, PanelColor, Vector2.zero, new Vector2(380f, 240f));
            background.raycastTarget = true;
            CreateText("Title", rect, "INVENTORY", new Vector2(0f, 96f), new Vector2(340f, 36f), 26f, TextAlignmentOptions.Midline, Color.white);

            // Ô Katana.
            Image slot = CreateImage("Slot_Katana", rect, SlotColor, new Vector2(-126f, 10f), new Vector2(96f, 96f));
            Image icon = CreateImage("Icon", slot.rectTransform, Color.white, Vector2.zero, new Vector2(88f, 88f));
            icon.sprite = EnsureIconSprite();
            icon.preserveAspect = true;
            CreateText("Name", rect, "Katana", new Vector2(-126f, -52f), new Vector2(110f, 24f), 20f, TextAlignmentOptions.Midline, Color.white);
            status = CreateText("Status", rect, "In bag", new Vector2(-126f, -74f), new Vector2(110f, 20f), 16f, TextAlignmentOptions.Midline, new Color(0.7f, 0.78f, 0.9f));

            button = CreateButton(rect, new Vector2(-126f, -102f), new Vector2(100f, 30f), out buttonLabel);

            // 3 ô trống cho vật phẩm sau này.
            for (int i = 0; i < 3; i++)
            {
                CreateImage($"Slot_Empty_{i + 1}", rect, new Color(SlotColor.r, SlotColor.g, SlotColor.b, 0.55f), new Vector2(-10f + i * 112f, 10f), new Vector2(96f, 96f));
            }

            panel.SetActive(false);
            return panel;
        }

        // Gắn template ISDK Ray Canvas của Meta để Button nhận tia chiếu từ rig; đảm bảo có PointableCanvasModule.
        private static void AddRayInteraction(GameObject panel, Canvas canvas)
        {
            if (Object.FindFirstObjectByType<PointableCanvasModule>() == null)
            {
                EventSystem eventSystem = Object.FindFirstObjectByType<EventSystem>();
                GameObject host = eventSystem != null ? eventSystem.gameObject : new GameObject("Pointable Canvas Module", typeof(EventSystem));
                Undo.AddComponent<PointableCanvasModule>(host);
            }

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(RayCanvasTemplateGuid));
            if (prefab == null)
            {
                Debug.LogError("[Inventory] Không tìm thấy template Ray Canvas Interaction của Meta.");
                return;
            }

            GameObject interaction = (GameObject)PrefabUtility.InstantiatePrefab(prefab, panel.transform);
            interaction.name = "ISDK_RayCanvasInteraction";
            RectTransform rect = interaction.GetComponent<RectTransform>();
            rect.localPosition = Vector3.zero;
            rect.localRotation = Quaternion.identity;
            rect.localScale = Vector3.one;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.sizeDelta = Vector2.zero;
            interaction.GetComponent<PointableCanvas>().InjectCanvas(canvas);
        }

        // Tạo sprite icon kiếm từ ảnh concept (bản sao riêng để import dạng Sprite 256).
        private static Sprite EnsureIconSprite()
        {
            if (AssetDatabase.LoadAssetAtPath<Texture2D>(IconPath) == null && AssetDatabase.LoadAssetAtPath<Texture2D>(IconSource) != null)
            {
                AssetDatabase.CopyAsset(IconSource, IconPath);
            }

            TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(IconPath);
            if (importer != null && importer.textureType != TextureImporterType.Sprite)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.maxTextureSize = 256;
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Sprite>(IconPath);
        }

        // Image không sprite (hình chữ nhật màu) tại vị trí/kích thước cố định quanh tâm cha.
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

        // Chữ TextMeshPro UGUI không nhận tia.
        private static TMP_Text CreateText(string name, RectTransform parent, string text, Vector2 position, Vector2 size, float fontSize, TextAlignmentOptions alignment, Color color)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            RectTransform rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            TextMeshProUGUI tmp = go.GetComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.alignment = alignment;
            tmp.color = color;
            tmp.raycastTarget = false;
            return tmp;
        }

        // Nút Unity có nền màu nhấn; PointableCanvas chuyển tia chọn của rig thành click cho nút này.
        private static Button CreateButton(RectTransform parent, Vector2 position, Vector2 size, out TMP_Text label)
        {
            Image image = CreateImage("SwordButton", parent, AccentColor, position, size);
            image.raycastTarget = true;
            Button button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            label = CreateText("Label", image.rectTransform, "EQUIP", Vector2.zero, size, 18f, TextAlignmentOptions.Midline, Color.white);
            label.fontStyle = FontStyles.Bold;
            return button;
        }
    }
}
