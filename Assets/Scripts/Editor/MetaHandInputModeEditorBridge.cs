using UnityEditor;

public static class MetaHandInputModeEditorBridge
{
    public static void SetHandTrackingSupport(int mode)
    {
        OVRProjectConfig.HandTrackingSupport support = (OVRProjectConfig.HandTrackingSupport)mode;
        OVRProjectConfig projectConfig = OVRProjectConfig.CachedProjectConfig;
        Undo.RecordObject(projectConfig, "Set Meta Hand Tracking Support");
        projectConfig.handTrackingSupport = support;
        OVRProjectConfig.CommitProjectConfig(projectConfig);
        AssetDatabase.SaveAssets();
    }
}
