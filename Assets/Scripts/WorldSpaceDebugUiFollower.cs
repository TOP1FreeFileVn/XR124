using UnityEngine;

[RequireComponent(typeof(Canvas))]
public class WorldSpaceDebugUiFollower : MonoBehaviour
{
    public Transform target;
    public string fallbackTargetName = "CenterEyeAnchor";
    public float distance = 1.5f;
    public Vector2 viewPlaneOffset = new Vector2(0f, -0.08f);
    public bool followEveryFrame;
    public bool faceTarget = true;
    public bool keepUpright = true;
    public bool forceCameraCullingMask = true;

    private Canvas canvas;
    private Camera targetCamera;

    private void Awake()
    {
        canvas = GetComponent<Canvas>();
        ResolveTarget();
        ApplyCameraSettings();
        Place();
    }

    private void LateUpdate()
    {
        if (target == null)
        {
            ResolveTarget();
            ApplyCameraSettings();
        }

        if (followEveryFrame)
        {
            Place();
        }
    }

    public void Place()
    {
        if (target == null)
        {
            return;
        }

        Vector3 forward = GetPlacementForward();
        Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
        Vector3 up = keepUpright ? Vector3.up : target.up;

        transform.position = target.position + forward * distance + right * viewPlaneOffset.x + up * viewPlaneOffset.y;

        if (faceTarget)
        {
            transform.rotation = Quaternion.LookRotation(forward, up);
        }
    }

    private Vector3 GetPlacementForward()
    {
        if (!keepUpright)
        {
            return target.forward;
        }

        Vector3 forward = Vector3.ProjectOnPlane(target.forward, Vector3.up);
        if (forward.sqrMagnitude < 0.0001f)
        {
            forward = Vector3.ProjectOnPlane(target.parent != null ? target.parent.forward : Vector3.forward, Vector3.up);
        }

        return forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.forward;
    }

    private void ResolveTarget()
    {
        if (target == null && !string.IsNullOrWhiteSpace(fallbackTargetName))
        {
            GameObject targetObject = GameObject.Find(fallbackTargetName);
            if (targetObject != null)
            {
                target = targetObject.transform;
            }
        }

        if (target == null && Camera.main != null)
        {
            target = Camera.main.transform;
        }

        targetCamera = target != null ? target.GetComponent<Camera>() : Camera.main;
    }

    private void ApplyCameraSettings()
    {
        if (canvas == null)
        {
            canvas = GetComponent<Canvas>();
        }

        if (canvas != null && canvas.renderMode == RenderMode.WorldSpace && targetCamera != null)
        {
            canvas.worldCamera = targetCamera;
        }

        if (!forceCameraCullingMask || targetCamera == null)
        {
            return;
        }

        int layerMask = 1 << gameObject.layer;
        if ((targetCamera.cullingMask & layerMask) == 0)
        {
            targetCamera.cullingMask |= layerMask;
        }
    }
}
