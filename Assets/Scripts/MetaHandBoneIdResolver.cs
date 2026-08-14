public static class MetaHandBoneIdResolver
{
    public static OVRSkeleton.BoneId ResolveLegacyIdForSkeleton(
        OVRSkeleton skeleton,
        OVRSkeleton.BoneId legacyBoneId)
    {
        if (skeleton == null || !IsOpenXRHand(skeleton.GetSkeletonType()))
        {
            return legacyBoneId;
        }

        // OpenXR and legacy hand IDs reuse numeric values for different joints.
        switch (legacyBoneId)
        {
            case OVRSkeleton.BoneId.Hand_WristRoot:
                return OVRSkeleton.BoneId.XRHand_Wrist;
            case OVRSkeleton.BoneId.Hand_ForearmStub:
            case OVRSkeleton.BoneId.Hand_Thumb0:
                return OVRSkeleton.BoneId.Invalid;

            case OVRSkeleton.BoneId.Hand_Thumb1:
                return OVRSkeleton.BoneId.XRHand_ThumbMetacarpal;
            case OVRSkeleton.BoneId.Hand_Thumb2:
                return OVRSkeleton.BoneId.XRHand_ThumbProximal;
            case OVRSkeleton.BoneId.Hand_Thumb3:
                return OVRSkeleton.BoneId.XRHand_ThumbDistal;
            case OVRSkeleton.BoneId.Hand_ThumbTip:
                return OVRSkeleton.BoneId.XRHand_ThumbTip;

            case OVRSkeleton.BoneId.Hand_Index1:
                return OVRSkeleton.BoneId.XRHand_IndexProximal;
            case OVRSkeleton.BoneId.Hand_Index2:
                return OVRSkeleton.BoneId.XRHand_IndexIntermediate;
            case OVRSkeleton.BoneId.Hand_Index3:
                return OVRSkeleton.BoneId.XRHand_IndexDistal;
            case OVRSkeleton.BoneId.Hand_IndexTip:
                return OVRSkeleton.BoneId.XRHand_IndexTip;

            case OVRSkeleton.BoneId.Hand_Middle1:
                return OVRSkeleton.BoneId.XRHand_MiddleProximal;
            case OVRSkeleton.BoneId.Hand_Middle2:
                return OVRSkeleton.BoneId.XRHand_MiddleIntermediate;
            case OVRSkeleton.BoneId.Hand_Middle3:
                return OVRSkeleton.BoneId.XRHand_MiddleDistal;
            case OVRSkeleton.BoneId.Hand_MiddleTip:
                return OVRSkeleton.BoneId.XRHand_MiddleTip;

            case OVRSkeleton.BoneId.Hand_Ring1:
                return OVRSkeleton.BoneId.XRHand_RingProximal;
            case OVRSkeleton.BoneId.Hand_Ring2:
                return OVRSkeleton.BoneId.XRHand_RingIntermediate;
            case OVRSkeleton.BoneId.Hand_Ring3:
                return OVRSkeleton.BoneId.XRHand_RingDistal;
            case OVRSkeleton.BoneId.Hand_RingTip:
                return OVRSkeleton.BoneId.XRHand_RingTip;

            case OVRSkeleton.BoneId.Hand_Pinky0:
                return OVRSkeleton.BoneId.XRHand_LittleMetacarpal;
            case OVRSkeleton.BoneId.Hand_Pinky1:
                return OVRSkeleton.BoneId.XRHand_LittleProximal;
            case OVRSkeleton.BoneId.Hand_Pinky2:
                return OVRSkeleton.BoneId.XRHand_LittleIntermediate;
            case OVRSkeleton.BoneId.Hand_Pinky3:
                return OVRSkeleton.BoneId.XRHand_LittleDistal;
            case OVRSkeleton.BoneId.Hand_PinkyTip:
                return OVRSkeleton.BoneId.XRHand_LittleTip;

            default:
                return OVRSkeleton.BoneId.Invalid;
        }
    }

    private static bool IsOpenXRHand(OVRSkeleton.SkeletonType skeletonType)
    {
        return skeletonType == OVRSkeleton.SkeletonType.XRHandLeft ||
               skeletonType == OVRSkeleton.SkeletonType.XRHandRight;
    }
}
