using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class HandJointDebugVisualizer : MonoBehaviour
{
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

    public enum CoordinateSpaceMode
    {
        World,
        LocalToReference,
        HandLocal
    }

    [Serializable]
    public class TextOutput
    {
        public Text uiText;
        public TMP_Text tmpText;

        public void SetText(string value)
        {
            if (uiText != null && uiText.text != value)
            {
                uiText.text = value;
            }

            if (tmpText != null && tmpText.text != value)
            {
                tmpText.text = value;
            }
        }

        public void SetColor(Color color)
        {
            if (uiText != null && uiText.color != color)
            {
                uiText.color = color;
            }

            if (tmpText != null && tmpText.color != color)
            {
                tmpText.color = color;
            }
        }
    }

    [Serializable]
    public class JointUiBinding
    {
        public OVRSkeleton.BoneId boneId;
        public string displayName;
        public Color color = Color.white;
        public TextOutput positionText = new TextOutput();
        public TextOutput rotationText = new TextOutput();
        public Transform marker;
        public GameObject activeWhenTracked;
        public bool showRotation;
    }

    [Serializable]
    public class HandUiBindings
    {
        [Header("Source")]
        public GameObject handObject;
        public OVRHand hand;
        public OVRSkeleton skeleton;

        [Header("Status UI")]
        public TextOutput trackedText = new TextOutput();
        public TextOutput summaryText = new TextOutput();
        public GameObject activeWhenTracked;

        [Header("Joint UI")]
        public JointUiBinding wrist = new JointUiBinding { boneId = OVRSkeleton.BoneId.Hand_WristRoot, displayName = "Co tay", color = new Color(0.1f, 0.85f, 1f, 1f) };
        public JointUiBinding thumbTip = new JointUiBinding { boneId = OVRSkeleton.BoneId.Hand_ThumbTip, displayName = "Cai dau ngon", color = new Color(1f, 0.84f, 0.15f, 1f) };
        public JointUiBinding indexTip = new JointUiBinding { boneId = OVRSkeleton.BoneId.Hand_IndexTip, displayName = "Tro dau ngon", color = new Color(0.25f, 1f, 0.25f, 1f) };
        public JointUiBinding middleTip = new JointUiBinding { boneId = OVRSkeleton.BoneId.Hand_MiddleTip, displayName = "Giua dau ngon", color = new Color(0.3f, 0.55f, 1f, 1f) };
        public JointUiBinding ringTip = new JointUiBinding { boneId = OVRSkeleton.BoneId.Hand_RingTip, displayName = "Ap ut dau ngon", color = new Color(1f, 0.35f, 1f, 1f) };
        public JointUiBinding pinkyTip = new JointUiBinding { boneId = OVRSkeleton.BoneId.Hand_PinkyTip, displayName = "Ut dau ngon", color = new Color(1f, 0.45f, 0.1f, 1f) };

        [Header("Detailed Joint UI")]
        public JointUiBinding[] detailedJoints = Array.Empty<JointUiBinding>();
    }

    [Header("Optional Sender Source")]
    public HandTrackingUdpSender_Debuggable source;
    public bool pullHandObjectsFromSource = true;

    [Header("Hands")]
    public HandUiBindings leftHand = new HandUiBindings();
    public HandUiBindings rightHand = new HandUiBindings();

    [Header("Coordinate Space")]
    public CoordinateSpaceMode coordinateSpace = CoordinateSpaceMode.World;
    public Transform coordinateSpaceReference;

    [Header("Formatting")]
    public bool showUi = true;
    public bool includeJointName = true;
    public string notTrackedText = "not tracked";
    public string missingText = "-";
    public int decimalPlaces = 3;
    public float markerSize = 0.018f;

    [Header("UI Refresh")]
    [Min(1f)] public float textRefreshRate = 8f;

    [Header("Overlay Refresh")]
    public OVROverlayCanvas overlayCanvas;
    public bool requestOverlayRedraw = true;

    private float nextTextRefreshTime;
    private bool refreshTextThisFrame;

    public void SetVisible(bool visible)
    {
        showUi = visible;

        if (!visible)
        {
            HideAllJoints();
        }
    }

    public void ToggleVisible()
    {
        SetVisible(!showUi);
    }

    public void ApplyDefaultJointStyles()
    {
        ApplyDefaultJointStyles(leftHand);
        ApplyDefaultJointStyles(rightHand);
    }

    private void ApplyDefaultJointStyles(HandUiBindings handUi)
    {
        ApplyJointStyle(handUi.wrist, OVRSkeleton.BoneId.Hand_WristRoot, "Co tay", new Color(0.1f, 0.85f, 1f, 1f));
        ApplyJointStyle(handUi.thumbTip, OVRSkeleton.BoneId.Hand_ThumbTip, "Cai dau ngon", new Color(1f, 0.84f, 0.15f, 1f));
        ApplyJointStyle(handUi.indexTip, OVRSkeleton.BoneId.Hand_IndexTip, "Tro dau ngon", new Color(0.25f, 1f, 0.25f, 1f));
        ApplyJointStyle(handUi.middleTip, OVRSkeleton.BoneId.Hand_MiddleTip, "Giua dau ngon", new Color(0.3f, 0.55f, 1f, 1f));
        ApplyJointStyle(handUi.ringTip, OVRSkeleton.BoneId.Hand_RingTip, "Ap ut dau ngon", new Color(1f, 0.35f, 1f, 1f));
        ApplyJointStyle(handUi.pinkyTip, OVRSkeleton.BoneId.Hand_PinkyTip, "Ut dau ngon", new Color(1f, 0.45f, 0.1f, 1f));
        handUi.detailedJoints = CreateDetailedJointBindings();
    }

    private void ApplyJointStyle(JointUiBinding binding, OVRSkeleton.BoneId boneId, string displayName, Color color)
    {
        binding.boneId = boneId;
        binding.displayName = displayName;
        binding.color = color;
    }

    private void LateUpdate()
    {
        EnsureSources();
        refreshTextThisFrame = Time.unscaledTime >= nextTextRefreshTime;

        if (refreshTextThisFrame)
        {
            nextTextRefreshTime = Time.unscaledTime + 1f / Mathf.Max(1f, textRefreshRate);
        }

        if (!showUi)
        {
            SetHandActive(leftHand, false);
            SetHandActive(rightHand, false);
            return;
        }

        UpdateHand("L", leftHand);
        UpdateHand("R", rightHand);

        if (refreshTextThisFrame && requestOverlayRedraw && overlayCanvas != null)
        {
            overlayCanvas.SetFrameDirty();
        }
    }

    private void EnsureSources()
    {
        if (source == null)
        {
            source = FindFirstObjectByType<HandTrackingUdpSender_Debuggable>();
        }

        if (pullHandObjectsFromSource && source != null)
        {
            if (leftHand.handObject == null)
            {
                leftHand.handObject = source.leftHandObject;
            }

            if (rightHand.handObject == null)
            {
                rightHand.handObject = source.rightHandObject;
            }
        }

        CacheHandComponents(leftHand);
        CacheHandComponents(rightHand);
    }

    private void CacheHandComponents(HandUiBindings handUi)
    {
        if (handUi.handObject == null)
        {
            return;
        }

        if (handUi.hand == null)
        {
            handUi.hand = handUi.handObject.GetComponent<OVRHand>();
        }

        if (handUi.skeleton == null)
        {
            handUi.skeleton = handUi.handObject.GetComponent<OVRSkeleton>();
        }
    }

    private void UpdateHand(string side, HandUiBindings handUi)
    {
        bool tracked = handUi.hand != null && handUi.hand.IsTracked;
        bool hasSkeleton = handUi.skeleton != null && handUi.skeleton.Bones != null && handUi.skeleton.Bones.Count > 0;
        bool ready = tracked && hasSkeleton;

        SetHandActive(handUi, ready);
        SetTextWhenDue(handUi.trackedText, ready ? "tracked" : notTrackedText);

        if (!ready)
        {
            SetTextWhenDue(handUi.summaryText, $"{side} {notTrackedText}");
            SetStandardJointsMissing(handUi);
            return;
        }

        UpdateStandardJoints(handUi);

        Vector3 wrist = GetOutputPosition(handUi.skeleton, OVRSkeleton.BoneId.Hand_WristRoot);
        Vector3 index = GetOutputPosition(handUi.skeleton, OVRSkeleton.BoneId.Hand_IndexTip);
        SetTextWhenDue(handUi.summaryText, $"{side} wrist {FormatVector(wrist)} | index {FormatVector(index)}");
    }

    private void UpdateStandardJoints(HandUiBindings handUi)
    {
        UpdateJoint(handUi, handUi.wrist);
        UpdateJoint(handUi, handUi.thumbTip);
        UpdateJoint(handUi, handUi.indexTip);
        UpdateJoint(handUi, handUi.middleTip);
        UpdateJoint(handUi, handUi.ringTip);
        UpdateJoint(handUi, handUi.pinkyTip);

        if (handUi.detailedJoints == null)
        {
            return;
        }

        foreach (JointUiBinding binding in handUi.detailedJoints)
        {
            UpdateJoint(handUi, binding);
        }
    }

    private void UpdateJoint(HandUiBindings handUi, JointUiBinding binding)
    {
        Transform bone = FindBone(handUi.skeleton, binding.boneId);
        if (bone == null)
        {
            SetMissing(binding);
            return;
        }

        Vector3 outputPosition = ToOutputPosition(bone.position, handUi.skeleton);
        Quaternion outputRotation = ToOutputRotation(bone.rotation, handUi.skeleton);
        string label = string.IsNullOrWhiteSpace(binding.displayName) ? ShortBoneName(binding.boneId) : binding.displayName;
        string prefix = includeJointName ? label + " " : "";

        SetTextWhenDue(binding.positionText, prefix + FormatVector(outputPosition));
        SetColorWhenDue(binding.positionText, binding.color);

        if (binding.showRotation)
        {
            SetTextWhenDue(binding.rotationText, FormatQuaternion(outputRotation));
            SetColorWhenDue(binding.rotationText, binding.color);
        }

        if (binding.marker != null)
        {
            SetActiveIfChanged(binding.marker.gameObject, true);
            binding.marker.position = bone.position;
            binding.marker.localScale = Vector3.one * markerSize;
            ApplyMarkerColor(binding.marker, binding.color);
        }

        if (binding.activeWhenTracked != null)
        {
            SetActiveIfChanged(binding.activeWhenTracked, true);
        }
    }

    private void SetMissing(JointUiBinding binding)
    {
        SetTextWhenDue(binding.positionText, missingText);
        SetTextWhenDue(binding.rotationText, missingText);

        if (binding.marker != null)
        {
            SetActiveIfChanged(binding.marker.gameObject, false);
        }

        if (binding.activeWhenTracked != null)
        {
            SetActiveIfChanged(binding.activeWhenTracked, false);
        }
    }

    private void SetStandardJointsMissing(HandUiBindings handUi)
    {
        SetMissing(handUi.wrist);
        SetMissing(handUi.thumbTip);
        SetMissing(handUi.indexTip);
        SetMissing(handUi.middleTip);
        SetMissing(handUi.ringTip);
        SetMissing(handUi.pinkyTip);

        if (handUi.detailedJoints == null)
        {
            return;
        }

        foreach (JointUiBinding binding in handUi.detailedJoints)
        {
            SetMissing(binding);
        }
    }

    private void HideAllJoints()
    {
        SetHandActive(leftHand, false);
        SetHandActive(rightHand, false);
        SetStandardJointsMissing(leftHand);
        SetStandardJointsMissing(rightHand);
    }

    private void ApplyMarkerColor(Transform marker, Color color)
    {
        Renderer renderer = marker.GetComponent<Renderer>();
        if (renderer != null)
        {
            Material material = renderer.material;
            Shader markerShader = FindMarkerShader();
            if (markerShader != null && (material.shader == null || material.shader.name.Contains("Sprites")))
            {
                material.shader = markerShader;
            }

            material.color = color;

            if (material.HasProperty(ColorId))
            {
                material.SetColor(ColorId, color);
            }

            if (material.HasProperty(BaseColorId))
            {
                material.SetColor(BaseColorId, color);
            }

            if (material.HasProperty(EmissionColorId))
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor(EmissionColorId, color);
            }
        }

        Graphic graphic = marker.GetComponent<Graphic>();
        if (graphic != null)
        {
            graphic.color = color;
        }
    }

    private Shader FindMarkerShader()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
        {
            shader = Shader.Find("Unlit/Color");
        }

        return shader;
    }

    private void SetHandActive(HandUiBindings handUi, bool active)
    {
        SetActiveIfChanged(handUi.activeWhenTracked, active);
    }

    private void SetTextWhenDue(TextOutput output, string value)
    {
        if (refreshTextThisFrame)
        {
            output?.SetText(value);
        }
    }

    private void SetColorWhenDue(TextOutput output, Color color)
    {
        if (refreshTextThisFrame)
        {
            output?.SetColor(color);
        }
    }

    private static void SetActiveIfChanged(GameObject target, bool active)
    {
        if (target != null && target.activeSelf != active)
        {
            target.SetActive(active);
        }
    }

    private Vector3 GetOutputPosition(OVRSkeleton skeleton, OVRSkeleton.BoneId boneId)
    {
        Transform bone = FindBone(skeleton, boneId);
        return bone != null ? ToOutputPosition(bone.position, skeleton) : Vector3.zero;
    }

    private Vector3 ToOutputPosition(Vector3 worldPosition, OVRSkeleton skeleton)
    {
        if (coordinateSpace == CoordinateSpaceMode.HandLocal)
        {
            Transform wrist = FindBone(skeleton, OVRSkeleton.BoneId.Hand_WristRoot);
            if (wrist != null)
            {
                return Quaternion.Inverse(wrist.rotation) * (worldPosition - wrist.position);
            }
        }

        if (coordinateSpace == CoordinateSpaceMode.LocalToReference && coordinateSpaceReference != null)
        {
            return coordinateSpaceReference.InverseTransformPoint(worldPosition);
        }

        return worldPosition;
    }

    private Quaternion ToOutputRotation(Quaternion worldRotation, OVRSkeleton skeleton)
    {
        if (coordinateSpace == CoordinateSpaceMode.HandLocal)
        {
            Transform wrist = FindBone(skeleton, OVRSkeleton.BoneId.Hand_WristRoot);
            if (wrist != null)
            {
                return Quaternion.Inverse(wrist.rotation) * worldRotation;
            }
        }

        if (coordinateSpace == CoordinateSpaceMode.LocalToReference && coordinateSpaceReference != null)
        {
            return Quaternion.Inverse(coordinateSpaceReference.rotation) * worldRotation;
        }

        return worldRotation;
    }

    private Transform FindBone(OVRSkeleton skeleton, OVRSkeleton.BoneId boneId)
    {
        if (skeleton == null || skeleton.Bones == null)
        {
            return null;
        }

        OVRSkeleton.BoneId resolvedBoneId = MetaHandBoneIdResolver.ResolveLegacyIdForSkeleton(skeleton, boneId);
        if (resolvedBoneId == OVRSkeleton.BoneId.Invalid)
        {
            return null;
        }

        foreach (OVRBone bone in skeleton.Bones)
        {
            if (bone.Id == resolvedBoneId)
            {
                return bone.Transform;
            }
        }

        return null;
    }

    private string ShortBoneName(OVRSkeleton.BoneId boneId)
    {
        string name = boneId.ToString();
        return name.StartsWith("Hand_") ? name.Substring(5) : name;
    }

    private string FormatVector(Vector3 value)
    {
        string format = GetNumberFormat();
        return $"({value.x.ToString(format)}, {value.y.ToString(format)}, {value.z.ToString(format)})";
    }

    private string FormatQuaternion(Quaternion value)
    {
        string format = GetNumberFormat();
        return $"({value.x.ToString(format)}, {value.y.ToString(format)}, {value.z.ToString(format)}, {value.w.ToString(format)})";
    }

    private string GetNumberFormat()
    {
        int places = Mathf.Max(0, decimalPlaces);
        return places == 0 ? "0" : "0." + new string('0', places);
    }

    private JointUiBinding[] CreateDetailedJointBindings()
    {
        return new[]
        {
            CreateJointBinding(OVRSkeleton.BoneId.Hand_ForearmStub, "Can tay", new Color(0.45f, 0.95f, 1f, 1f)),

            CreateJointBinding(OVRSkeleton.BoneId.Hand_Thumb0, "Cai 0 / goc", new Color(1f, 0.95f, 0.45f, 1f)),
            CreateJointBinding(OVRSkeleton.BoneId.Hand_Thumb1, "Cai 1 / ban tay", new Color(1f, 0.9f, 0.35f, 1f)),
            CreateJointBinding(OVRSkeleton.BoneId.Hand_Thumb2, "Cai 2 / dot gan", new Color(1f, 0.84f, 0.25f, 1f)),
            CreateJointBinding(OVRSkeleton.BoneId.Hand_Thumb3, "Cai 3 / dot xa", new Color(1f, 0.78f, 0.15f, 1f)),
            CreateJointBinding(OVRSkeleton.BoneId.Hand_ThumbTip, "Cai dau ngon", new Color(1f, 0.7f, 0.05f, 1f)),

            CreateJointBinding(OVRSkeleton.BoneId.Hand_Index1, "Tro 1 / dot gan", new Color(0.55f, 1f, 0.55f, 1f)),
            CreateJointBinding(OVRSkeleton.BoneId.Hand_Index2, "Tro 2 / dot giua", new Color(0.4f, 1f, 0.4f, 1f)),
            CreateJointBinding(OVRSkeleton.BoneId.Hand_Index3, "Tro 3 / dot xa", new Color(0.25f, 1f, 0.25f, 1f)),
            CreateJointBinding(OVRSkeleton.BoneId.Hand_IndexTip, "Tro dau ngon", new Color(0.05f, 0.9f, 0.05f, 1f)),

            CreateJointBinding(OVRSkeleton.BoneId.Hand_Middle1, "Giua 1 / dot gan", new Color(0.55f, 0.75f, 1f, 1f)),
            CreateJointBinding(OVRSkeleton.BoneId.Hand_Middle2, "Giua 2 / dot giua", new Color(0.4f, 0.65f, 1f, 1f)),
            CreateJointBinding(OVRSkeleton.BoneId.Hand_Middle3, "Giua 3 / dot xa", new Color(0.3f, 0.55f, 1f, 1f)),
            CreateJointBinding(OVRSkeleton.BoneId.Hand_MiddleTip, "Giua dau ngon", new Color(0.1f, 0.35f, 1f, 1f)),

            CreateJointBinding(OVRSkeleton.BoneId.Hand_Ring1, "Ap ut 1 / dot gan", new Color(1f, 0.65f, 1f, 1f)),
            CreateJointBinding(OVRSkeleton.BoneId.Hand_Ring2, "Ap ut 2 / dot giua", new Color(1f, 0.5f, 1f, 1f)),
            CreateJointBinding(OVRSkeleton.BoneId.Hand_Ring3, "Ap ut 3 / dot xa", new Color(1f, 0.35f, 1f, 1f)),
            CreateJointBinding(OVRSkeleton.BoneId.Hand_RingTip, "Ap ut dau ngon", new Color(0.95f, 0.1f, 1f, 1f)),

            CreateJointBinding(OVRSkeleton.BoneId.Hand_Pinky0, "Ut 0 / ban tay", new Color(1f, 0.75f, 0.45f, 1f)),
            CreateJointBinding(OVRSkeleton.BoneId.Hand_Pinky1, "Ut 1 / dot gan", new Color(1f, 0.62f, 0.3f, 1f)),
            CreateJointBinding(OVRSkeleton.BoneId.Hand_Pinky2, "Ut 2 / dot giua", new Color(1f, 0.52f, 0.2f, 1f)),
            CreateJointBinding(OVRSkeleton.BoneId.Hand_Pinky3, "Ut 3 / dot xa", new Color(1f, 0.45f, 0.1f, 1f)),
            CreateJointBinding(OVRSkeleton.BoneId.Hand_PinkyTip, "Ut dau ngon", new Color(1f, 0.28f, 0.02f, 1f)),
        };
    }

    private JointUiBinding CreateJointBinding(OVRSkeleton.BoneId boneId, string displayName, Color color)
    {
        return new JointUiBinding
        {
            boneId = boneId,
            displayName = displayName,
            color = color
        };
    }
}
