using UnityEngine;
using UnityEngine.UI;

public class HandJointDebugToggle : MonoBehaviour
{
    [Header("Debug Targets")]
    public HandJointDebugVisualizer visualizer;
    public GameObject debugCanvasRoot;
    public GameObject markerRoot;
    public Behaviour[] extraBehavioursToToggle;
    public GameObject[] extraObjectsToToggle;

    [Header("Optional Input")]
    public bool toggleWithKey = true;
    public KeyCode toggleKey = KeyCode.F2;

    [Header("Optional UI Feedback")]
    public Text stateText;
    public string onText = "Hand debug: ON";
    public string offText = "Hand debug: OFF";

    [Header("Startup")]
    public bool startEnabled = true;

    private bool isEnabledState;

    private void Awake()
    {
        SetDebugEnabled(startEnabled);
    }

    private void Update()
    {
        if (toggleWithKey && Input.GetKeyDown(toggleKey))
        {
            ToggleDebug();
        }
    }

    public void ToggleDebug()
    {
        SetDebugEnabled(!isEnabledState);
    }

    public void ShowDebug()
    {
        SetDebugEnabled(true);
    }

    public void HideDebug()
    {
        SetDebugEnabled(false);
    }

    public void SetDebugEnabled(bool enabled)
    {
        isEnabledState = enabled;

        if (visualizer != null)
        {
            visualizer.SetVisible(enabled);
            visualizer.enabled = enabled;
        }

        SetGameObjectActive(debugCanvasRoot, enabled);
        SetGameObjectActive(markerRoot, enabled);

        if (extraBehavioursToToggle != null)
        {
            foreach (Behaviour behaviour in extraBehavioursToToggle)
            {
                if (behaviour != null)
                {
                    behaviour.enabled = enabled;
                }
            }
        }

        if (extraObjectsToToggle != null)
        {
            foreach (GameObject target in extraObjectsToToggle)
            {
                SetGameObjectActive(target, enabled);
            }
        }

        if (stateText != null)
        {
            stateText.text = enabled ? onText : offText;
        }
    }

    public bool IsDebugEnabled()
    {
        return isEnabledState;
    }

    private static void SetGameObjectActive(GameObject target, bool active)
    {
        if (target != null && target.activeSelf != active)
        {
            target.SetActive(active);
        }
    }
}
