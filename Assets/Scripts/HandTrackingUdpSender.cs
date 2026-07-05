using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

public class HandTrackingUdpSender_Debuggable : MonoBehaviour
{
    [Header("Hand Tracking Objects")]
    public GameObject leftHandObject;
    public GameObject rightHandObject;

    [Header("UDP Target")]
    public string targetIp = "192.168.1.10";
    public int targetPort = 5055;
    public float sendRate = 30f;

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

    private OVRHand leftHand;
    private OVRHand rightHand;
    private OVRSkeleton leftSkeleton;
    private OVRSkeleton rightSkeleton;
    private UdpClient udp;
    private IPEndPoint endpoint;
    private float nextSendTime;

    [Serializable]
    private class Packet
    {
        public string type = "hand_tracking";
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
    }

    private void OnDisable()
    {
        CloseUdp();
    }

    private void Update()
    {
        CacheHandComponents();
        UpdateDebugFields();

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
    }

    public void SendTestPacket()
    {
        SendCurrentPacket();
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
        }
        catch (Exception e)
        {
            udpReady = false;
            lastStatus = "UDP error: " + e.Message;
        }
    }

    private void CloseUdp()
    {
        udpReady = false;
        udp?.Close();
        udp = null;
    }

    private void UpdateDebugFields()
    {
        leftTracked = leftHand != null && leftHand.IsTracked;
        rightTracked = rightHand != null && rightHand.IsTracked;

        leftWristPosition = GetWristPosition(leftSkeleton);
        rightWristPosition = GetWristPosition(rightSkeleton);
        leftIndexTipPosition = GetBonePosition(leftSkeleton, OVRSkeleton.BoneId.Hand_IndexTip, leftWristPosition);
        rightIndexTipPosition = GetBonePosition(rightSkeleton, OVRSkeleton.BoneId.Hand_IndexTip, rightWristPosition);

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
        }
    }

    private HandData BuildHandData(OVRHand hand, OVRSkeleton skeleton)
    {
        var data = new HandData();

        if (hand == null || skeleton == null || !hand.IsTracked)
        {
            data.tracked = false;
            return data;
        }

        Vector3 wristPosition = GetWristPosition(skeleton);
        Quaternion wristRotation = GetWristRotation(skeleton);

        data.tracked = true;
        data.wristPosition = new Vec3(wristPosition);
        data.wristRotation = new Quat(wristRotation);
        data.indexTipPosition = new Vec3(GetBonePosition(skeleton, OVRSkeleton.BoneId.Hand_IndexTip, wristPosition));
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

        foreach (OVRBone bone in skeleton.Bones)
        {
            if (bone.Id == boneId)
            {
                return bone.Transform;
            }
        }

        return null;
    }
}
