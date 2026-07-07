using UnityEngine;

public class HandTrackingDebugHud : MonoBehaviour
{
    [Header("Source")]
    public HandTrackingUdpSender_Debuggable source;
    public Transform anchor;

    [Header("Display")]
    public bool showHud = true;
    public Vector3 localOffset = new Vector3(-0.75f, 0.35f, 1.6f);
    public float characterSize = 0.008f;
    public Color textColor = Color.green;
    public TextAnchor anchorMode = TextAnchor.UpperLeft;
    public float refreshRate = 15f;
    public bool showFps = true;
    public float fpsSmoothing = 0.9f;

    private TextMesh textMesh;
    private float nextRefreshTime;
    private float smoothedDeltaTime;

    private void Awake()
    {
        EnsureTextMesh();
    }

    private void LateUpdate()
    {
        if (!showHud)
        {
            SetHudActive(false);
            return;
        }

        EnsureReferences();
        EnsureTextMesh();

        if (textMesh == null || anchor == null || source == null)
        {
            SetHudActive(false);
            return;
        }

        UpdateFps();
        SetHudActive(true);
        if (textMesh.transform.parent != anchor)
        {
            textMesh.transform.SetParent(anchor, false);
        }

        textMesh.transform.localPosition = localOffset;
        textMesh.transform.localRotation = Quaternion.identity;
        textMesh.characterSize = Mathf.Max(0.005f, characterSize);
        textMesh.color = textColor;
        textMesh.anchor = anchorMode;

        if (Time.unscaledTime < nextRefreshTime)
        {
            return;
        }

        nextRefreshTime = Time.unscaledTime + (1f / Mathf.Max(1f, refreshRate));
        textMesh.text = BuildHudText();
    }

    private void EnsureReferences()
    {
        if (source == null)
        {
            source = FindFirstObjectByType<HandTrackingUdpSender_Debuggable>();
        }

        if (anchor == null && Camera.main != null)
        {
            anchor = Camera.main.transform;
        }
    }

    private void EnsureTextMesh()
    {
        if (textMesh != null)
        {
            return;
        }

        GameObject hudObject = new GameObject("HandTrackingDebugHudText");
        hudObject.transform.SetParent(anchor != null ? anchor : transform, false);
        textMesh = hudObject.AddComponent<TextMesh>();
        textMesh.fontSize = 64;
        textMesh.alignment = TextAlignment.Left;
        textMesh.anchor = anchorMode;
    }

    private void SetHudActive(bool active)
    {
        if (textMesh != null && textMesh.gameObject.activeSelf != active)
        {
            textMesh.gameObject.SetActive(active);
        }
    }

    private string BuildHudText()
    {
        string fpsLine = showFps ? $"FPS {GetSmoothedFps():0.0}\n" : "";

        return
            fpsLine +
            $"UDP {source.lastStatus} packets={source.packetsSent}\n" +
            $"L tracked={source.leftTracked} wrist={FormatVector(source.leftWristPosition)} index={FormatVector(source.leftIndexTipPosition)} pinch={source.leftIndexPinch:0.000}\n" +
            $"R tracked={source.rightTracked} wrist={FormatVector(source.rightWristPosition)} index={FormatVector(source.rightIndexTipPosition)} pinch={source.rightIndexPinch:0.000}";
    }

    private void UpdateFps()
    {
        float currentDeltaTime = Time.unscaledDeltaTime;
        if (smoothedDeltaTime <= 0f)
        {
            smoothedDeltaTime = currentDeltaTime;
            return;
        }

        float smoothing = Mathf.Clamp01(fpsSmoothing);
        smoothedDeltaTime = (smoothedDeltaTime * smoothing) + (currentDeltaTime * (1f - smoothing));
    }

    private float GetSmoothedFps()
    {
        return smoothedDeltaTime > 0f ? 1f / smoothedDeltaTime : 0f;
    }

    private string FormatVector(Vector3 value)
    {
        return $"({value.x:0.000}, {value.y:0.000}, {value.z:0.000})";
    }
}
