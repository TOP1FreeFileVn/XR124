using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.UI;
using System;

[CustomEditor(typeof(HandJointDebugVisualizer))]
public class HandJointDebugVisualizerEditor : Editor
{
    private const float PanelWidth = 1500f;
    private const float PanelHeight = 620f;
    private const float RowHeight = 30f;
    private const float HandColumnWidth = 710f;
    private const float JointColumnWidth = 118f;
    private const float GrabHeaderLeft = 18f;
    private const float GrabHeaderTop = 18f;
    private const float GrabHeaderWidth = 900f;
    private const float GrabHeaderHeight = 46f;
    private const float GrabHeaderDepth = 18f;

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EditorGUILayout.Space(12f);
        EditorGUILayout.LabelField("UI Setup", EditorStyles.boldLabel);

        HandJointDebugVisualizer visualizer = (HandJointDebugVisualizer)target;

        if (GUILayout.Button("Auto Assign Hands From Sender"))
        {
            AutoAssignHands(visualizer);
        }

        if (GUILayout.Button("Apply Default Joint Colors"))
        {
            Undo.RecordObject(visualizer, "Apply Default Joint Colors");
            visualizer.ApplyDefaultJointStyles();
            EditorUtility.SetDirty(visualizer);
        }

        if (GUILayout.Button("Create And Bind World Space UI"))
        {
            CreateAndBindWorldSpaceUi(visualizer);
        }

        if (GUILayout.Button("Add Grab Header To Existing UI"))
        {
            AddGrabHeaderToExistingUi();
        }

        EditorGUILayout.Space(12f);
        EditorGUILayout.LabelField("Meta Hand Input Setup", EditorStyles.boldLabel);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Set Hands Only"))
        {
            SetHandTrackingSupport(OVRProjectConfig.HandTrackingSupport.HandsOnly);
        }

        if (GUILayout.Button("Set Controllers + Hands"))
        {
            SetHandTrackingSupport(OVRProjectConfig.HandTrackingSupport.ControllersAndHands);
        }
        EditorGUILayout.EndHorizontal();

        if (GUILayout.Button("Set Controllers Only"))
        {
            SetHandTrackingSupport(OVRProjectConfig.HandTrackingSupport.ControllersOnly);
        }
    }

    private static void AutoAssignHands(HandJointDebugVisualizer visualizer)
    {
        Undo.RecordObject(visualizer, "Auto Assign Hand UI Sources");

        if (visualizer.source == null)
        {
            visualizer.source = FindFirstObjectByType<HandTrackingUdpSender_Debuggable>();
        }

        if (visualizer.source != null)
        {
            visualizer.leftHand.handObject = visualizer.source.leftHandObject;
            visualizer.rightHand.handObject = visualizer.source.rightHandObject;
        }

        CacheHandComponents(visualizer.leftHand);
        CacheHandComponents(visualizer.rightHand);

        EditorUtility.SetDirty(visualizer);
    }

    private static void CreateAndBindWorldSpaceUi(HandJointDebugVisualizer visualizer)
    {
        Undo.RecordObject(visualizer, "Create Hand Joint UI");
        AutoAssignHands(visualizer);
        visualizer.ApplyDefaultJointStyles();
        EnsureTextOutputs(visualizer);

        GameObject canvasObject = new GameObject("Hand Joint UI Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(WorldSpaceDebugUiFollower), typeof(HandJointUiPanelToggle));
        Undo.RegisterCreatedObjectUndo(canvasObject, "Create Hand Joint UI Canvas");
        Component inputModeUi = AddComponentByTypeName(canvasObject, "MetaHandInputModeUi");

        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;

        RectTransform canvasRect = canvasObject.GetComponent<RectTransform>();
        canvasRect.sizeDelta = new Vector2(PanelWidth, PanelHeight);
        canvasRect.localScale = Vector3.one * 0.0018f;
        PlaceCanvas(canvasObject.transform);

        WorldSpaceDebugUiFollower follower = canvasObject.GetComponent<WorldSpaceDebugUiFollower>();
        follower.target = FindCameraTarget();
        follower.distance = 1.5f;
        follower.viewPlaneOffset = new Vector2(0f, -0.08f);
        follower.followEveryFrame = false;
        follower.keepUpright = true;
        follower.forceCameraCullingMask = true;

        GameObject panelObject = new GameObject("Panel", typeof(RectTransform), typeof(Image));
        Undo.RegisterCreatedObjectUndo(panelObject, "Create Hand Joint UI Panel");
        panelObject.transform.SetParent(canvasObject.transform, false);

        RectTransform panelRect = panelObject.GetComponent<RectTransform>();
        panelRect.anchorMin = Vector2.zero;
        panelRect.anchorMax = Vector2.one;
        panelRect.offsetMin = Vector2.zero;
        panelRect.offsetMax = Vector2.zero;

        Image panelImage = panelObject.GetComponent<Image>();
        panelImage.color = new Color(0.02f, 0.025f, 0.03f, 0.78f);

        Font font = GetBuiltinFont();
        float y = -24f;
        Text hideLabel = CreateButton(canvasObject.transform, font, "Hide Debug UI Button", "Hide UI", -18f, -18f, 130f);
        Text showLabel = CreateButton(canvasObject.transform, font, "Show Debug UI Button", "Show UI", -18f, -18f, 130f);
        Text inputModeLabel = CreateText(canvasObject.transform, font, "Input: -", PanelWidth - 500f, -66f, 480f, 14, FontStyle.Bold, new Color(0.72f, 0.92f, 1f, 1f));

        HandJointUiPanelToggle panelToggle = canvasObject.GetComponent<HandJointUiPanelToggle>();
        panelToggle.panelRoot = panelObject;
        panelToggle.hideButtonRoot = hideLabel.GetComponentInParent<Button>().gameObject;
        panelToggle.showButtonRoot = showLabel.GetComponentInParent<Button>().gameObject;
        panelToggle.buttonLabel = showLabel;
        panelToggle.visibleText = "Hide UI";
        panelToggle.hiddenText = "Show UI";
        panelToggle.startVisible = true;

        Button hideButton = hideLabel.GetComponentInParent<Button>();
        UnityEventTools.AddPersistentListener(hideButton.onClick, panelToggle.Hide);

        Button showButton = showLabel.GetComponentInParent<Button>();
        UnityEventTools.AddPersistentListener(showButton.onClick, panelToggle.Show);
        panelToggle.SetVisible(true);

        CreateInputModeButtons(canvasObject.transform, font, inputModeUi);
        ConfigureInputModeUi(inputModeUi, inputModeLabel);

        GameObject markerRoot = new GameObject("Hand Joint Color Markers");
        Undo.RegisterCreatedObjectUndo(markerRoot, "Create Hand Joint Color Markers");

        CreateGrabHeader(canvasObject, panelObject.transform, font);
        CreateHeader(panelObject.transform, font, "Hand Joint UI", ref y);
        float leftY = -68f;
        float rightY = -68f;
        CreateHandTable(panelObject.transform, font, markerRoot.transform, visualizer.leftHand, "Left", 24f, ref leftY);
        CreateHandTable(panelObject.transform, font, markerRoot.transform, visualizer.rightHand, "Right", 764f, ref rightY);

        visualizer.coordinateSpace = HandJointDebugVisualizer.CoordinateSpaceMode.HandLocal;
        visualizer.showUi = true;
        visualizer.includeJointName = false;

        EditorUtility.SetDirty(visualizer);
        Selection.activeGameObject = canvasObject;
    }

    private static void SetHandTrackingSupport(OVRProjectConfig.HandTrackingSupport support)
    {
        MetaHandInputModeEditorBridge.SetHandTrackingSupport((int)support);
        Debug.Log("Meta Hand Tracking Support set to " + support);
    }

    private static void AddGrabHeaderToExistingUi()
    {
        GameObject canvasObject = GameObject.Find("Hand Joint UI Canvas");
        if (canvasObject == null)
        {
            Debug.LogWarning("Hand Joint UI Canvas was not found in the scene.");
            return;
        }

        Transform panel = canvasObject.transform.Find("Panel");
        Transform visualParent = panel != null ? panel : canvasObject.transform;
        CreateGrabHeader(canvasObject, visualParent, GetBuiltinFont());

        EditorUtility.SetDirty(canvasObject);
        Selection.activeGameObject = canvasObject;
    }

    private static void CreateHandTable(Transform parent, Font font, Transform markerRoot, HandJointDebugVisualizer.HandUiBindings hand, string label, float x, ref float y)
    {
        CreateHeader(parent, font, label + " Hand", x, ref y);
        hand.trackedText.uiText = CreateText(parent, font, label + " tracked", x, ref y, 15, FontStyle.Bold);
        hand.summaryText.uiText = CreateText(parent, font, label + " summary", x, ref y, 12, FontStyle.Normal);

        y -= 8f;
        CreateTableHeader(parent, font, x, ref y);
        CreateSingleJointRow(parent, font, markerRoot, hand.wrist, label, "Co tay", x, ref y);
        CreateFingerRow(parent, font, markerRoot, hand, label, "Ngon cai", x, ref y,
            OVRSkeleton.BoneId.Hand_Thumb0,
            OVRSkeleton.BoneId.Hand_Thumb1,
            OVRSkeleton.BoneId.Hand_Thumb2,
            OVRSkeleton.BoneId.Hand_Thumb3,
            OVRSkeleton.BoneId.Hand_ThumbTip);
        CreateFingerRow(parent, font, markerRoot, hand, label, "Ngon tro", x, ref y,
            null,
            OVRSkeleton.BoneId.Hand_Index1,
            OVRSkeleton.BoneId.Hand_Index2,
            OVRSkeleton.BoneId.Hand_Index3,
            OVRSkeleton.BoneId.Hand_IndexTip);
        CreateFingerRow(parent, font, markerRoot, hand, label, "Ngon giua", x, ref y,
            null,
            OVRSkeleton.BoneId.Hand_Middle1,
            OVRSkeleton.BoneId.Hand_Middle2,
            OVRSkeleton.BoneId.Hand_Middle3,
            OVRSkeleton.BoneId.Hand_MiddleTip);
        CreateFingerRow(parent, font, markerRoot, hand, label, "Ngon ap ut", x, ref y,
            null,
            OVRSkeleton.BoneId.Hand_Ring1,
            OVRSkeleton.BoneId.Hand_Ring2,
            OVRSkeleton.BoneId.Hand_Ring3,
            OVRSkeleton.BoneId.Hand_RingTip);
        CreateFingerRow(parent, font, markerRoot, hand, label, "Ngon ut", x, ref y,
            OVRSkeleton.BoneId.Hand_Pinky0,
            OVRSkeleton.BoneId.Hand_Pinky1,
            OVRSkeleton.BoneId.Hand_Pinky2,
            OVRSkeleton.BoneId.Hand_Pinky3,
            OVRSkeleton.BoneId.Hand_PinkyTip);
    }

    private static void CreateTableHeader(Transform parent, Font font, float x, ref float y)
    {
        CreateText(parent, font, "Ngon", x, y, 78f, 13, FontStyle.Bold, new Color(0.78f, 0.86f, 0.95f, 1f));
        CreateText(parent, font, "0 / goc", x + 86f, y, JointColumnWidth, 13, FontStyle.Bold, new Color(0.78f, 0.86f, 0.95f, 1f));
        CreateText(parent, font, "1 / gan", x + 86f + JointColumnWidth, y, JointColumnWidth, 13, FontStyle.Bold, new Color(0.78f, 0.86f, 0.95f, 1f));
        CreateText(parent, font, "2 / giua", x + 86f + JointColumnWidth * 2f, y, JointColumnWidth, 13, FontStyle.Bold, new Color(0.78f, 0.86f, 0.95f, 1f));
        CreateText(parent, font, "3 / xa", x + 86f + JointColumnWidth * 3f, y, JointColumnWidth, 13, FontStyle.Bold, new Color(0.78f, 0.86f, 0.95f, 1f));
        CreateText(parent, font, "Dau ngon", x + 86f + JointColumnWidth * 4f, y, JointColumnWidth, 13, FontStyle.Bold, new Color(0.78f, 0.86f, 0.95f, 1f));
        y -= RowHeight;
    }

    private static void CreateSingleJointRow(Transform parent, Font font, Transform markerRoot, HandJointDebugVisualizer.JointUiBinding binding, string sideLabel, string rowLabel, float x, ref float y)
    {
        CreateText(parent, font, rowLabel, x, y, 78f, 13, FontStyle.Bold, binding.color);
        BindJointAt(parent, font, markerRoot, binding, sideLabel, x + 86f, y, JointColumnWidth * 2f);
        y -= RowHeight;
    }

    private static void CreateFingerRow(Transform parent, Font font, Transform markerRoot, HandJointDebugVisualizer.HandUiBindings hand, string sideLabel, string rowLabel, float x, ref float y, params OVRSkeleton.BoneId?[] boneIds)
    {
        Color rowColor = GetRowColor(hand, boneIds);
        CreateText(parent, font, rowLabel, x, y, 78f, 13, FontStyle.Bold, rowColor);

        for (int i = 0; i < boneIds.Length; i++)
        {
            if (!boneIds[i].HasValue)
            {
                continue;
            }

            HandJointDebugVisualizer.JointUiBinding binding = FindBinding(hand, boneIds[i].Value);
            if (binding != null)
            {
                BindJointAt(parent, font, markerRoot, binding, sideLabel, x + 86f + JointColumnWidth * i, y, JointColumnWidth - 6f);
            }
        }

        y -= RowHeight;
    }

    private static void BindJointAt(Transform parent, Font font, Transform markerRoot, HandJointDebugVisualizer.JointUiBinding binding, string sideLabel, float x, float y, float width)
    {
        EnsureTextOutput(binding);
        string label = string.IsNullOrWhiteSpace(binding.displayName) ? binding.boneId.ToString() : binding.displayName;
        binding.positionText.uiText = CreateText(parent, font, label, x, y, width, 11, FontStyle.Normal, binding.color);
        binding.positionText.uiText.color = binding.color;
        binding.marker = CreateJointMarker(markerRoot, $"{sideLabel} {label}", binding.color);
    }

    private static void CreateGrabHeader(GameObject canvasObject, Transform visualParent, Font font)
    {
        Transform existingHeader = visualParent.Find("Grab Header");
        GameObject headerObject;
        if (existingHeader != null)
        {
            headerObject = existingHeader.gameObject;
            Undo.RecordObject(headerObject, "Update Hand Joint UI Grab Header");
        }
        else
        {
            headerObject = new GameObject("Grab Header", typeof(RectTransform), typeof(Image));
            Undo.RegisterCreatedObjectUndo(headerObject, "Create Hand Joint UI Grab Header");
            headerObject.transform.SetParent(visualParent, false);
        }

        RectTransform rect = headerObject.GetComponent<RectTransform>();
        if (rect == null)
        {
            Debug.LogWarning("Grab Header exists but is not a UI RectTransform.");
            return;
        }

        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(GrabHeaderLeft, -GrabHeaderTop);
        rect.sizeDelta = new Vector2(GrabHeaderWidth, GrabHeaderHeight);

        Image image = headerObject.GetComponent<Image>();
        if (image == null)
        {
            image = Undo.AddComponent<Image>(headerObject);
        }

        image.color = new Color(0.055f, 0.09f, 0.12f, 0.92f);
        image.raycastTarget = false;

        Transform existingLabel = headerObject.transform.Find("Label");
        GameObject labelObject;
        if (existingLabel != null)
        {
            labelObject = existingLabel.gameObject;
            Undo.RecordObject(labelObject, "Update Grab Header Label");
        }
        else
        {
            labelObject = new GameObject("Label", typeof(RectTransform), typeof(Text));
            Undo.RegisterCreatedObjectUndo(labelObject, "Create Grab Header Label");
            labelObject.transform.SetParent(headerObject.transform, false);
        }

        RectTransform labelRect = labelObject.GetComponent<RectTransform>();
        if (labelRect == null)
        {
            Debug.LogWarning("Grab Header Label exists but is not a UI RectTransform.");
            return;
        }

        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = new Vector2(14f, 0f);
        labelRect.offsetMax = new Vector2(-14f, 0f);

        Text label = labelObject.GetComponent<Text>();
        if (label == null)
        {
            label = Undo.AddComponent<Text>(labelObject);
        }

        label.font = font;
        label.fontSize = 15;
        label.fontStyle = FontStyle.Bold;
        label.alignment = TextAnchor.MiddleRight;
        label.raycastTarget = false;
        label.color = new Color(0.72f, 0.92f, 1f, 1f);
        label.text = "Grab here";

        ConfigureGrabHeaderProxy(headerObject, canvasObject.transform);

        BoxCollider grabCollider = canvasObject.GetComponent<BoxCollider>();
        if (grabCollider == null)
        {
            grabCollider = Undo.AddComponent<BoxCollider>(canvasObject);
        }

        grabCollider.isTrigger = true;
        grabCollider.center = new Vector3(
            -PanelWidth * 0.5f + GrabHeaderLeft + GrabHeaderWidth * 0.5f,
            PanelHeight * 0.5f - GrabHeaderTop - GrabHeaderHeight * 0.5f,
            0f);
        grabCollider.size = new Vector3(GrabHeaderWidth, GrabHeaderHeight, GrabHeaderDepth);

        Rigidbody rigidbody = canvasObject.GetComponent<Rigidbody>();
        if (rigidbody == null)
        {
            rigidbody = Undo.AddComponent<Rigidbody>(canvasObject);
        }

        rigidbody.isKinematic = true;
        rigidbody.useGravity = false;
        rigidbody.interpolation = RigidbodyInterpolation.None;
        rigidbody.collisionDetectionMode = CollisionDetectionMode.Discrete;
    }

    private static void ConfigureGrabHeaderProxy(GameObject headerObject, Transform moveRoot)
    {
        Component proxy = GetOrAddComponentByTypeName(headerObject, "HandJointUiGrabHeaderProxy");
        if (proxy == null)
        {
            return;
        }

        SerializedObject serializedObject = new SerializedObject(proxy);
        serializedObject.FindProperty("moveRoot").objectReferenceValue = moveRoot;
        serializedObject.FindProperty("proxyPositionToRoot").boolValue = true;
        serializedObject.FindProperty("resetHeaderRotation").boolValue = true;
        serializedObject.FindProperty("resetHeaderScale").boolValue = true;
        serializedObject.ApplyModifiedPropertiesWithoutUndo();

        proxy.GetType().GetMethod("CaptureCurrentLocalPose")?.Invoke(proxy, null);
        EditorUtility.SetDirty(proxy);
    }

    private static Text CreateText(Transform parent, Font font, string name, float x, ref float y, int fontSize, FontStyle fontStyle)
    {
        Text text = CreateText(parent, font, name, x, y, HandColumnWidth, fontSize, fontStyle, Color.white);
        y -= RowHeight;
        return text;
    }

    private static Text CreateText(Transform parent, Font font, string name, float x, float y, float width, int fontSize, FontStyle fontStyle, Color color)
    {
        GameObject textObject = new GameObject(name, typeof(RectTransform), typeof(Text));
        Undo.RegisterCreatedObjectUndo(textObject, "Create Hand Joint UI Text");
        textObject.transform.SetParent(parent, false);

        RectTransform rect = textObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(x, y);
        rect.sizeDelta = new Vector2(-(PanelWidth - x - width), RowHeight);

        Text text = textObject.GetComponent<Text>();
        text.font = font;
        text.fontSize = fontSize;
        text.fontStyle = fontStyle;
        text.color = color;
        text.alignment = TextAnchor.MiddleLeft;
        text.raycastTarget = false;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        text.text = name;

        return text;
    }

    private static Text CreateButton(Transform parent, Font font, string name, string labelText, float xFromRight, float yFromTop, float width)
    {
        GameObject buttonObject = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        Undo.RegisterCreatedObjectUndo(buttonObject, "Create Hand Joint UI Button");
        buttonObject.transform.SetParent(parent, false);

        RectTransform rect = buttonObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(1f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(1f, 1f);
        rect.anchoredPosition = new Vector2(xFromRight, yFromTop);
        rect.sizeDelta = new Vector2(width, 42f);

        Image image = buttonObject.GetComponent<Image>();
        image.color = new Color(0.08f, 0.12f, 0.16f, 0.95f);

        GameObject labelObject = new GameObject("Label", typeof(RectTransform), typeof(Text));
        Undo.RegisterCreatedObjectUndo(labelObject, "Create Toggle Debug UI Button Label");
        labelObject.transform.SetParent(buttonObject.transform, false);

        RectTransform labelRect = labelObject.GetComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;

        Text label = labelObject.GetComponent<Text>();
        label.font = font;
        label.fontSize = 16;
        label.fontStyle = FontStyle.Bold;
        label.alignment = TextAnchor.MiddleCenter;
        label.raycastTarget = false;
        label.color = new Color(0.72f, 0.92f, 1f, 1f);
        label.text = labelText;

        return label;
    }

    private static void CreateInputModeButtons(Transform parent, Font font, Component inputModeUi)
    {
        Text handsLabel = CreateButton(parent, font, "Hands Only Button", "Hands", -158f, -18f, 120f);
        Text bothLabel = CreateButton(parent, font, "Hands And Controllers Button", "Hands + Ctrl", -288f, -18f, 150f);
        Text controllersLabel = CreateButton(parent, font, "Controllers Only Button", "Ctrl", -448f, -18f, 110f);

        if (inputModeUi == null)
        {
            return;
        }

        SerializedObject serializedObject = new SerializedObject(inputModeUi);
        serializedObject.FindProperty("handsOnlyButton").objectReferenceValue = handsLabel.GetComponentInParent<Button>();
        serializedObject.FindProperty("controllersAndHandsButton").objectReferenceValue = bothLabel.GetComponentInParent<Button>();
        serializedObject.FindProperty("controllersOnlyButton").objectReferenceValue = controllersLabel.GetComponentInParent<Button>();
        serializedObject.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void ConfigureInputModeUi(Component inputModeUi, Text stateText)
    {
        if (inputModeUi == null)
        {
            return;
        }

        SerializedObject serializedObject = new SerializedObject(inputModeUi);
        serializedObject.FindProperty("stateText").objectReferenceValue = stateText;
        serializedObject.ApplyModifiedPropertiesWithoutUndo();
    }

    private static Component AddComponentByTypeName(GameObject target, string typeName)
    {
        Type type = FindType(typeName);
        if (type == null)
        {
            Debug.LogWarning(typeName + " was not found. The generated UI will not include input mode behavior.");
            return null;
        }

        return target.AddComponent(type);
    }

    private static Component GetOrAddComponentByTypeName(GameObject target, string typeName)
    {
        Type type = FindType(typeName);
        if (type == null)
        {
            Debug.LogWarning(typeName + " was not found. The generated grab header will only work when this script compiles.");
            return null;
        }

        Component existing = target.GetComponent(type);
        return existing != null ? existing : Undo.AddComponent(target, type);
    }

    private static Type FindType(string typeName)
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type type = assembly.GetType(typeName);
            if (type != null)
            {
                return type;
            }
        }

        return null;
    }

    private static void CreateHeader(Transform parent, Font font, string title, ref float y)
    {
        Text text = CreateText(parent, font, title, 24f, ref y, 20, FontStyle.Bold);
        text.color = new Color(0.35f, 0.8f, 1f, 1f);
        y -= 4f;
    }

    private static void CreateHeader(Transform parent, Font font, string title, float x, ref float y)
    {
        Text text = CreateText(parent, font, title, x, ref y, 15, FontStyle.Bold);
        text.color = new Color(0.35f, 0.8f, 1f, 1f);
        y -= 4f;
    }

    private static void CreateGroupHeader(Transform parent, Font font, string title, float x, ref float y)
    {
        Text text = CreateText(parent, font, title, x + 12f, ref y, 12, FontStyle.Bold);
        text.color = new Color(0.78f, 0.86f, 0.95f, 1f);
    }

    private static Transform CreateJointMarker(Transform parent, string name, Color color)
    {
        GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Undo.RegisterCreatedObjectUndo(marker, "Create Hand Joint Marker");
        marker.name = name + " Marker";
        marker.transform.SetParent(parent, false);
        marker.transform.localScale = Vector3.one * 0.018f;
        marker.SetActive(false);

        Collider collider = marker.GetComponent<Collider>();
        if (collider != null)
        {
            UnityEngine.Object.DestroyImmediate(collider);
        }

        Renderer renderer = marker.GetComponent<Renderer>();
        if (renderer != null)
        {
            Material material = CreateMarkerMaterial(color);
            renderer.sharedMaterial = material;
        }

        return marker.transform;
    }

    private static Material CreateMarkerMaterial(Color color)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
        {
            shader = Shader.Find("Unlit/Color");
        }

        if (shader == null)
        {
            shader = Shader.Find("Standard");
        }

        Material material = new Material(shader)
        {
            name = "Hand Joint Marker " + ColorUtility.ToHtmlStringRGB(color),
            color = color
        };

        SetMaterialColor(material, "_Color", color);
        SetMaterialColor(material, "_BaseColor", color);
        SetMaterialColor(material, "_EmissionColor", color);
        material.EnableKeyword("_EMISSION");
        return material;
    }

    private static void SetMaterialColor(Material material, string propertyName, Color color)
    {
        if (material.HasProperty(propertyName))
        {
            material.SetColor(propertyName, color);
        }
    }

    private static string GetJointGroup(OVRSkeleton.BoneId boneId)
    {
        string name = boneId.ToString();
        if (name.Contains("Thumb"))
        {
            return "Ngon cai";
        }

        if (name.Contains("Index"))
        {
            return "Ngon tro";
        }

        if (name.Contains("Middle"))
        {
            return "Ngon giua";
        }

        if (name.Contains("Ring"))
        {
            return "Ngon ap ut";
        }

        if (name.Contains("Pinky"))
        {
            return "Ngon ut";
        }

        return "Co tay / can tay";
    }

    private static Color GetRowColor(HandJointDebugVisualizer.HandUiBindings hand, OVRSkeleton.BoneId?[] boneIds)
    {
        for (int i = boneIds.Length - 1; i >= 0; i--)
        {
            if (!boneIds[i].HasValue)
            {
                continue;
            }

            HandJointDebugVisualizer.JointUiBinding binding = FindBinding(hand, boneIds[i].Value);
            if (binding != null)
            {
                return binding.color;
            }
        }

        return Color.white;
    }

    private static HandJointDebugVisualizer.JointUiBinding FindBinding(HandJointDebugVisualizer.HandUiBindings hand, OVRSkeleton.BoneId boneId)
    {
        if (hand.wrist.boneId == boneId)
        {
            return hand.wrist;
        }

        if (hand.thumbTip.boneId == boneId)
        {
            return hand.thumbTip;
        }

        if (hand.indexTip.boneId == boneId)
        {
            return hand.indexTip;
        }

        if (hand.middleTip.boneId == boneId)
        {
            return hand.middleTip;
        }

        if (hand.ringTip.boneId == boneId)
        {
            return hand.ringTip;
        }

        if (hand.pinkyTip.boneId == boneId)
        {
            return hand.pinkyTip;
        }

        if (hand.detailedJoints == null)
        {
            return null;
        }

        foreach (HandJointDebugVisualizer.JointUiBinding binding in hand.detailedJoints)
        {
            if (binding.boneId == boneId)
            {
                return binding;
            }
        }

        return null;
    }

    private static void PlaceCanvas(Transform canvasTransform)
    {
        Transform cameraTarget = FindCameraTarget();
        if (cameraTarget != null)
        {
            Vector3 forward = Vector3.ProjectOnPlane(cameraTarget.forward, Vector3.up);
            if (forward.sqrMagnitude < 0.0001f)
            {
                forward = Vector3.forward;
            }

            forward.Normalize();
            canvasTransform.position = cameraTarget.position + forward * 1.5f + Vector3.up * -0.08f;
            canvasTransform.rotation = Quaternion.LookRotation(forward, Vector3.up);
            return;
        }

        canvasTransform.position = new Vector3(0f, 1.4f, 1.2f);
        canvasTransform.rotation = Quaternion.Euler(0f, 180f, 0f);
    }

    private static Transform FindCameraTarget()
    {
        GameObject centerEye = GameObject.Find("CenterEyeAnchor");
        if (centerEye != null)
        {
            return centerEye.transform;
        }

        Camera camera = Camera.main;
        return camera != null ? camera.transform : null;
    }

    private static void CacheHandComponents(HandJointDebugVisualizer.HandUiBindings hand)
    {
        if (hand.handObject == null)
        {
            return;
        }

        hand.hand = hand.handObject.GetComponent<OVRHand>();
        hand.skeleton = hand.handObject.GetComponent<OVRSkeleton>();
    }

    private static void EnsureTextOutputs(HandJointDebugVisualizer visualizer)
    {
        EnsureTextOutput(visualizer.leftHand.trackedText);
        EnsureTextOutput(visualizer.leftHand.summaryText);
        EnsureTextOutput(visualizer.rightHand.trackedText);
        EnsureTextOutput(visualizer.rightHand.summaryText);

        EnsureTextOutput(visualizer.leftHand.wrist);
        EnsureTextOutput(visualizer.leftHand.thumbTip);
        EnsureTextOutput(visualizer.leftHand.indexTip);
        EnsureTextOutput(visualizer.leftHand.middleTip);
        EnsureTextOutput(visualizer.leftHand.ringTip);
        EnsureTextOutput(visualizer.leftHand.pinkyTip);
        EnsureDetailedTextOutputs(visualizer.leftHand);

        EnsureTextOutput(visualizer.rightHand.wrist);
        EnsureTextOutput(visualizer.rightHand.thumbTip);
        EnsureTextOutput(visualizer.rightHand.indexTip);
        EnsureTextOutput(visualizer.rightHand.middleTip);
        EnsureTextOutput(visualizer.rightHand.ringTip);
        EnsureTextOutput(visualizer.rightHand.pinkyTip);
        EnsureDetailedTextOutputs(visualizer.rightHand);
    }

    private static void EnsureDetailedTextOutputs(HandJointDebugVisualizer.HandUiBindings hand)
    {
        if (hand.detailedJoints == null)
        {
            return;
        }

        foreach (HandJointDebugVisualizer.JointUiBinding binding in hand.detailedJoints)
        {
            EnsureTextOutput(binding);
        }
    }

    private static void EnsureTextOutput(HandJointDebugVisualizer.JointUiBinding binding)
    {
        binding.positionText ??= new HandJointDebugVisualizer.TextOutput();
        binding.rotationText ??= new HandJointDebugVisualizer.TextOutput();
    }

    private static void EnsureTextOutput(HandJointDebugVisualizer.TextOutput output)
    {
        if (output == null)
        {
            Debug.LogWarning("A TextOutput field is null. Recreate the component or reset the script to restore default UI bindings.");
        }
    }

    private static Font GetBuiltinFont()
    {
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font == null)
        {
            font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        }

        return font;
    }
}
