using UnityEngine;

[DisallowMultipleComponent]
[DefaultExecutionOrder(10000)]
public class HandJointUiGrabHeaderProxy : MonoBehaviour
{
    public Transform moveRoot;
    public bool proxyPositionToRoot = true;
    public bool resetHeaderRotation = true;
    public bool resetHeaderScale = true;

    [SerializeField] private Vector3 restLocalPosition;
    [SerializeField] private Quaternion restLocalRotation = Quaternion.identity;
    [SerializeField] private Vector3 restLocalScale = Vector3.one;
    [SerializeField] private bool hasRestPose;

    private Rigidbody headerRigidbody;
    private Rigidbody rootRigidbody;

    private void Awake()
    {
        CacheRigidbodies();

        if (!hasRestPose)
        {
            CaptureCurrentLocalPose();
        }
    }

    private void OnEnable()
    {
        CacheRigidbodies();

        if (!hasRestPose)
        {
            CaptureCurrentLocalPose();
        }
    }

    private void LateUpdate()
    {
        if (!hasRestPose)
        {
            CaptureCurrentLocalPose();
        }

        Transform root = moveRoot != null ? moveRoot : transform.parent;
        if (root == null || transform.parent == null)
        {
            return;
        }

        if (proxyPositionToRoot)
        {
            Vector3 expectedWorldPosition = transform.parent.TransformPoint(restLocalPosition);
            Vector3 worldDelta = transform.position - expectedWorldPosition;

            if (worldDelta.sqrMagnitude > 0.0000001f)
            {
                MoveRoot(root, worldDelta);
            }
        }

        ResetHeaderLocalPose();
    }

    public void CaptureCurrentLocalPose()
    {
        restLocalPosition = transform.localPosition;
        restLocalRotation = transform.localRotation;
        restLocalScale = transform.localScale;
        hasRestPose = true;
    }

    private void CacheRigidbodies()
    {
        headerRigidbody = GetComponent<Rigidbody>();
        rootRigidbody = moveRoot != null ? moveRoot.GetComponent<Rigidbody>() : null;
    }

    private void MoveRoot(Transform root, Vector3 worldDelta)
    {
        if (rootRigidbody == null || rootRigidbody.transform != root)
        {
            rootRigidbody = root.GetComponent<Rigidbody>();
        }

        if (rootRigidbody != null && !rootRigidbody.isKinematic)
        {
            rootRigidbody.MovePosition(rootRigidbody.position + worldDelta);
            return;
        }

        root.position += worldDelta;
    }

    private void ResetHeaderLocalPose()
    {
        transform.localPosition = restLocalPosition;

        if (resetHeaderRotation)
        {
            transform.localRotation = restLocalRotation;
        }

        if (resetHeaderScale)
        {
            transform.localScale = restLocalScale;
        }

        if (headerRigidbody != null)
        {
            headerRigidbody.position = transform.position;
            headerRigidbody.rotation = transform.rotation;
        }
    }
}
