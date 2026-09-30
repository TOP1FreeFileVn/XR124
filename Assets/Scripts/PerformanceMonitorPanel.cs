using System;
using System.Collections.Generic;
using TMPro;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class PerformanceMonitorPanel : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private TMP_Text fpsText;
    [SerializeField] private TMP_Text frameTimeText;
    [SerializeField] private TMP_Text lowFpsText;
    [SerializeField] private TMP_Text cpuTimeText;
    [SerializeField] private TMP_Text gpuTimeText;
    [SerializeField] private TMP_Text hitchText;
    [SerializeField] private TMP_Text memoryText;
    [SerializeField] private TMP_Text gcText;
    [SerializeField] private TMP_Text objectStateText;
    [SerializeField] private CanvasGroup panelCanvasGroup;

    [Header("Summon Battle State")]
    [SerializeField] private XR124.Combat.BattleSummoner summoner;
    [SerializeField] private XR124.Combat.SealComboCaster sealCaster;

    [Header("Sampling")]
    [SerializeField, Min(0.1f)] private float refreshInterval = 0.25f;
    [SerializeField, Min(11f)] private float hitchThresholdMs = 25f;
    [SerializeField, Range(60, 600)] private int sampleCapacity = 240;

    [Header("Left Palm Menu")]
    [SerializeField] private OVRSkeleton leftHandSkeleton;
    [SerializeField, Range(-1f, 1f)] private float palmUpThreshold = 0.55f;
    [SerializeField, Min(0f)] private float showHoldSeconds = 0.12f;
    [SerializeField, Min(0f)] private float hideGraceSeconds = 0.18f;
    [SerializeField, Min(0f)] private float heightAbovePalm = 0.16f;
    [SerializeField, Min(0f)] private float offsetTowardPlayer = 0.04f;
    [SerializeField, Min(0f)] private float positionSmoothTime = 0.06f;
    [SerializeField, Min(0f)] private float rotationSmoothSpeed = 14f;

    private readonly FrameTiming[] frameTimings = new FrameTiming[1];
    private readonly Dictionary<OVRSkeleton.BoneId, Transform> boneCache =
        new Dictionary<OVRSkeleton.BoneId, Transform>();

    private float[] frameSamples;
    private float[] sortedSamples;
    private int sampleCount;
    private int sampleIndex;
    private float refreshTimer;
    private float accumulatedTime;
    private int accumulatedFrames;
    private ProfilerRecorder systemMemoryRecorder;
    private ProfilerRecorder gcMemoryRecorder;

    private OVRSkeleton.SkeletonType cachedSkeletonType = OVRSkeleton.SkeletonType.None;
    private OVRSkeleton.BoneId wristId;
    private OVRSkeleton.BoneId indexProximalId;
    private OVRSkeleton.BoneId middleProximalId;
    private OVRSkeleton.BoneId littleProximalId;
    private float palmUpSince = -1f;
    private float lastPalmUpTime = -1f;
    private bool panelVisible;
    private Vector3 panelVelocity;

    /// <summary>
    /// Khởi tạo bộ đệm đo hiệu năng, tìm tay trái và ẩn bảng cho đến khi người chơi ngửa lòng bàn tay.
    /// </summary>
    private void Awake()
    {
        frameSamples = new float[sampleCapacity];
        sortedSamples = new float[sampleCapacity];
        systemMemoryRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "System Used Memory");
        gcMemoryRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Reserved Memory");
        panelCanvasGroup ??= GetComponent<CanvasGroup>();
        // Tìm hệ triệu hồi một lần lúc khởi tạo; không dò lại cả scene theo chu kỳ để tránh tốn CPU trên Quest.
        if (summoner == null)
        {
            summoner = FindFirstObjectByType<XR124.Combat.BattleSummoner>();
        }

        if (sealCaster == null)
        {
            sealCaster = FindFirstObjectByType<XR124.Combat.SealComboCaster>();
        }
        FindLeftHandSkeleton();
        SetPanelVisible(false);
    }

    /// <summary>
    /// Cập nhật trạng thái palm menu mỗi frame và lấy mẫu hiệu năng mà không tạo rác bộ nhớ liên tục.
    /// </summary>
    private void Update()
    {
        UpdatePalmMenu();

        float deltaTime = Time.unscaledDeltaTime;
        if (deltaTime <= 0f)
        {
            return;
        }

        float frameTimeMs = deltaTime * 1000f;
        frameSamples[sampleIndex] = frameTimeMs;
        sampleIndex = (sampleIndex + 1) % frameSamples.Length;
        sampleCount = Mathf.Min(sampleCount + 1, frameSamples.Length);

        accumulatedTime += deltaTime;
        accumulatedFrames++;
        refreshTimer += deltaTime;
        FrameTimingManager.CaptureFrameTimings();

        if (refreshTimer >= refreshInterval)
        {
            RefreshDisplay();
            refreshTimer = 0f;
            accumulatedTime = 0f;
            accumulatedFrames = 0;
        }
    }

    /// <summary>
    /// Tự tìm skeleton tay trái để bảng vẫn hoạt động khi scene chưa gán tham chiếu sẵn.
    /// </summary>
    private void FindLeftHandSkeleton()
    {
        if (leftHandSkeleton != null && IsLeftHand(leftHandSkeleton.GetSkeletonType()))
        {
            return;
        }

        leftHandSkeleton = null;
        OVRSkeleton[] skeletons = FindObjectsByType<OVRSkeleton>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        foreach (OVRSkeleton skeleton in skeletons)
        {
            if (skeleton != null && IsLeftHand(skeleton.GetSkeletonType()))
            {
                leftHandSkeleton = skeleton;
                cachedSkeletonType = OVRSkeleton.SkeletonType.None;
                return;
            }
        }
    }

    /// <summary>
    /// Hiện bảng khi lòng bàn tay trái ngửa đủ lâu và giữ một khoảng trễ ngắn khi pose rung nhẹ.
    /// </summary>
    private void UpdatePalmMenu()
    {
        if (!TryGetLeftPalmPose(out Vector3 palmCenter, out Vector3 palmNormal))
        {
            palmUpSince = -1f;
            lastPalmUpTime = -1f;
            SetPanelVisible(false);
            return;
        }

        float now = Time.unscaledTime;
        bool palmIsUp = Vector3.Dot(palmNormal, Vector3.up) >= palmUpThreshold;
        if (palmIsUp)
        {
            lastPalmUpTime = now;
            if (palmUpSince < 0f)
            {
                palmUpSince = now;
            }

            UpdatePanelPose(palmCenter, !panelVisible);
            if (now - palmUpSince >= showHoldSeconds)
            {
                SetPanelVisible(true);
            }

            return;
        }

        palmUpSince = -1f;
        if (panelVisible && now - lastPalmUpTime <= hideGraceSeconds)
        {
            UpdatePanelPose(palmCenter, false);
            return;
        }

        SetPanelVisible(false);
    }

    /// <summary>
    /// Đọc bốn khớp theo đúng loại skeleton Meta XR rồi tính tâm và pháp tuyến của lòng bàn tay trái.
    /// </summary>
    private bool TryGetLeftPalmPose(out Vector3 palmCenter, out Vector3 palmNormal)
    {
        palmCenter = Vector3.zero;
        palmNormal = Vector3.zero;
        if (leftHandSkeleton == null)
        {
            FindLeftHandSkeleton();
        }

        if (leftHandSkeleton == null || !leftHandSkeleton.IsDataValid)
        {
            return false;
        }

        OVRSkeleton.SkeletonType skeletonType = leftHandSkeleton.GetSkeletonType();
        if (!IsLeftHand(skeletonType) || !ResolveBoneIds(skeletonType) || !RefreshBoneCache())
        {
            return false;
        }

        if (!boneCache.TryGetValue(wristId, out Transform wrist)
            || !boneCache.TryGetValue(indexProximalId, out Transform index)
            || !boneCache.TryGetValue(middleProximalId, out Transform middle)
            || !boneCache.TryGetValue(littleProximalId, out Transform little))
        {
            return false;
        }

        Vector3 fingerDirection = middle.position - wrist.position;
        Vector3 acrossPalm = index.position - little.position;

        // Với tay trái, thứ tự này tạo pháp tuyến hướng ra khỏi lòng bàn tay; đảo lại sẽ nhận nhầm tư thế úp tay.
        palmNormal = Vector3.Cross(acrossPalm, fingerDirection).normalized;
        if (palmNormal.sqrMagnitude < 0.01f)
        {
            return false;
        }

        palmCenter = (wrist.position + index.position + middle.position + little.position) * 0.25f;
        return true;
    }

    /// <summary>
    /// Chọn đúng BoneId OpenXR hoặc legacy; hai layout có giá trị ID khác nhau nên tuyệt đối không dùng lẫn.
    /// </summary>
    private bool ResolveBoneIds(OVRSkeleton.SkeletonType skeletonType)
    {
        if (cachedSkeletonType == skeletonType)
        {
            return true;
        }

        if (skeletonType == OVRSkeleton.SkeletonType.XRHandLeft)
        {
            wristId = OVRSkeleton.BoneId.XRHand_Wrist;
            indexProximalId = OVRSkeleton.BoneId.XRHand_IndexProximal;
            middleProximalId = OVRSkeleton.BoneId.XRHand_MiddleProximal;
            littleProximalId = OVRSkeleton.BoneId.XRHand_LittleProximal;
        }
        else if (skeletonType == OVRSkeleton.SkeletonType.HandLeft)
        {
            wristId = OVRSkeleton.BoneId.Hand_WristRoot;
            indexProximalId = OVRSkeleton.BoneId.Hand_Index1;
            middleProximalId = OVRSkeleton.BoneId.Hand_Middle1;
            littleProximalId = OVRSkeleton.BoneId.Hand_Pinky1;
        }
        else
        {
            return false;
        }

        cachedSkeletonType = skeletonType;
        return true;
    }

    /// <summary>
    /// Làm mới ánh xạ BoneId sang Transform vì Meta XR có thể tạo lại xương sau khi tracking phục hồi.
    /// </summary>
    private bool RefreshBoneCache()
    {
        if (leftHandSkeleton.Bones == null || leftHandSkeleton.Bones.Count == 0)
        {
            return false;
        }

        boneCache.Clear();
        foreach (OVRBone bone in leftHandSkeleton.Bones)
        {
            if (bone != null && bone.Transform != null)
            {
                boneCache[bone.Id] = bone.Transform;
            }
        }

        return true;
    }

    /// <summary>
    /// Đặt bảng phía trên lòng bàn tay và luôn xoay mặt bảng về đầu người chơi với chuyển động được làm mượt.
    /// </summary>
    private void UpdatePanelPose(Vector3 palmCenter, bool snap)
    {
        Camera referenceCamera = Camera.main;
        if (referenceCamera == null)
        {
            return;
        }

        Vector3 towardPlayer = Vector3.ProjectOnPlane(
            referenceCamera.transform.position - palmCenter,
            Vector3.up).normalized;
        Vector3 targetPosition = palmCenter
            + Vector3.up * heightAbovePalm
            + towardPlayer * offsetTowardPlayer;
        Quaternion targetRotation = Quaternion.LookRotation(
            targetPosition - referenceCamera.transform.position,
            Vector3.up);

        if (snap || positionSmoothTime <= 0f)
        {
            transform.position = targetPosition;
            transform.rotation = targetRotation;
            panelVelocity = Vector3.zero;
            return;
        }

        transform.position = Vector3.SmoothDamp(
            transform.position,
            targetPosition,
            ref panelVelocity,
            positionSmoothTime,
            Mathf.Infinity,
            Time.unscaledDeltaTime);
        float rotationAmount = 1f - Mathf.Exp(-rotationSmoothSpeed * Time.unscaledDeltaTime);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationAmount);
    }

    /// <summary>
    /// Đổi alpha và trạng thái nhận ray thay vì tắt GameObject để script vẫn phát hiện lần ngửa tay tiếp theo.
    /// </summary>
    private void SetPanelVisible(bool visible)
    {
        panelVisible = visible;
        if (panelCanvasGroup == null)
        {
            return;
        }

        panelCanvasGroup.alpha = visible ? 1f : 0f;
        panelCanvasGroup.interactable = visible;
        panelCanvasGroup.blocksRaycasts = visible;
    }

    /// <summary>
    /// Xác nhận skeleton đang dùng thực sự là tay trái trước khi đọc pose palm menu.
    /// </summary>
    private static bool IsLeftHand(OVRSkeleton.SkeletonType skeletonType)
    {
        return skeletonType == OVRSkeleton.SkeletonType.XRHandLeft
            || skeletonType == OVRSkeleton.SkeletonType.HandLeft;
    }

    /// <summary>
    /// Giải phóng các bộ ghi Profiler khi bảng bị hủy hoặc scene được đóng.
    /// </summary>
    private void OnDestroy()
    {
        systemMemoryRecorder.Dispose();
        gcMemoryRecorder.Dispose();
    }

    /// <summary>
    /// Tải lại scene hiện tại để đưa vật thể, kẻ địch và trạng thái trò chơi về lúc bắt đầu.
    /// </summary>
    public void ResetGame()
    {
        Time.timeScale = 1f;
        Scene activeScene = SceneManager.GetActiveScene();
        if (activeScene.buildIndex >= 0)
        {
            SceneManager.LoadScene(activeScene.buildIndex);
        }
        else
        {
            SceneManager.LoadScene(activeScene.name);
        }
    }

    /// <summary>
    /// Tính các chỉ số trong cửa sổ mẫu hiện tại và đưa kết quả lên bảng.
    /// </summary>
    private void RefreshDisplay()
    {
        RefreshObjectState();

        if (sampleCount == 0 || accumulatedTime <= 0f)
        {
            return;
        }

        float fps = accumulatedFrames / accumulatedTime;
        float averageFrameTimeMs = accumulatedTime * 1000f / accumulatedFrames;
        Array.Copy(frameSamples, sortedSamples, sampleCount);
        Array.Sort(sortedSamples, 0, sampleCount);

        int percentileIndex = Mathf.Clamp(Mathf.CeilToInt(sampleCount * 0.99f) - 1, 0, sampleCount - 1);
        float percentileFrameTimeMs = sortedSamples[percentileIndex];
        float onePercentLowFps = percentileFrameTimeMs > 0f ? 1000f / percentileFrameTimeMs : 0f;
        int hitchCount = CountHitches();

        SetText(fpsText, $"{fps:0.0}");
        SetText(frameTimeText, $"{averageFrameTimeMs:0.00} ms");
        SetText(lowFpsText, $"{onePercentLowFps:0.0}");
        SetText(hitchText, $"{hitchCount} / {sampleCount}");

        uint timingCount = FrameTimingManager.GetLatestTimings(1, frameTimings);
        SetText(cpuTimeText, timingCount > 0 ? $"{frameTimings[0].cpuFrameTime:0.00} ms" : "N/A");
        SetText(gpuTimeText, timingCount > 0 && frameTimings[0].gpuFrameTime > 0
            ? $"{frameTimings[0].gpuFrameTime:0.00} ms"
            : "N/A");

        SetText(memoryText, FormatBytes(systemMemoryRecorder.Valid ? systemMemoryRecorder.LastValue : 0));
        SetText(gcText, FormatBytes(gcMemoryRecorder.Valid ? gcMemoryRecorder.LastValue : 0));
    }

    /// <summary>
    /// Hiện trạng thái trận triệu hồi trên bảng lòng bàn tay trái: giai đoạn triệu hồi, HP/AP/Energy của pet người chơi,
    /// HP địch, chuỗi ấn đang kết và kết quả lệnh gần nhất. Chỉ dùng ASCII vì font TMP có thể thiếu dấu tiếng Việt.
    /// </summary>
    private void RefreshObjectState()
    {
        if (summoner == null)
        {
            SetText(objectStateText, "SUMMON  BattleSummoner not found");
            return;
        }

        XR124.Combat.PetCombatant player = summoner.PlayerPet;
        XR124.Combat.PetCombatant enemy = summoner.EnemyPet;
        string state = $"SUMMON  {summoner.State}";

        // Chỉ hiện chỉ số khi đang/đã đánh; trước đó pet còn ẩn và chưa có số liệu trận.
        if (player != null && (player.IsInMatch || summoner.State == XR124.Combat.BattleSummoner.SummonState.Finished))
        {
            state += $"\nYOU  HP {player.CurrentHp:0}/{player.MaxHp:0}  AP {player.CurrentAP}/{player.MaxAP}  E {player.CurrentEnergy}/{player.UltimateCost}";
            if (enemy != null)
            {
                state += $"\nFOE  HP {enemy.CurrentHp:0}/{enemy.MaxHp:0}";
            }
        }

        if (sealCaster != null)
        {
            state += $"\nSEAL  {sealCaster.PendingSequence}  |  {sealCaster.LastResult}";
        }

        SetText(objectStateText, state);
    }

    /// <summary>
    /// Đếm số khung hình vượt ngưỡng giật trong toàn bộ cửa sổ mẫu đang hiển thị.
    /// </summary>
    private int CountHitches()
    {
        int count = 0;
        for (int i = 0; i < sampleCount; i++)
        {
            if (frameSamples[i] >= hitchThresholdMs)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// Gán chữ khi tham chiếu UI hợp lệ để bảng vẫn chạy nếu một ô bị bỏ trống.
    /// </summary>
    private static void SetText(TMP_Text target, string value)
    {
        if (target != null)
        {
            target.text = value;
        }
    }

    /// <summary>
    /// Đổi số byte sang MB để thông số bộ nhớ dễ đọc trong kính.
    /// </summary>
    private static string FormatBytes(long bytes)
    {
        return bytes > 0 ? $"{bytes / (1024f * 1024f):0.0} MB" : "N/A";
    }
}
