using Oculus.Interaction;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public static class PerformanceMonitorPanelBuilder
{
    private const string RootName = "Performance Monitor Panel";
    private const string RayCanvasTemplateGuid = "8369d93f7b6b99742bbea0649a41b7b1";
    private static readonly Color PanelColor = new Color32(13, 19, 28, 245);
    private static readonly Color HeaderColor = new Color32(24, 34, 48, 255);
    private static readonly Color AccentColor = new Color32(45, 212, 191, 255);
    private static readonly Color LabelColor = new Color32(153, 167, 186, 255);
    private static readonly Color ValueColor = new Color32(242, 246, 250, 255);

    /// <summary>
    /// Tạo lại bảng hiệu năng trong scene, giữ toàn bộ chỉ số và state bên trong nền đen của palm menu.
    /// </summary>
    [MenuItem("Tools/XR124/Create Performance Monitor Panel")]
    public static void CreatePanel()
    {
        GameObject existing = GameObject.Find(RootName);
        if (existing != null)
        {
            Undo.DestroyObjectImmediate(existing);
        }

        GameObject root = CreateUiObject(RootName, null);
        Undo.RegisterCreatedObjectUndo(root, "Create Performance Monitor Panel");
        Canvas canvas = ConfigureCanvas(root);
        ConfigureMetaCanvasInteraction(root, canvas);
        CanvasGroup canvasGroup = root.AddComponent<CanvasGroup>();

        CreateImage("Background", root.transform, PanelColor, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        GameObject header = CreateImage("Header", root.transform, HeaderColor,
            new Vector2(0f, 1f), Vector2.one, new Vector2(0f, -54f), Vector2.zero);
        CreateImage("Accent", header.transform, AccentColor,
            new Vector2(0f, 0f), new Vector2(0.012f, 1f), Vector2.zero, Vector2.zero);
        CreateText("Title", header.transform, "PERFORMANCE", 25, FontStyles.Bold, ValueColor,
            new Vector2(0f, 0f), Vector2.one, new Vector2(24f, 0f), new Vector2(-164f, 0f), TextAlignmentOptions.MidlineLeft);

        PerformanceMonitorPanel monitor = root.AddComponent<PerformanceMonitorPanel>();
        SerializedObject monitorObject = new SerializedObject(monitor);
        monitorObject.FindProperty("panelCanvasGroup").objectReferenceValue = canvasGroup;
        monitorObject.FindProperty("leftHandSkeleton").objectReferenceValue = FindLeftHandSkeleton();
        monitorObject.FindProperty("movementController").objectReferenceValue =
            Object.FindFirstObjectByType<PocketControl>();
        monitorObject.FindProperty("combatController").objectReferenceValue =
            Object.FindFirstObjectByType<CompanionCombatController>();

        Button resetButton = CreateButton("Reset Game Button", root.transform, "RESET GAME",
            new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-148f, -45f), new Vector2(-14f, -9f));
        UnityEventTools.AddPersistentListener(resetButton.onClick, monitor.ResetGame);

        CreateMetricGrid(root.transform, monitorObject);
        GameObject stateText = CreateText("Object State", root.transform, "OBJECT STATE", 13,
            FontStyles.Normal, LabelColor, new Vector2(0f, 1f), Vector2.one,
            new Vector2(24f, -432f), new Vector2(-24f, -312f), TextAlignmentOptions.TopLeft);
        TMP_Text stateTextComponent = stateText.GetComponent<TMP_Text>();
        stateTextComponent.raycastTarget = false;
        monitorObject.FindProperty("objectStateText").objectReferenceValue = stateTextComponent;
        monitorObject.ApplyModifiedPropertiesWithoutUndo();

        Selection.activeGameObject = root;
        EditorSceneManager.MarkSceneDirty(root.scene);
        Debug.Log("Created left-palm performance monitor panel.", root);
    }

    /// <summary>
    /// Cấu hình Canvas world-space thường để tránh render kép với OVR Overlay Canvas.
    /// </summary>
    private static Canvas ConfigureCanvas(GameObject root)
    {
        RectTransform rect = root.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(560f, 450f);
        rect.localScale = Vector3.one * 0.001f;

        Canvas canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = 20;

        CanvasScaler scaler = root.AddComponent<CanvasScaler>();
        scaler.dynamicPixelsPerUnit = 2f;
        root.AddComponent<GraphicRaycaster>();
        return canvas;
    }

    /// <summary>
    /// Dùng nguyên prefab ray-canvas của Meta để các Button Unity nhận được ray và thao tác chọn trong XR.
    /// </summary>
    private static void ConfigureMetaCanvasInteraction(GameObject root, Canvas canvas)
    {
        EnsurePointableCanvasModule();

        string prefabPath = AssetDatabase.GUIDToAssetPath(RayCanvasTemplateGuid);
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (prefab == null)
        {
            Debug.LogError("Meta Ray Canvas Interaction template was not found.", root);
            return;
        }

        GameObject interaction = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root.transform);
        interaction.name = "ISDK_RayCanvasInteraction";
        Undo.RegisterCreatedObjectUndo(interaction, "Add Meta Ray Canvas Interaction");

        RectTransform rect = interaction.GetComponent<RectTransform>();
        rect.localPosition = Vector3.zero;
        rect.localRotation = Quaternion.identity;
        rect.localScale = Vector3.one;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = Vector2.zero;
        interaction.GetComponent<PointableCanvas>().InjectCanvas(canvas);
    }

    /// <summary>
    /// Bảo đảm scene có module chuyển sự kiện Pointable của Meta thành sự kiện Button của Unity.
    /// </summary>
    private static void EnsurePointableCanvasModule()
    {
        if (Object.FindFirstObjectByType<PointableCanvasModule>() != null)
        {
            return;
        }

        EventSystem eventSystem = Object.FindFirstObjectByType<EventSystem>();
        GameObject moduleObject;
        if (eventSystem != null)
        {
            moduleObject = eventSystem.gameObject;
        }
        else
        {
            moduleObject = new GameObject("Pointable Canvas Module", typeof(EventSystem));
            Undo.RegisterCreatedObjectUndo(moduleObject, "Create Pointable Canvas Module");
        }

        Undo.AddComponent<PointableCanvasModule>(moduleObject);
    }

    /// <summary>
    /// Tìm đúng skeleton tay trái để công cụ Editor gán sẵn nguồn theo dõi cho palm menu.
    /// </summary>
    private static OVRSkeleton FindLeftHandSkeleton()
    {
        OVRSkeleton[] skeletons = Object.FindObjectsByType<OVRSkeleton>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);

        foreach (OVRSkeleton skeleton in skeletons)
        {
            OVRSkeleton.SkeletonType skeletonType = skeleton.GetSkeletonType();
            if (skeletonType == OVRSkeleton.SkeletonType.XRHandLeft
                || skeletonType == OVRSkeleton.SkeletonType.HandLeft)
            {
                return skeleton;
            }
        }

        return null;
    }

    /// <summary>
    /// Tạo lưới hai cột thông số và liên kết từng ô giá trị với component đo hiệu năng.
    /// </summary>
    private static void CreateMetricGrid(Transform root, SerializedObject monitorObject)
    {
        string[] labels = { "FPS", "FRAME TIME", "1% LOW", "CPU", "GPU", "HITCHES", "SYSTEM RAM", "GC RESERVED" };
        string[] properties = { "fpsText", "frameTimeText", "lowFpsText", "cpuTimeText", "gpuTimeText", "hitchText", "memoryText", "gcText" };

        for (int i = 0; i < labels.Length; i++)
        {
            int column = i % 2;
            int row = i / 2;
            float left = 24f + column * 268f;
            float top = -74f - row * 61f;

            GameObject label = CreateText(labels[i] + " Label", root, labels[i], 15, FontStyles.Normal, LabelColor,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(left, top), new Vector2(left + 120f, top - 42f), TextAlignmentOptions.MidlineLeft);
            GameObject value = CreateText(labels[i] + " Value", root, "--", 22, FontStyles.Bold, ValueColor,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(left + 124f, top), new Vector2(left + 244f, top - 42f), TextAlignmentOptions.MidlineRight);

            label.GetComponent<TMP_Text>().raycastTarget = false;
            TMP_Text valueText = value.GetComponent<TMP_Text>();
            valueText.raycastTarget = false;
            monitorObject.FindProperty(properties[i]).objectReferenceValue = valueText;
        }
    }

    /// <summary>
    /// Tạo GameObject UI có RectTransform và đặt nó dưới đúng transform cha.
    /// </summary>
    private static GameObject CreateUiObject(string name, Transform parent)
    {
        GameObject gameObject = new GameObject(name, typeof(RectTransform));
        gameObject.transform.SetParent(parent, false);
        return gameObject;
    }

    /// <summary>
    /// Tạo một vùng màu phẳng theo anchor và khoảng cách mép được truyền vào.
    /// </summary>
    private static GameObject CreateImage(string name, Transform parent, Color color,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
    {
        GameObject gameObject = CreateUiObject(name, parent);
        RectTransform rect = gameObject.GetComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;

        Image image = gameObject.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return gameObject;
    }

    /// <summary>
    /// Tạo một ô TextMeshPro và cấu hình đầy đủ vị trí, màu cùng kiểu chữ.
    /// </summary>
    private static GameObject CreateText(string name, Transform parent, string text, float fontSize,
        FontStyles style, Color color, Vector2 anchorMin, Vector2 anchorMax,
        Vector2 offsetMin, Vector2 offsetMax, TextAlignmentOptions alignment)
    {
        GameObject gameObject = CreateUiObject(name, parent);
        RectTransform rect = gameObject.GetComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;

        TextMeshProUGUI textComponent = gameObject.AddComponent<TextMeshProUGUI>();
        textComponent.text = text;
        textComponent.fontSize = fontSize;
        textComponent.fontStyle = style;
        textComponent.color = color;
        textComponent.alignment = alignment;
        textComponent.enableWordWrapping = false;
        textComponent.raycastTarget = false;
        return gameObject;
    }

    /// <summary>
    /// Tạo Button Unity có vùng bấm rõ ràng; sự kiện XR được PointableCanvas của Meta chuyển vào Button này.
    /// </summary>
    private static Button CreateButton(string name, Transform parent, string label,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
    {
        GameObject gameObject = CreateUiObject(name, parent);
        RectTransform rect = gameObject.GetComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;

        Image image = gameObject.AddComponent<Image>();
        image.color = AccentColor;
        image.raycastTarget = true;

        Button button = gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        ColorBlock colors = button.colors;
        colors.normalColor = AccentColor;
        colors.highlightedColor = new Color32(75, 232, 214, 255);
        colors.pressedColor = new Color32(31, 174, 158, 255);
        colors.selectedColor = colors.highlightedColor;
        button.colors = colors;

        CreateText("Label", gameObject.transform, label, 15, FontStyles.Bold, PanelColor,
            Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, TextAlignmentOptions.Center);
        return button;
    }
}
