using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.UI;

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

        if (GUILayout.Button("Add Hide UI Buttons To Existing Canvas"))
        {
            AddVisibilityButtonsToExistingCanvas();
        }

        if (GUILayout.Button("Configure Existing UI Overlay For XR"))
        {
            ConfigureExistingUiOverlay(visualizer);
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

        GameObject canvasObject = new GameObject("Hand Joint UI Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        Undo.RegisterCreatedObjectUndo(canvasObject, "Create Hand Joint UI Canvas");

        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;

        RectTransform canvasRect = canvasObject.GetComponent<RectTransform>();
        canvasRect.sizeDelta = new Vector2(PanelWidth, PanelHeight);
        canvasRect.localScale = Vector3.one * 0.0018f;
        PlaceCanvas(canvasObject.transform);

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
        GameObject markerRoot = new GameObject("Hand Joint Color Markers");
        Undo.RegisterCreatedObjectUndo(markerRoot, "Create Hand Joint Color Markers");

        CreateVisualGrabHeader(panelObject.transform, font);
        CreateVisibilityButtons(canvasObject, panelObject, font);
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

    private static void AddVisibilityButtonsToExistingCanvas()
    {
        GameObject canvasObject = GameObject.Find("Hand Joint UI Canvas");
        Transform panel = canvasObject != null ? canvasObject.transform.Find("Panel") : null;

        if (panel == null)
        {
            Debug.LogWarning("Hand Joint UI Canvas or its Panel was not found in the scene.");
            return;
        }

        CreateVisibilityButtons(canvasObject, panel.gameObject, GetBuiltinFont());
        EditorUtility.SetDirty(canvasObject);
        Selection.activeGameObject = canvasObject;
    }

    private static void ConfigureExistingUiOverlay(HandJointDebugVisualizer visualizer)
    {
        GameObject canvasObject = GameObject.Find("Hand Joint UI Canvas");
        Component overlay = canvasObject != null ? canvasObject.GetComponent("OVROverlayCanvas") : null;
        int overlayLayer = LayerMask.NameToLayer("Overlay UI");

        if (canvasObject == null || overlay == null)
        {
            Debug.LogWarning("Add OVROverlayCanvas to Hand Joint UI Canvas before configuring it.");
            return;
        }

        if (overlayLayer < 0)
        {
            Debug.LogWarning("The Overlay UI layer was not found.");
            return;
        }

        Undo.RecordObject(canvasObject, "Configure Hand Joint UI Overlay");
        canvasObject.layer = overlayLayer;
        SetLayerRecursively(canvasObject.transform.Find("Panel"), overlayLayer);
        SetLayerRecursively(canvasObject.transform.Find("Show UI Button"), overlayLayer);

        Camera xrCamera = FindCameraTarget()?.GetComponent<Camera>();
        if (xrCamera != null)
        {
            Undo.RecordObject(xrCamera, "Exclude Overlay UI From XR Camera");
            xrCamera.cullingMask &= ~(1 << overlayLayer);
        }

        SerializedObject overlayProperties = new SerializedObject(overlay);
        overlayProperties.FindProperty("manualRedraw").boolValue = true;
        overlayProperties.FindProperty("renderInterval").intValue = 1;
        overlayProperties.FindProperty("_dynamicResolution").boolValue = false;
        overlayProperties.ApplyModifiedPropertiesWithoutUndo();

        SerializedObject visualizerProperties = new SerializedObject(visualizer);
        visualizerProperties.FindProperty("overlayCanvas").objectReferenceValue = overlay;
        visualizerProperties.ApplyModifiedPropertiesWithoutUndo();

        EditorUtility.SetDirty(canvasObject);
        EditorUtility.SetDirty(overlay);
        EditorUtility.SetDirty(xrCamera);
        EditorUtility.SetDirty(visualizer);
        Selection.activeGameObject = canvasObject;
    }

    private static void SetLayerRecursively(Transform root, int layer)
    {
        if (root == null)
        {
            return;
        }

        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
        {
            child.gameObject.layer = layer;
        }
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

    private static Text CreateText(Transform parent, Font font, string name, float x, ref float y, int fontSize, FontStyle fontStyle)
    {
        Text text = CreateText(parent, font, name, x, y, HandColumnWidth, fontSize, fontStyle, Color.white);
        y -= RowHeight;
        return text;
    }

    private static void CreateVisualGrabHeader(Transform parent, Font font)
    {
        GameObject headerObject = new GameObject("Grab Header", typeof(RectTransform), typeof(Image));
        Undo.RegisterCreatedObjectUndo(headerObject, "Create Hand Joint UI Grab Header");
        headerObject.transform.SetParent(parent, false);

        RectTransform rect = headerObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(GrabHeaderLeft, -GrabHeaderTop);
        rect.sizeDelta = new Vector2(GrabHeaderWidth, GrabHeaderHeight);

        Image image = headerObject.GetComponent<Image>();
        image.color = new Color(0.055f, 0.09f, 0.12f, 0.92f);
        image.raycastTarget = false;

        GameObject labelObject = new GameObject("Label", typeof(RectTransform), typeof(Text));
        Undo.RegisterCreatedObjectUndo(labelObject, "Create Grab Header Label");
        labelObject.transform.SetParent(headerObject.transform, false);

        RectTransform labelRect = labelObject.GetComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = new Vector2(14f, 0f);
        labelRect.offsetMax = new Vector2(-14f, 0f);

        Text label = labelObject.GetComponent<Text>();
        label.font = font;
        label.fontSize = 15;
        label.fontStyle = FontStyle.Bold;
        label.alignment = TextAnchor.MiddleRight;
        label.raycastTarget = false;
        label.color = new Color(0.72f, 0.92f, 1f, 1f);
        label.text = "Grab here";
    }

    private static void CreateVisibilityButtons(GameObject canvasObject, GameObject panelObject, Font font)
    {
        Button hideButton = CreateOrUpdateButton(panelObject.transform, font, "Hide UI Button", "Hide UI", -18f, -18f, 130f);
        Button showButton = CreateOrUpdateButton(canvasObject.transform, font, "Show UI Button", "Show UI", -18f, -18f, 130f);

        Undo.RecordObject(hideButton, "Configure Hide UI Button");
        hideButton.onClick = new Button.ButtonClickedEvent();
        UnityEventTools.AddBoolPersistentListener(hideButton.onClick, panelObject.SetActive, false);
        UnityEventTools.AddBoolPersistentListener(hideButton.onClick, showButton.gameObject.SetActive, true);

        Undo.RecordObject(showButton, "Configure Show UI Button");
        showButton.onClick = new Button.ButtonClickedEvent();
        UnityEventTools.AddBoolPersistentListener(showButton.onClick, panelObject.SetActive, true);
        UnityEventTools.AddBoolPersistentListener(showButton.onClick, showButton.gameObject.SetActive, false);

        showButton.gameObject.SetActive(false);
    }

    private static Button CreateOrUpdateButton(Transform parent, Font font, string name, string labelText, float xFromRight, float yFromTop, float width)
    {
        Transform existing = parent.Find(name);
        GameObject buttonObject;
        if (existing != null)
        {
            buttonObject = existing.gameObject;
            Undo.RecordObject(buttonObject, "Update Hand Joint UI Button");
        }
        else
        {
            buttonObject = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            Undo.RegisterCreatedObjectUndo(buttonObject, "Create Hand Joint UI Button");
            buttonObject.transform.SetParent(parent, false);
        }

        RectTransform rect = buttonObject.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.one;
        rect.anchorMax = Vector2.one;
        rect.pivot = Vector2.one;
        rect.anchoredPosition = new Vector2(xFromRight, yFromTop);
        rect.sizeDelta = new Vector2(width, 42f);

        Image image = buttonObject.GetComponent<Image>();
        image.color = new Color(0.08f, 0.12f, 0.16f, 0.95f);

        Transform existingLabel = buttonObject.transform.Find("Label");
        GameObject labelObject;
        if (existingLabel != null)
        {
            labelObject = existingLabel.gameObject;
        }
        else
        {
            labelObject = new GameObject("Label", typeof(RectTransform), typeof(Text));
            Undo.RegisterCreatedObjectUndo(labelObject, "Create Hand Joint UI Button Label");
            labelObject.transform.SetParent(buttonObject.transform, false);
        }

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

        return buttonObject.GetComponent<Button>();
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
