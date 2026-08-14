using System.Collections.Generic;
using Meta.XR.MRUtilityKit;
using UnityEngine;
using UnityEngine.Serialization;

public class PocketControl : MonoBehaviour
{
    private enum AnchorControlState
    {
        Idle,
        Priming,
        Controlling,
        Pulling
    }

    private enum ControlAxis
    {
        None,
        Horizontal,
        Depth
    }

    [Header("Hand Tracking")]
    public OVRSkeleton skeleton;
    [Tooltip("Huong di chuyen tinh theo camera. De trong se tu dung Camera.main.")]
    public Transform movementReference;

    [Header("Controlled Object")]
    public GameObject controlledObject;

    [Header("Anchor Movement")]
    [Min(0f)] public float moveSpeed = 3f;
    [Min(0.001f)] public float deadZoneRadius = 0.05f;
    [Min(0.001f)] public float fullSpeedRadius = 0.18f;
    [Min(0f)] public float movementSmoothTime = 0.1f;
    public bool dominantAxisOnly = true;
    [Range(1f, 2f)] public float axisSwitchRatio = 1.25f;

    [Header("Movement Bounds")]
    [Min(0.1f)] public float boundsSize = 20f;
    public bool centerBoundsOnStart = true;
    public Vector3 boundsCenter;

    [Header("MRUK Floor")]
    public bool useScannedRoomFloor = true;
    public bool keepInsideScannedRoom = true;
    public bool avoidScannedSceneVolumes = true;
    [Min(0f)] public float floorClearance = 0.01f;
    [Min(0.1f)] public float floorRayStartHeight = 2f;

    [Header("Fist Pull")]
    [FormerlySerializedAs("returnSpeed")]
    [Min(0f)] public float pullSpeed = 5f;
    [FormerlySerializedAs("returnStopDistance")]
    [Min(0.1f)] public float closestDistanceToPlayer = 1f;
    [FormerlySerializedAs("resetHoldSeconds")]
    [Min(0f)] public float fistPullHoldSeconds = 0.1f;

    [Header("Obstacle Avoidance")]
    public bool avoidObstaclesWhilePulling = true;
    [Min(0.05f)] public float avoidanceLookAhead = 0.4f;
    [Range(15f, 120f)] public float maximumAvoidanceAngle = 90f;
    [Range(1, 12)] public int avoidanceDirectionSamples = 6;

    [Header("Gesture Recognition")]
    [Range(0.5f, 1f)] public float extendedRatio = 0.82f;
    [Range(0.2f, 0.9f)] public float curledRatio = 0.68f;
    [Min(0f)] public float trackingWarmupSeconds = 0.75f;
    [Min(0f)] public float activationHoldSeconds = 0.15f;
    [Min(0f)] public float releaseGraceSeconds = 0.15f;

    [Header("Anchor Visual")]
    public bool showAnchorVisual = true;
    [Range(12, 96)] public int ringSegments = 48;
    [Min(0.001f)] public float visualLineWidth = 0.006f;
    public Color primingColor = new Color(0.65f, 0.68f, 0.72f, 0.85f);
    public Color activeColor = new Color(0.1f, 1f, 0.45f, 0.95f);

    [Header("Runtime Debug")]
    [SerializeField] private AnchorControlState controlState;
    [SerializeField] private bool isControlling;
    [SerializeField] private bool isPullingTowardPlayer;
    [SerializeField] private bool isOpenHand;
    [SerializeField] private bool isFist;
    [SerializeField] private bool roomFloorReady;
    [SerializeField] private Vector3 anchorPositionWorld;
    [SerializeField] private Vector2 controlOffset;
    [SerializeField, Range(0f, 1f)] private float movementAmount;
    [SerializeField, Range(0f, 1f)] private float indexStraightness;
    [SerializeField, Range(0f, 1f)] private float middleStraightness;
    [SerializeField, Range(0f, 1f)] private float ringStraightness;
    [SerializeField, Range(0f, 1f)] private float littleStraightness;

    private readonly Dictionary<OVRSkeleton.BoneId, Transform> boneCache =
        new Dictionary<OVRSkeleton.BoneId, Transform>();

    private readonly FingerBoneIds indexIds = new FingerBoneIds();
    private readonly FingerBoneIds middleIds = new FingerBoneIds();
    private readonly FingerBoneIds ringIds = new FingerBoneIds();
    private readonly FingerBoneIds littleIds = new FingerBoneIds();

    private OVRSkeleton.BoneId wristId;
    private OVRSkeleton.SkeletonType lastResolvedType = OVRSkeleton.SkeletonType.None;
    private bool boneIdsResolved;
    private float controlledHeight;
    private float objectBottomOffset;
    private float objectCollisionRadius;
    private Vector3 initialPosition;
    private float trackingValidSince = -1f;
    private float openPoseSince = -1f;
    private float fistPoseSince = -1f;
    private float lastOpenPoseTime = -1f;
    private Vector3 smoothedPlanarVelocity;
    private Vector3 velocitySmoothRef;
    private Vector3 anchorForwardWorld;
    private Vector3 anchorRightWorld;
    private MRUKRoom currentRoom;
    private bool roomFloorInitialized;
    private int avoidanceSide;
    private ControlAxis activeAxis;
    private GameObject anchorVisualRoot;
    private LineRenderer anchorRing;
    private LineRenderer directionLine;
    private Material visualMaterial;

    private sealed class FingerBoneIds
    {
        public OVRSkeleton.BoneId Proximal;
        public OVRSkeleton.BoneId Intermediate;
        public OVRSkeleton.BoneId Distal;
        public OVRSkeleton.BoneId Tip;
    }

    private void Start()
    {
        ResolveBoneIds();

        if (movementReference == null && Camera.main != null)
        {
            movementReference = Camera.main.transform;
        }

        if (controlledObject != null)
        {
            CacheControlledObjectDimensions();
            controlledHeight = controlledObject.transform.position.y;
            initialPosition = controlledObject.transform.position;
            if (centerBoundsOnStart)
            {
                boundsCenter = initialPosition;
            }
        }

        CreateAnchorVisual();
        SetAnchorVisualVisible(false);
    }

    private void LateUpdate()
    {
        RefreshScannedRoomFloor();

        if (!HasValidTracking())
        {
            StopControlForTrackingLoss();
            return;
        }

        if (trackingValidSince < 0f)
        {
            trackingValidSince = Time.unscaledTime;
        }

        if (Time.unscaledTime - trackingValidSince < trackingWarmupSeconds)
        {
            return;
        }

        if (!boneIdsResolved || skeleton.GetSkeletonType() != lastResolvedType)
        {
            ResolveBoneIds();
        }

        if (!boneIdsResolved || !TryRefreshBoneCache()
            || !TryReadHandState(out Vector3 palmPosition))
        {
            StopControlForTrackingLoss();
            return;
        }

        // Nam tay va giu trong mot khoang ngan de keo vat the lai gan nguoi choi.
        if (isFist)
        {
            HandleFistPull();
            return;
        }

        fistPoseSince = -1f;
        isPullingTowardPlayer = false;
        avoidanceSide = 0;

        if (!isControlling)
        {
            HandleControlActivation(palmPosition);
            return;
        }

        if (isOpenHand)
        {
            lastOpenPoseTime = Time.unscaledTime;
            MoveFromAnchor(palmPosition);
            UpdateAnchorVisual(anchorPositionWorld, palmPosition, activeColor, true);
            return;
        }

        if (isFist || Time.unscaledTime - lastOpenPoseTime > releaseGraceSeconds)
        {
            ReleaseControl();
        }
    }

    private bool HasValidTracking()
    {
        return skeleton != null
            && controlledObject != null
            && skeleton.IsDataValid;
    }

    // Doc mot lan cac thong so pose can cho activation, release va reset.
    private bool TryReadHandState(out Vector3 palmPosition)
    {
        palmPosition = Vector3.zero;
        if (!TryGetFingerStraightness(indexIds, out indexStraightness)
            || !TryGetFingerStraightness(middleIds, out middleStraightness)
            || !TryGetFingerStraightness(ringIds, out ringStraightness)
            || !TryGetFingerStraightness(littleIds, out littleStraightness)
            || !TryGetPalmCenter(out palmPosition))
        {
            return false;
        }

        isOpenHand = indexStraightness >= extendedRatio
            && middleStraightness >= extendedRatio
            && ringStraightness >= extendedRatio
            && littleStraightness >= extendedRatio;

        isFist = indexStraightness <= curledRatio
            && middleStraightness <= curledRatio
            && ringStraightness <= curledRatio
            && littleStraightness <= curledRatio;

        return true;
    }

    // Mo tay on dinh trong mot khoang ngan se dat anchor moi ngay tai long ban tay.
    private void HandleControlActivation(Vector3 palmPosition)
    {
        if (!isOpenHand)
        {
            openPoseSince = -1f;
            controlState = AnchorControlState.Idle;
            SetAnchorVisualVisible(false);
            return;
        }

        controlState = AnchorControlState.Priming;
        UpdateAnchorVisual(palmPosition, palmPosition, primingColor, false);

        if (openPoseSince < 0f)
        {
            openPoseSince = Time.unscaledTime;
        }

        if (Time.unscaledTime - openPoseSince < activationHoldSeconds)
        {
            return;
        }

        if (!TryGetMovementBasis(out anchorForwardWorld, out anchorRightWorld))
        {
            return;
        }

        anchorPositionWorld = palmPosition;
        isControlling = true;
        controlState = AnchorControlState.Controlling;
        lastOpenPoseTime = Time.unscaledTime;
        activeAxis = ControlAxis.None;
        smoothedPlanarVelocity = Vector3.zero;
        velocitySmoothRef = Vector3.zero;
        UpdateAnchorVisual(anchorPositionWorld, palmPosition, activeColor, true);
    }

    // Doi offset cua tay trong he truc camera thanh van toc cua vat the.
    private void MoveFromAnchor(Vector3 palmPosition)
    {
        Vector3 planarHandPosition = new Vector3(
            palmPosition.x,
            anchorPositionWorld.y,
            palmPosition.z);
        Vector3 delta = planarHandPosition - anchorPositionWorld;
        float horizontal = Vector3.Dot(delta, anchorRightWorld);
        float depth = Vector3.Dot(delta, anchorForwardWorld);

        SelectDominantAxis(ref horizontal, ref depth);
        controlOffset = new Vector2(horizontal, depth);

        float offsetMagnitude = new Vector2(horizontal, depth).magnitude;
        movementAmount = Mathf.SmoothStep(
            0f,
            1f,
            Mathf.InverseLerp(deadZoneRadius, fullSpeedRadius, offsetMagnitude));

        Vector3 targetVelocity = Vector3.zero;
        if (movementAmount > 0f && offsetMagnitude > Mathf.Epsilon)
        {
            Vector3 direction = (anchorRightWorld * horizontal
                + anchorForwardWorld * depth).normalized;
            targetVelocity = direction * moveSpeed * movementAmount;
        }

        smoothedPlanarVelocity = Vector3.SmoothDamp(
            smoothedPlanarVelocity,
            targetVelocity,
            ref velocitySmoothRef,
            movementSmoothTime);

        Vector3 nextPosition = controlledObject.transform.position
            + smoothedPlanarVelocity * Time.deltaTime;
        ApplyBoundsAndHeight(ref nextPosition);
        controlledObject.transform.position = nextPosition;
    }

    // Giu truc dang dung; chi doi truc khi truc moi manh hon theo axisSwitchRatio.
    private void SelectDominantAxis(ref float horizontal, ref float depth)
    {
        if (!dominantAxisOnly)
        {
            return;
        }

        float horizontalAbs = Mathf.Abs(horizontal);
        float depthAbs = Mathf.Abs(depth);

        if (activeAxis == ControlAxis.Horizontal
            && depthAbs > horizontalAbs * axisSwitchRatio)
        {
            activeAxis = ControlAxis.Depth;
        }
        else if (activeAxis == ControlAxis.Depth
            && horizontalAbs > depthAbs * axisSwitchRatio)
        {
            activeAxis = ControlAxis.Horizontal;
        }
        else if (activeAxis == ControlAxis.None)
        {
            activeAxis = horizontalAbs >= depthAbs
                ? ControlAxis.Horizontal
                : ControlAxis.Depth;
        }

        if (activeAxis == ControlAxis.Horizontal)
        {
            depth = 0f;
        }
        else
        {
            horizontal = 0f;
        }
    }

    private bool TryGetMovementBasis(out Vector3 forward, out Vector3 right)
    {
        if (movementReference == null && Camera.main != null)
        {
            movementReference = Camera.main.transform;
        }

        if (movementReference == null)
        {
            forward = Vector3.zero;
            right = Vector3.zero;
            return false;
        }

        forward = Vector3.ProjectOnPlane(movementReference.forward, Vector3.up).normalized;
        right = Vector3.ProjectOnPlane(movementReference.right, Vector3.up).normalized;
        return forward.sqrMagnitude >= 0.001f && right.sqrMagnitude >= 0.001f;
    }

    private void ApplyBoundsAndHeight(ref Vector3 position)
    {
        float halfSize = boundsSize * 0.5f;
        position.x = Mathf.Clamp(position.x, boundsCenter.x - halfSize, boundsCenter.x + halfSize);
        position.z = Mathf.Clamp(position.z, boundsCenter.z - halfSize, boundsCenter.z + halfSize);

        if (useScannedRoomFloor && roomFloorReady)
        {
            Vector3 requestedPosition = position;
            if (TryProjectPositionToFloor(requestedPosition, out Vector3 groundedPosition)
                && IsValidRoomPosition(groundedPosition))
            {
                position = groundedPosition;
                controlledHeight = groundedPosition.y;
                return;
            }

            position = controlledObject.transform.position;
            return;
        }

        position.y = controlledHeight;
    }

    // Lay room hien tai va dat vi tri khoi dau cua vat the len mat san da quet.
    private void RefreshScannedRoomFloor()
    {
        if (!useScannedRoomFloor || MRUK.Instance == null || !MRUK.Instance.IsInitialized)
        {
            roomFloorReady = false;
            return;
        }

        // Use the loaded room list instead of the native current-room query.
        // The native query can be unavailable for one frame while XR is shutting down.
        MRUKRoom detectedRoom = MRUK.Instance.Rooms.Count > 0
            ? MRUK.Instance.Rooms[0]
            : null;
        if (detectedRoom != currentRoom)
        {
            currentRoom = detectedRoom;
            roomFloorInitialized = false;
        }

        roomFloorReady = currentRoom != null && currentRoom.FloorAnchors.Count > 0;
        if (!roomFloorReady || roomFloorInitialized || controlledObject == null)
        {
            return;
        }

        if (!TryProjectPositionToFloor(controlledObject.transform.position, out Vector3 groundedPosition))
        {
            return;
        }

        controlledObject.transform.position = groundedPosition;
        controlledHeight = groundedPosition.y;
        initialPosition = groundedPosition;
        if (centerBoundsOnStart)
        {
            boundsCenter = groundedPosition;
        }

        roomFloorInitialized = true;
    }

    // Raycast chi vao nhan FLOOR de day vat the luon nam tren san cua room.
    private bool TryProjectPositionToFloor(Vector3 position, out Vector3 groundedPosition)
    {
        groundedPosition = position;
        if (currentRoom == null)
        {
            return false;
        }

        Bounds roomBounds = currentRoom.GetRoomBounds();
        float rayOriginY = Mathf.Max(
            position.y + floorRayStartHeight,
            roomBounds.max.y + 0.5f);
        float rayDistance = rayOriginY - roomBounds.min.y + 1f;
        Ray floorRay = new Ray(
            new Vector3(position.x, rayOriginY, position.z),
            Vector3.down);
        LabelFilter floorFilter =
            new LabelFilter(MRUKAnchor.SceneLabels.FLOOR);

        if (!currentRoom.Raycast(
                floorRay,
                rayDistance,
                floorFilter,
                out RaycastHit hit,
                out _))
        {
            return false;
        }

        groundedPosition.y = hit.point.y + objectBottomOffset + floorClearance;
        return true;
    }

    private bool IsValidRoomPosition(Vector3 position)
    {
        if (currentRoom == null)
        {
            return false;
        }

        if (keepInsideScannedRoom && !currentRoom.IsPositionInRoom(position))
        {
            return false;
        }

        return !avoidScannedSceneVolumes
            || !currentRoom.IsPositionInSceneVolume(position, objectCollisionRadius);
    }

    // Tinh khoang cach tu pivot den day va ban kinh vat the tu collider/renderer.
    private void CacheControlledObjectDimensions()
    {
        Bounds bounds = new Bounds(
            controlledObject.transform.position,
            Vector3.zero);
        bool hasBounds = false;

        foreach (Collider childCollider in controlledObject.GetComponentsInChildren<Collider>())
        {
            if (!childCollider.enabled)
            {
                continue;
            }

            if (!hasBounds)
            {
                bounds = childCollider.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(childCollider.bounds);
            }
        }

        if (!hasBounds)
        {
            foreach (Renderer childRenderer in controlledObject.GetComponentsInChildren<Renderer>())
            {
                if (!childRenderer.enabled)
                {
                    continue;
                }

                if (!hasBounds)
                {
                    bounds = childRenderer.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(childRenderer.bounds);
                }
            }
        }

        if (!hasBounds)
        {
            return;
        }

        objectBottomOffset = Mathf.Max(
            0f,
            controlledObject.transform.position.y - bounds.min.y);
        objectCollisionRadius = Mathf.Max(bounds.extents.x, bounds.extents.z);
    }

    // Khi nam tay, keo vat the ve phia camera nhung dung truoc nguoi choi mot khoang an toan.
    private void HandleFistPull()
    {
        openPoseSince = -1f;
        SetAnchorVisualVisible(false);

        if (fistPoseSince < 0f)
        {
            fistPoseSince = Time.unscaledTime;
            return;
        }

        if (Time.unscaledTime - fistPoseSince < fistPullHoldSeconds)
        {
            return;
        }

        ReleaseControl();
        isPullingTowardPlayer = true;
        controlState = AnchorControlState.Pulling;
        PullControlledObjectTowardPlayer();
    }

    private void PullControlledObjectTowardPlayer()
    {
        if (controlledObject == null || !TryGetPlayerPlanarPosition(out Vector3 playerPosition))
        {
            isPullingTowardPlayer = false;
            controlState = AnchorControlState.Idle;
            return;
        }

        Transform target = controlledObject.transform;
        Vector3 toPlayer = playerPosition - target.position;
        toPlayer.y = 0f;
        float distanceToPlayer = toPlayer.magnitude;
        if (distanceToPlayer <= closestDistanceToPlayer)
        {
            return;
        }

        float moveDistance = Mathf.Min(
            pullSpeed * Time.deltaTime,
            distanceToPlayer - closestDistanceToPlayer);
        if (TryFindPullPosition(
                target.position,
                toPlayer.normalized,
                moveDistance,
                out Vector3 nextPosition))
        {
            target.position = nextPosition;
        }
    }

    // Thu duong thang truoc; neu bi chan thi tim dan cac huong lech trai va phai.
    private bool TryFindPullPosition(
        Vector3 origin,
        Vector3 desiredDirection,
        float moveDistance,
        out Vector3 nextPosition)
    {
        if (!avoidObstaclesWhilePulling || !roomFloorReady)
        {
            avoidanceSide = 0;
            return TryCreateValidPullPosition(
                origin,
                desiredDirection,
                moveDistance,
                out nextPosition);
        }

        float probeDistance = Mathf.Max(moveDistance, avoidanceLookAhead);
        if (TryCreateValidPullPosition(
                origin,
                desiredDirection,
                probeDistance,
                out _))
        {
            avoidanceSide = 0;
            return TryCreateValidPullPosition(
                origin,
                desiredDirection,
                moveDistance,
                out nextPosition);
        }

        int preferredSide = avoidanceSide == 0 ? 1 : avoidanceSide;
        for (int sample = 1; sample <= avoidanceDirectionSamples; sample++)
        {
            float angle = maximumAvoidanceAngle * sample / avoidanceDirectionSamples;
            if (TryAvoidanceSide(preferredSide, angle, out nextPosition)
                || TryAvoidanceSide(-preferredSide, angle, out nextPosition))
            {
                return true;
            }
        }

        nextPosition = origin;
        return false;

        bool TryAvoidanceSide(int side, float angle, out Vector3 candidate)
        {
            Vector3 direction = Quaternion.AngleAxis(side * angle, Vector3.up)
                * desiredDirection;
            if (!TryCreateValidPullPosition(
                    origin,
                    direction,
                    probeDistance,
                    out _))
            {
                candidate = origin;
                return false;
            }

            avoidanceSide = side;
            return TryCreateValidPullPosition(
                origin,
                direction,
                moveDistance,
                out candidate);
        }
    }

    private bool TryCreateValidPullPosition(
        Vector3 origin,
        Vector3 direction,
        float distance,
        out Vector3 position)
    {
        position = origin + direction * distance;
        float halfSize = boundsSize * 0.5f;
        position.x = Mathf.Clamp(position.x, boundsCenter.x - halfSize, boundsCenter.x + halfSize);
        position.z = Mathf.Clamp(position.z, boundsCenter.z - halfSize, boundsCenter.z + halfSize);

        if (!useScannedRoomFloor || !roomFloorReady)
        {
            position.y = controlledHeight;
            return true;
        }

        return TryProjectPositionToFloor(position, out position)
            && IsValidRoomPosition(position);
    }

    private bool TryGetPlayerPlanarPosition(out Vector3 playerPosition)
    {
        if (movementReference == null && Camera.main != null)
        {
            movementReference = Camera.main.transform;
        }

        if (movementReference == null)
        {
            playerPosition = Vector3.zero;
            return false;
        }

        playerPosition = movementReference.position;
        playerPosition.y = controlledObject.transform.position.y;
        return true;
    }

    private void ReleaseControl()
    {
        isControlling = false;
        openPoseSince = -1f;
        activeAxis = ControlAxis.None;
        controlOffset = Vector2.zero;
        movementAmount = 0f;
        smoothedPlanarVelocity = Vector3.zero;
        velocitySmoothRef = Vector3.zero;
        controlState = AnchorControlState.Idle;
        SetAnchorVisualVisible(false);
    }

    private void StopControlForTrackingLoss()
    {
        trackingValidSince = -1f;
        fistPoseSince = -1f;
        isPullingTowardPlayer = false;
        avoidanceSide = 0;
        isOpenHand = false;
        isFist = false;
        ReleaseControl();
    }

    // Do do thang: khoang cach dau-cuoi chia tong chieu dai cac dot.
    private bool TryGetFingerStraightness(FingerBoneIds ids, out float straightness)
    {
        straightness = 0f;
        if (!boneCache.TryGetValue(ids.Proximal, out Transform proximal)
            || !boneCache.TryGetValue(ids.Intermediate, out Transform intermediate)
            || !boneCache.TryGetValue(ids.Distal, out Transform distal)
            || !boneCache.TryGetValue(ids.Tip, out Transform tip))
        {
            return false;
        }

        float chainLength = Vector3.Distance(proximal.position, intermediate.position)
            + Vector3.Distance(intermediate.position, distal.position)
            + Vector3.Distance(distal.position, tip.position);
        if (chainLength <= Mathf.Epsilon)
        {
            return false;
        }

        straightness = Vector3.Distance(proximal.position, tip.position) / chainLength;
        return true;
    }

    // Tam long ban tay on dinh hon mot joint don le khi tracking co jitter.
    private bool TryGetPalmCenter(out Vector3 palmCenter)
    {
        palmCenter = Vector3.zero;
        if (!boneCache.TryGetValue(wristId, out Transform wrist)
            || !boneCache.TryGetValue(indexIds.Proximal, out Transform indexProximal)
            || !boneCache.TryGetValue(middleIds.Proximal, out Transform middleProximal)
            || !boneCache.TryGetValue(ringIds.Proximal, out Transform ringProximal)
            || !boneCache.TryGetValue(littleIds.Proximal, out Transform littleProximal))
        {
            return false;
        }

        palmCenter = (wrist.position
            + indexProximal.position
            + middleProximal.position
            + ringProximal.position
            + littleProximal.position) * 0.2f;
        return true;
    }

    private void CreateAnchorVisual()
    {
        if (!showAnchorVisual || anchorVisualRoot != null)
        {
            return;
        }

        anchorVisualRoot = new GameObject("Pocket Control Anchor Visual");
        anchorVisualRoot.hideFlags = HideFlags.DontSave;
        anchorVisualRoot.transform.SetParent(transform, false);

        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
        {
            shader = Shader.Find("Unlit/Color");
        }

        if (shader != null)
        {
            visualMaterial = new Material(shader)
            {
                name = "Pocket Control Anchor Runtime Material",
                hideFlags = HideFlags.DontSave
            };
        }

        anchorRing = CreateVisualLine("Dead Zone Ring", true);
        directionLine = CreateVisualLine("Control Direction", false);
    }

    private LineRenderer CreateVisualLine(string objectName, bool loop)
    {
        GameObject lineObject = new GameObject(objectName);
        lineObject.hideFlags = HideFlags.DontSave;
        lineObject.transform.SetParent(anchorVisualRoot.transform, false);

        LineRenderer line = lineObject.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.loop = loop;
        line.widthMultiplier = visualLineWidth;
        line.numCapVertices = 4;
        line.numCornerVertices = 2;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;

        if (visualMaterial != null)
        {
            line.sharedMaterial = visualMaterial;
        }

        return line;
    }

    private void UpdateAnchorVisual(
        Vector3 center,
        Vector3 handPosition,
        Color color,
        bool showDirection)
    {
        if (!showAnchorVisual)
        {
            SetAnchorVisualVisible(false);
            return;
        }

        if (anchorVisualRoot == null)
        {
            CreateAnchorVisual();
        }

        if (anchorVisualRoot == null || anchorRing == null || directionLine == null)
        {
            return;
        }

        anchorVisualRoot.SetActive(true);
        anchorRing.widthMultiplier = visualLineWidth;
        anchorRing.startColor = color;
        anchorRing.endColor = color;
        anchorRing.positionCount = ringSegments;

        for (int i = 0; i < ringSegments; i++)
        {
            float angle = i * Mathf.PI * 2f / ringSegments;
            Vector3 point = center + new Vector3(
                Mathf.Cos(angle) * deadZoneRadius,
                0f,
                Mathf.Sin(angle) * deadZoneRadius);
            anchorRing.SetPosition(i, point);
        }

        directionLine.gameObject.SetActive(showDirection);
        if (!showDirection)
        {
            return;
        }

        Vector3 projectedHand = new Vector3(handPosition.x, center.y, handPosition.z);
        directionLine.widthMultiplier = visualLineWidth;
        directionLine.startColor = color;
        directionLine.endColor = color;
        directionLine.positionCount = 2;
        directionLine.SetPosition(0, center);
        directionLine.SetPosition(1, projectedHand);
    }

    private void SetAnchorVisualVisible(bool visible)
    {
        if (anchorVisualRoot != null)
        {
            anchorVisualRoot.SetActive(visible && showAnchorVisual);
        }
    }

    private void OnDestroy()
    {
        if (anchorVisualRoot != null)
        {
            Destroy(anchorVisualRoot);
        }

        if (visualMaterial != null)
        {
            Destroy(visualMaterial);
        }
    }

    private bool TryRefreshBoneCache()
    {
        if (skeleton.Bones == null || skeleton.Bones.Count == 0)
        {
            return false;
        }

        boneCache.Clear();
        for (int i = 0; i < skeleton.Bones.Count; i++)
        {
            OVRBone bone = skeleton.Bones[i];
            boneCache[bone.Id] = bone.Transform;
        }

        return true;
    }

    private void ResolveBoneIds()
    {
        if (skeleton == null)
        {
            boneIdsResolved = false;
            return;
        }

        OVRSkeleton.SkeletonType type = skeleton.GetSkeletonType();
        if (type == OVRSkeleton.SkeletonType.None)
        {
            boneIdsResolved = false;
            return;
        }

        bool isOpenXRHand = type == OVRSkeleton.SkeletonType.XRHandLeft
            || type == OVRSkeleton.SkeletonType.XRHandRight;

        if (isOpenXRHand)
        {
            wristId = OVRSkeleton.BoneId.XRHand_Wrist;
            SetFingerIds(indexIds,
                OVRSkeleton.BoneId.XRHand_IndexProximal,
                OVRSkeleton.BoneId.XRHand_IndexIntermediate,
                OVRSkeleton.BoneId.XRHand_IndexDistal,
                OVRSkeleton.BoneId.XRHand_IndexTip);
            SetFingerIds(middleIds,
                OVRSkeleton.BoneId.XRHand_MiddleProximal,
                OVRSkeleton.BoneId.XRHand_MiddleIntermediate,
                OVRSkeleton.BoneId.XRHand_MiddleDistal,
                OVRSkeleton.BoneId.XRHand_MiddleTip);
            SetFingerIds(ringIds,
                OVRSkeleton.BoneId.XRHand_RingProximal,
                OVRSkeleton.BoneId.XRHand_RingIntermediate,
                OVRSkeleton.BoneId.XRHand_RingDistal,
                OVRSkeleton.BoneId.XRHand_RingTip);
            SetFingerIds(littleIds,
                OVRSkeleton.BoneId.XRHand_LittleProximal,
                OVRSkeleton.BoneId.XRHand_LittleIntermediate,
                OVRSkeleton.BoneId.XRHand_LittleDistal,
                OVRSkeleton.BoneId.XRHand_LittleTip);
        }
        else
        {
            wristId = OVRSkeleton.BoneId.Hand_WristRoot;
            SetFingerIds(indexIds,
                OVRSkeleton.BoneId.Hand_Index1,
                OVRSkeleton.BoneId.Hand_Index2,
                OVRSkeleton.BoneId.Hand_Index3,
                OVRSkeleton.BoneId.Hand_IndexTip);
            SetFingerIds(middleIds,
                OVRSkeleton.BoneId.Hand_Middle1,
                OVRSkeleton.BoneId.Hand_Middle2,
                OVRSkeleton.BoneId.Hand_Middle3,
                OVRSkeleton.BoneId.Hand_MiddleTip);
            SetFingerIds(ringIds,
                OVRSkeleton.BoneId.Hand_Ring1,
                OVRSkeleton.BoneId.Hand_Ring2,
                OVRSkeleton.BoneId.Hand_Ring3,
                OVRSkeleton.BoneId.Hand_RingTip);
            SetFingerIds(littleIds,
                OVRSkeleton.BoneId.Hand_Pinky1,
                OVRSkeleton.BoneId.Hand_Pinky2,
                OVRSkeleton.BoneId.Hand_Pinky3,
                OVRSkeleton.BoneId.Hand_PinkyTip);
        }

        lastResolvedType = type;
        boneIdsResolved = true;
    }

    private static void SetFingerIds(
        FingerBoneIds target,
        OVRSkeleton.BoneId proximal,
        OVRSkeleton.BoneId intermediate,
        OVRSkeleton.BoneId distal,
        OVRSkeleton.BoneId tip)
    {
        target.Proximal = proximal;
        target.Intermediate = intermediate;
        target.Distal = distal;
        target.Tip = tip;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        deadZoneRadius = Mathf.Max(0.001f, deadZoneRadius);
        fullSpeedRadius = Mathf.Max(deadZoneRadius + 0.001f, fullSpeedRadius);
        movementSmoothTime = Mathf.Max(0f, movementSmoothTime);
        boundsSize = Mathf.Max(0.1f, boundsSize);
        pullSpeed = Mathf.Max(0f, pullSpeed);
        closestDistanceToPlayer = Mathf.Max(0.1f, closestDistanceToPlayer);
        fistPullHoldSeconds = Mathf.Max(0f, fistPullHoldSeconds);
        avoidanceLookAhead = Mathf.Max(0.05f, avoidanceLookAhead);
        maximumAvoidanceAngle = Mathf.Clamp(maximumAvoidanceAngle, 15f, 120f);
        avoidanceDirectionSamples = Mathf.Clamp(avoidanceDirectionSamples, 1, 12);
        floorClearance = Mathf.Max(0f, floorClearance);
        floorRayStartHeight = Mathf.Max(0.1f, floorRayStartHeight);
        extendedRatio = Mathf.Clamp(extendedRatio, 0.5f, 1f);
        curledRatio = Mathf.Clamp(curledRatio, 0.2f, 0.9f);
        axisSwitchRatio = Mathf.Clamp(axisSwitchRatio, 1f, 2f);
        ringSegments = Mathf.Clamp(ringSegments, 12, 96);
        visualLineWidth = Mathf.Max(0.001f, visualLineWidth);
        ResolveBoneIds();
    }
#endif
}
