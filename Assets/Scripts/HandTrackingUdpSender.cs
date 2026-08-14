using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

public class HandTrackingUdpSender_Debuggable : MonoBehaviour
{
    public enum CoordinateSpaceMode
    {
        World,
        LocalToReference,
        HandLocal
    }

    [Header("Hand Tracking Objects")]
    public GameObject leftHandObject;
    public GameObject rightHandObject;

    [Header("UDP Target")]
    public string targetIp = "192.168.1.10";
    public int targetPort = 5055;
    public float sendRate = 30f;

    [Header("Coordinate Space")]
    public CoordinateSpaceMode coordinateSpace = CoordinateSpaceMode.World;
    public Transform coordinateSpaceReference;

    [Header("Debug - Tick These In Immersive Debugger")]
    public bool udpReady;
    public bool leftTracked;
    public bool rightTracked;
    public Vector3 leftWristPosition;
    public Vector3 rightWristPosition;
    public Vector3 leftIndexTipPosition;
    public Vector3 rightIndexTipPosition;
    public float leftIndexPinch;
    public float rightIndexPinch;
    public int packetsSent;
    public string lastStatus = "Not started";
    public string lastPacketPreview = "";

    [Header("Continuous Debug Log")]
    public bool logContinuously = true;
    public float debugLogRate = 1f;
    public bool logPacketPreview = true;

    private OVRHand leftHand;
    private OVRHand rightHand;
    private OVRSkeleton leftSkeleton;
    private OVRSkeleton rightSkeleton;
    private UdpClient udp;
    private IPEndPoint endpoint;
    private float nextSendTime;
    private float nextDebugLogTime;

    [Serializable]
    private class Packet
    {
        public string type = "hand_tracking";
        public string coordinateSpace;
        public string coordinateReference;
        public float time;
        public HandData left;
        public HandData right;
    }

    [Serializable]
    private class HandData
    {
        public bool tracked;
        public Vec3 wristPosition;
        public Quat wristRotation;
        public Vec3 indexTipPosition;
        public float indexPinchStrength;
        public float middlePinchStrength;
        public float ringPinchStrength;
        public float pinkyPinchStrength;
    }

    [Serializable]
    private struct Vec3
    {
        public float x;
        public float y;
        public float z;

        public Vec3(Vector3 value)
        {
            x = value.x;
            y = value.y;
            z = value.z;
        }
    }

    [Serializable]
    private struct Quat
    {
        public float x;
        public float y;
        public float z;
        public float w;

        public Quat(Quaternion value)
        {
            x = value.x;
            y = value.y;
            z = value.z;
            w = value.w;
        }
    }

    private void Awake()
    {
        CacheHandComponents();
    }

    private void OnEnable()
    {
        OpenUdp();
        LogDebugSnapshot("enabled");
    }

    private void OnDisable()
    {
        LogDebugSnapshot("disabled");
        CloseUdp();
    }

    private void Update()
    {
        CacheHandComponents();
        UpdateDebugFields();
        LogDebugSnapshotThrottled();

        if (!udpReady || Time.time < nextSendTime)
        {
            return;
        }

        nextSendTime = Time.time + (1f / Mathf.Max(1f, sendRate));
        SendCurrentPacket();
    }

    public void ReconnectUdp()
    {
        CloseUdp();
        OpenUdp();
        LogDebugSnapshot("reconnect");
    }

    public void SendTestPacket()
    {
        SendCurrentPacket();
        LogDebugSnapshot("test packet");
    }

    private void CacheHandComponents()
    {
        if (leftHandObject != null)
        {
            leftHand = leftHandObject.GetComponent<OVRHand>();
            leftSkeleton = leftHandObject.GetComponent<OVRSkeleton>();
        }

        if (rightHandObject != null)
        {
            rightHand = rightHandObject.GetComponent<OVRHand>();
            rightSkeleton = rightHandObject.GetComponent<OVRSkeleton>();
        }
    }

    private void OpenUdp()
    {
        try
        {
            endpoint = new IPEndPoint(IPAddress.Parse(targetIp), targetPort);
            udp = new UdpClient();
            udpReady = true;
            lastStatus = "UDP ready";
            Debug.Log($"[HandTrackingUdpSender] UDP ready -> {targetIp}:{targetPort}", this);
        }
        catch (Exception e)
        {
            udpReady = false;
            lastStatus = "UDP error: " + e.Message;
            Debug.LogError($"[HandTrackingUdpSender] UDP open failed -> {e.Message}", this);
        }
    }

    private void CloseUdp()
    {
        udpReady = false;
        udp?.Close();
        udp = null;
        lastStatus = "UDP closed";
    }

    private void UpdateDebugFields()
    {
        leftTracked = leftHand != null && leftHand.IsTracked;
        rightTracked = rightHand != null && rightHand.IsTracked;

        Vector3 leftWristWorld = GetWristPosition(leftSkeleton);
        Vector3 rightWristWorld = GetWristPosition(rightSkeleton);
        Vector3 leftIndexTipWorld = GetBonePosition(leftSkeleton, OVRSkeleton.BoneId.Hand_IndexTip, leftWristWorld);
        Vector3 rightIndexTipWorld = GetBonePosition(rightSkeleton, OVRSkeleton.BoneId.Hand_IndexTip, rightWristWorld);

        Quaternion leftWristRotation = GetWristRotation(leftSkeleton);
        Quaternion rightWristRotation = GetWristRotation(rightSkeleton);

        leftWristPosition = ToOutputPosition(leftWristWorld, leftWristWorld, leftWristRotation);
        rightWristPosition = ToOutputPosition(rightWristWorld, rightWristWorld, rightWristRotation);
        leftIndexTipPosition = ToOutputPosition(leftIndexTipWorld, leftWristWorld, leftWristRotation);
        rightIndexTipPosition = ToOutputPosition(rightIndexTipWorld, rightWristWorld, rightWristRotation);

        leftIndexPinch = leftHand != null ? leftHand.GetFingerPinchStrength(OVRHand.HandFinger.Index) : 0f;
        rightIndexPinch = rightHand != null ? rightHand.GetFingerPinchStrength(OVRHand.HandFinger.Index) : 0f;
    }

    private void SendCurrentPacket()
    {
        if (udp == null || endpoint == null)
        {
            udpReady = false;
            lastStatus = "UDP not ready";
            return;
        }

        try
        {
            var packet = new Packet
            {
                coordinateSpace = GetCoordinateSpaceLabel(),
                coordinateReference = GetCoordinateReferenceLabel(),
                time = Time.time,
                left = BuildHandData(leftHand, leftSkeleton),
                right = BuildHandData(rightHand, rightSkeleton)
            };

            string json = JsonUtility.ToJson(packet);
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            udp.Send(bytes, bytes.Length, endpoint);

            packetsSent++;
            lastPacketPreview = json.Length > 220 ? json.Substring(0, 220) + "..." : json;
            lastStatus = "Sent packet " + packetsSent;
        }
        catch (Exception e)
        {
            udpReady = false;
            lastStatus = "Send error: " + e.Message;
            Debug.LogError($"[HandTrackingUdpSender] UDP send failed -> {e.Message}", this);
        }
    }

    private void LogDebugSnapshotThrottled()
    {
        if (!logContinuously || Time.time < nextDebugLogTime)
        {
            return;
        }

        nextDebugLogTime = Time.time + (1f / Mathf.Max(0.1f, debugLogRate));
        LogDebugSnapshot("tick");
    }

    private void LogDebugSnapshot(string reason)
    {
        string leftState = BuildHandDebugText("L", leftHandObject, leftHand, leftSkeleton, leftTracked, leftWristPosition, leftIndexTipPosition, leftIndexPinch);
        string rightState = BuildHandDebugText("R", rightHandObject, rightHand, rightSkeleton, rightTracked, rightWristPosition, rightIndexTipPosition, rightIndexPinch);
        string packetPreview = logPacketPreview && !string.IsNullOrEmpty(lastPacketPreview)
            ? "\npacket=" + lastPacketPreview
            : "";

        Debug.Log(
            $"[HandTrackingUdpSender] {reason} | space={GetCoordinateSpaceLabel()} ref={GetCoordinateReferenceLabel()} udp={udpReady} target={targetIp}:{targetPort} packets={packetsSent} status=\"{lastStatus}\"\n{leftState}\n{rightState}{packetPreview}",
            this);
    }

    private string BuildHandDebugText(
        string label,
        GameObject handObject,
        OVRHand hand,
        OVRSkeleton skeleton,
        bool tracked,
        Vector3 wristPosition,
        Vector3 indexTipPosition,
        float indexPinch)
    {
        string objectName = handObject != null ? handObject.name : "null";
        string handReady = hand != null ? "ok" : "missing";
        string skeletonReady = skeleton != null ? $"ok bones={skeleton.Bones?.Count ?? 0}" : "missing";

        return $"{label} object={objectName} hand={handReady} skeleton={skeletonReady} tracked={tracked} wrist={FormatVector(wristPosition)} indexTip={FormatVector(indexTipPosition)} pinch={indexPinch:0.000}";
    }

    private string FormatVector(Vector3 value)
    {
        return $"({value.x:0.000}, {value.y:0.000}, {value.z:0.000})";
    }

    private string GetCoordinateSpaceLabel()
    {
        if (coordinateSpace == CoordinateSpaceMode.HandLocal)
        {
            return "hand_local";
        }

        if (coordinateSpace == CoordinateSpaceMode.LocalToReference && coordinateSpaceReference != null)
        {
            return "local_to_reference";
        }

        return "world";
    }

    private string GetCoordinateReferenceLabel()
    {
        if (coordinateSpace == CoordinateSpaceMode.HandLocal)
        {
            return "Hand_WristRoot";
        }

        if (coordinateSpace == CoordinateSpaceMode.LocalToReference && coordinateSpaceReference != null)
        {
            return coordinateSpaceReference.name;
        }

        return "none";
    }

    private Vector3 ToOutputPosition(Vector3 worldPosition)
    {
        if (coordinateSpace == CoordinateSpaceMode.LocalToReference && coordinateSpaceReference != null)
        {
            return coordinateSpaceReference.InverseTransformPoint(worldPosition);
        }

        return worldPosition;
    }

    private Vector3 ToOutputPosition(Vector3 worldPosition, Vector3 handOriginWorldPosition, Quaternion handOriginWorldRotation)
    {
        if (coordinateSpace == CoordinateSpaceMode.HandLocal)
        {
            return Quaternion.Inverse(handOriginWorldRotation) * (worldPosition - handOriginWorldPosition);
        }

        return ToOutputPosition(worldPosition);
    }

    private Quaternion ToOutputRotation(Quaternion worldRotation, Quaternion handOriginWorldRotation)
    {
        if (coordinateSpace == CoordinateSpaceMode.HandLocal)
        {
            return Quaternion.Inverse(handOriginWorldRotation) * worldRotation;
        }

        return worldRotation;
    }

    private HandData BuildHandData(OVRHand hand, OVRSkeleton skeleton)
    {
        var data = new HandData();

        if (hand == null || skeleton == null || !hand.IsTracked)
        {
            data.tracked = false;
            return data;
        }

        Vector3 wristWorldPosition = GetWristPosition(skeleton);
        Quaternion wristRotation = GetWristRotation(skeleton);

        data.tracked = true;
        data.wristPosition = new Vec3(ToOutputPosition(wristWorldPosition, wristWorldPosition, wristRotation));
        data.wristRotation = new Quat(ToOutputRotation(wristRotation, wristRotation));
        data.indexTipPosition = new Vec3(ToOutputPosition(GetBonePosition(skeleton, OVRSkeleton.BoneId.Hand_IndexTip, wristWorldPosition), wristWorldPosition, wristRotation));
        data.indexPinchStrength = hand.GetFingerPinchStrength(OVRHand.HandFinger.Index);
        data.middlePinchStrength = hand.GetFingerPinchStrength(OVRHand.HandFinger.Middle);
        data.ringPinchStrength = hand.GetFingerPinchStrength(OVRHand.HandFinger.Ring);
        data.pinkyPinchStrength = hand.GetFingerPinchStrength(OVRHand.HandFinger.Pinky);

        return data;
    }

    private Vector3 GetWristPosition(OVRSkeleton skeleton)
    {
        Transform wrist = FindBone(skeleton, OVRSkeleton.BoneId.Hand_WristRoot);
        return wrist != null ? wrist.position : skeleton != null ? skeleton.transform.position : Vector3.zero;
    }

    private Quaternion GetWristRotation(OVRSkeleton skeleton)
    {
        Transform wrist = FindBone(skeleton, OVRSkeleton.BoneId.Hand_WristRoot);
        return wrist != null ? wrist.rotation : skeleton != null ? skeleton.transform.rotation : Quaternion.identity;
    }

    private Vector3 GetBonePosition(OVRSkeleton skeleton, OVRSkeleton.BoneId boneId, Vector3 fallback)
    {
        Transform bone = FindBone(skeleton, boneId);
        return bone != null ? bone.position : fallback;
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
}
