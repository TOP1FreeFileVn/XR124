using UnityEngine;
using UnityEngine.UI;
#if UNITY_EDITOR
using System;
using System.Reflection;
using UnityEditor;
#endif

public class MetaHandInputModeUi : MonoBehaviour
{
    public Text stateText;
    public Button handsOnlyButton;
    public Button controllersAndHandsButton;
    public Button controllersOnlyButton;
    public string handsOnlyLabel = "Hands Only";
    public string controllersAndHandsLabel = "Hands + Controllers";
    public string controllersOnlyLabel = "Controllers Only";

    private const int ControllersOnly = 0;
    private const int ControllersAndHands = 1;
    private const int HandsOnly = 2;

    private void Awake()
    {
        if (handsOnlyButton != null)
        {
            handsOnlyButton.onClick.AddListener(SetHandsOnly);
        }

        if (controllersAndHandsButton != null)
        {
            controllersAndHandsButton.onClick.AddListener(SetControllersAndHands);
        }

        if (controllersOnlyButton != null)
        {
            controllersOnlyButton.onClick.AddListener(SetControllersOnly);
        }
    }

    public void SetHandsOnly()
    {
        SetMode(HandsOnly, handsOnlyLabel);
    }

    public void SetControllersAndHands()
    {
        SetMode(ControllersAndHands, controllersAndHandsLabel);
    }

    public void SetControllersOnly()
    {
        SetMode(ControllersOnly, controllersOnlyLabel);
    }

    private void SetMode(int mode, string label)
    {
#if UNITY_EDITOR
        SetEditorProjectConfig(mode);
        SetState(label);
#else
        SetState(label + " (set before build)");
        Debug.LogWarning("Hand Tracking Support is a Meta project/manifest setting. Set it before build.");
#endif
    }

    private void SetState(string label)
    {
        if (stateText != null)
        {
            stateText.text = "Input: " + label;
        }
    }

#if UNITY_EDITOR
    private void SetEditorProjectConfig(int mode)
    {
        Type projectConfigType = FindType("OVRProjectConfig");
        if (projectConfigType == null)
        {
            Debug.LogWarning("OVRProjectConfig was not found. Open Meta XR SDK settings and set Hand Tracking Support manually.");
            return;
        }

        PropertyInfo cachedConfigProperty = projectConfigType.GetProperty("CachedProjectConfig", BindingFlags.Public | BindingFlags.Static);
        object projectConfig = cachedConfigProperty?.GetValue(null);
        if (projectConfig == null)
        {
            Debug.LogWarning("Could not load OVRProjectConfig.");
            return;
        }

        FieldInfo supportField = projectConfigType.GetField("handTrackingSupport", BindingFlags.Public | BindingFlags.Instance);
        Type supportType = supportField?.FieldType;
        if (supportField == null || supportType == null)
        {
            Debug.LogWarning("Could not find handTrackingSupport on OVRProjectConfig.");
            return;
        }

        UnityEngine.Object projectConfigObject = projectConfig as UnityEngine.Object;
        if (projectConfigObject != null)
        {
            Undo.RecordObject(projectConfigObject, "Set Meta Hand Tracking Support");
        }

        object enumValue = Enum.ToObject(supportType, mode);
        supportField.SetValue(projectConfig, enumValue);

        MethodInfo commitMethod = projectConfigType.GetMethod("CommitProjectConfig", BindingFlags.Public | BindingFlags.Static);
        commitMethod?.Invoke(null, new[] { projectConfig });
        AssetDatabase.SaveAssets();
        Debug.Log("Meta Hand Tracking Support set to " + enumValue);
    }

    private static Type FindType(string typeName)
    {
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type type = assembly.GetType(typeName);
            if (type != null)
            {
                return type;
            }
        }

        return null;
    }
#endif
}
