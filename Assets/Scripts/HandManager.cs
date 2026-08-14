using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Singleton quản lý tập trung hand tracking cho cả project.
/// Các script khác gọi HandManager.Instance thay vì tự FindObjectOfType.
/// </summary>
public class HandManager : MonoBehaviour
{
    public static HandManager Instance { get; private set; }

    [Header("Hand Objects")]
    public OVRHand leftHand;
    public OVRHand rightHand;
    public OVRSkeleton leftSkeleton;
    public OVRSkeleton rightSkeleton;

    // ── Public read-only state ────────────────────────────────
    public bool LeftTracked { get; private set; }
    public bool RightTracked { get; private set; }

    public Dictionary<OVRSkeleton.BoneId, Transform> LeftBones { get; private set; }
    public Dictionary<OVRSkeleton.BoneId, Transform> RightBones { get; private set; }

    // Pinch strengths — cập nhật mỗi frame
    public float LeftIndexPinch { get; private set; }
    public float RightIndexPinch { get; private set; }
    public float LeftMiddlePinch { get; private set; }
    public float RightMiddlePinch { get; private set; }

    // Bone IDs cache cho từng loại skeleton
    private bool leftIsOpenXR, rightIsOpenXR;
    public bool LeftIsOpenXR => leftIsOpenXR;
    public bool RightIsOpenXR => rightIsOpenXR;

    // ── Singleton ──────────────────────────────────────────────
    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        LeftBones = new Dictionary<OVRSkeleton.BoneId, Transform>(24);
        RightBones = new Dictionary<OVRSkeleton.BoneId, Transform>(24);
    }

    void LateUpdate()
    {
        // Rebuild bone cache mỗi frame (Meta SDK v203 không có BonesUpdated event)
        if (leftSkeleton != null && leftSkeleton.Bones != null)
        {
            leftIsOpenXR = leftSkeleton.GetSkeletonType() == OVRSkeleton.SkeletonType.XRHandLeft;
            RebuildCache(leftSkeleton.Bones, LeftBones);
        }

        if (rightSkeleton != null && rightSkeleton.Bones != null)
        {
            rightIsOpenXR = rightSkeleton.GetSkeletonType() == OVRSkeleton.SkeletonType.XRHandRight;
            RebuildCache(rightSkeleton.Bones, RightBones);
        }
    }

    void Update()
    {
        // Cache pinch strengths — các script khác chỉ đọc, không tự poll OVRHand
        if (leftHand != null)
        {
            LeftTracked = leftHand.IsTracked;
            if (LeftTracked)
            {
                LeftIndexPinch = leftHand.GetFingerPinchStrength(OVRHand.HandFinger.Index);
                LeftMiddlePinch = leftHand.GetFingerPinchStrength(OVRHand.HandFinger.Middle);
            }
            else
            {
                LeftIndexPinch = 0f;
                LeftMiddlePinch = 0f;
            }
        }

        if (rightHand != null)
        {
            RightTracked = rightHand.IsTracked;
            if (RightTracked)
            {
                RightIndexPinch = rightHand.GetFingerPinchStrength(OVRHand.HandFinger.Index);
                RightMiddlePinch = rightHand.GetFingerPinchStrength(OVRHand.HandFinger.Middle);
            }
            else
            {
                RightIndexPinch = 0f;
                RightMiddlePinch = 0f;
            }
        }
    }

    // ── Bone cache rebuild ────────────────────────────────────
    private static void RebuildCache(IList<OVRBone> bones, Dictionary<OVRSkeleton.BoneId, Transform> cache)
    {
        cache.Clear();
        if (bones == null) return;
        for (int i = 0; i < bones.Count; i++)
        {
            OVRBone b = bones[i];
            cache[b.Id] = b.Transform;
        }
    }

    // ── Public API: lấy Transform từ BoneId (O(1)) ────────────
    public bool TryGetBone(bool isRight, OVRSkeleton.BoneId id, out Transform bone)
    {
        var cache = isRight ? RightBones : LeftBones;
        return cache.TryGetValue(id, out bone);
    }

    public Transform GetBone(bool isRight, OVRSkeleton.BoneId id)
    {
        var cache = isRight ? RightBones : LeftBones;
        cache.TryGetValue(id, out Transform bone);
        return bone;
    }

    public Transform GetWrist(bool isRight)
    {
        var isOpenXR = isRight ? rightIsOpenXR : leftIsOpenXR;
        var id = isOpenXR ? OVRSkeleton.BoneId.XRHand_Wrist : OVRSkeleton.BoneId.Hand_WristRoot;
        return GetBone(isRight, id);
    }

    public Transform GetIndexTip(bool isRight)
    {
        var isOpenXR = isRight ? rightIsOpenXR : leftIsOpenXR;
        var id = isOpenXR ? OVRSkeleton.BoneId.XRHand_IndexTip : OVRSkeleton.BoneId.Hand_IndexTip;
        return GetBone(isRight, id);
    }
}
