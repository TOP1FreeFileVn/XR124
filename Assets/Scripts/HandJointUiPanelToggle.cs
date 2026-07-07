using UnityEngine;
using UnityEngine.UI;

public class HandJointUiPanelToggle : MonoBehaviour
{
    public GameObject panelRoot;
    public GameObject hideButtonRoot;
    public GameObject showButtonRoot;
    public Text buttonLabel;
    public string visibleText = "Hide UI";
    public string hiddenText = "Show UI";
    public bool startVisible = true;

    [Header("Controller Toggle")]
    public bool toggleWithControllerButton = true;
    public OVRInput.Button controllerButton = OVRInput.Button.Two;
    public OVRInput.Controller controller = OVRInput.Controller.LTouch;

    private bool isVisible;

    private void Awake()
    {
        SetVisible(startVisible);
    }

    private void Update()
    {
        if (toggleWithControllerButton && OVRInput.GetDown(controllerButton, controller))
        {
            Toggle();
        }
    }

    public void Toggle()
    {
        SetVisible(!isVisible);
    }

    public void Show()
    {
        SetVisible(true);
    }

    public void Hide()
    {
        SetVisible(false);
    }

    public void SetVisible(bool visible)
    {
        isVisible = visible;

        if (panelRoot != null)
        {
            panelRoot.SetActive(visible);
        }

        if (hideButtonRoot != null)
        {
            hideButtonRoot.SetActive(visible);
        }

        if (showButtonRoot != null)
        {
            showButtonRoot.SetActive(!visible);
        }

        if (buttonLabel != null)
        {
            buttonLabel.text = visible ? visibleText : hiddenText;
        }
    }
}
