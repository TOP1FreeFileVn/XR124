using System.Collections;
using TMPro;
using UnityEngine;

public sealed class CompanionCombatController : MonoBehaviour
{
    private enum HandSkillGesture
    {
        None,
        SkillOne,
        SkillTwo
    }

    [Header("References")]
    [SerializeField] private CombatActor actor;
    [SerializeField] private PocketControl movementController;

    [Header("Hand Skill Gestures")]
    [SerializeField] private bool enableHandSkillGestures = true;
    [SerializeField, Min(0f)] private float fistMenuHoldSeconds = 0.2f;
    [SerializeField, Min(0f)] private float handGestureHoldSeconds = 0.15f;
    [SerializeField, Min(0.01f)] private float skillBubbleSize = 0.065f;
    [SerializeField, Min(0.02f)] private float skillBubbleSpacing = 0.09f;
    [SerializeField, Min(0f)] private float skillMenuHeight = 0.1f;
    [SerializeField, Min(0f)] private float skillMenuCameraOffset = 0.025f;

    [Header("Auto Target")]
    [SerializeField, Min(1f)] private float scanRadius = 6f;
    [SerializeField, Min(0.1f)] private float targetRefreshInterval = 0.2f;
    [SerializeField, Min(0f)] private float minimumTargetLockTime = 1f;
    [SerializeField, Range(0.1f, 1f)] private float switchTargetScoreRatio = 0.75f;

    [Header("Basic Attack")]
    [SerializeField, Min(0.1f)] private float basicAttackRange = 3.2f;
    [SerializeField, Min(0.1f)] private float basicAttackInterval = 0.9f;
    [SerializeField, Min(0f)] private float basicAttackWindup = 0.16f;
    [SerializeField, Min(0f)] private float basicAttackDamage = 8f;

    [Header("Skill One - Cyan Bolt")]
    [SerializeField, Min(0f)] private float skillOneDamage = 30f;
    [SerializeField, Min(0.1f)] private float skillOneRange = 8f;
    [SerializeField, Min(0f)] private float skillOneCooldown = 4f;

    [Header("Skill Two - Ground Pulse")]
    [SerializeField, Min(0f)] private float skillTwoDamage = 22f;
    [SerializeField, Min(0.1f)] private float skillTwoRadius = 1.5f;
    [SerializeField, Min(0f)] private float skillTwoCooldown = 7f;
    [SerializeField, Min(0f)] private float skillTwoDelay = 0.65f;

    [Header("Runtime")]
    [SerializeField] private CombatActor currentTarget;
    [SerializeField] private float skillOneRemaining;
    [SerializeField] private float skillTwoRemaining;
    [SerializeField] private bool isCasting;
    [SerializeField] private string currentHandGesture = "None";
    [SerializeField] private bool skillMenuOpen;
    [SerializeField] private HandSkillGesture highlightedSkill;

    private float nextTargetRefreshTime;
    private float targetLockedAt;
    private float nextBasicAttackTime;
    private Coroutine basicAttackRoutine;
    private HandSkillGesture pendingHandGesture;
    private float pendingHandGestureSince = -1f;
    private float fistPoseSince = -1f;
    private GameObject skillMenuRoot;
    private Transform skillOneBubble;
    private Transform skillTwoBubble;
    private Renderer skillOneBubbleRenderer;
    private Renderer skillTwoBubbleRenderer;
    private TextMeshPro skillOneLabel;
    private TextMeshPro skillTwoLabel;
    private Material skillOneMaterial;
    private Material skillTwoMaterial;
    private Material cooldownMaterial;
    private Material highlightMaterial;

    public CombatActor CurrentTarget => currentTarget;
    public float SkillOneCooldownNormalized => skillOneCooldown > 0f ? skillOneRemaining / skillOneCooldown : 0f;
    public float SkillTwoCooldownNormalized => skillTwoCooldown > 0f ? skillTwoRemaining / skillTwoCooldown : 0f;

    // Tạo dòng trạng thái combat cho UI debug mà không can thiệp auto attack, kỹ năng hoặc di chuyển.
    public string GetRuntimeDebugText()
    {
        string action = isCasting
            ? "Casting"
            : basicAttackRoutine != null
                ? "Basic Attack"
                : "Ready";
        string targetName = currentTarget != null ? currentTarget.name : "None";
        return $"COMBAT  {action} | target={targetName} | gesture={currentHandGesture}"
            + $" | S1={skillOneRemaining:F1}s S2={skillTwoRemaining:F1}s";
    }

    // Tự lấy các component cùng object và chuẩn bị menu kỹ năng runtime quanh bàn tay phải.
    private void Awake()
    {
        if (actor == null)
        {
            actor = GetComponent<CombatActor>();
        }

        if (movementController == null)
        {
            movementController = FindFirstObjectByType<PocketControl>();
        }

        CreateSkillMenuVisuals();
    }

    // Cập nhật mục tiêu, cooldown, input thử nghiệm và đánh thường tự động theo thời gian thực.
    private void Update()
    {
        if (actor == null || !actor.IsAlive)
        {
            return;
        }

        skillOneRemaining = Mathf.Max(0f, skillOneRemaining - Time.deltaTime);
        skillTwoRemaining = Mathf.Max(0f, skillTwoRemaining - Time.deltaTime);

        if (Time.time >= nextTargetRefreshTime)
        {
            RefreshTarget();
            nextTargetRefreshTime = Time.time + targetRefreshInterval;
        }

        ReadPrototypeInput();
        ReadHandSkillGestures();
        TryStartBasicAttack();
    }

    // Cho phép gesture detector hoặc UI bên ngoài gọi trực tiếp chiêu đạn thẳng.
    public bool TryCastSkillOne()
    {
        if (isCasting || skillOneRemaining > 0f || !IsTargetValid(currentTarget, skillOneRange))
        {
            return false;
        }

        skillOneRemaining = skillOneCooldown;
        StartCoroutine(CastSkillOneRoutine(currentTarget));
        return true;
    }

    // Cho phép gesture detector hoặc UI bên ngoài gọi trực tiếp chiêu AoE tại mục tiêu đang khóa.
    public bool TryCastSkillTwo()
    {
        if (isCasting || skillTwoRemaining > 0f || !IsTargetValid(currentTarget, skillOneRange))
        {
            return false;
        }

        skillTwoRemaining = skillTwoCooldown;
        StartCoroutine(CastSkillTwoRoutine(currentTarget.transform.position));
        return true;
    }

    // Đọc phím 1/2 trong Editor và A/B trên Touch Controller để test trước khi nối gesture.
    private void ReadPrototypeInput()
    {
        bool skillOnePressed = false;
        bool skillTwoPressed = false;

#if ENABLE_LEGACY_INPUT_MANAGER
        skillOnePressed = Input.GetKeyDown(KeyCode.Alpha1);
        skillTwoPressed = Input.GetKeyDown(KeyCode.Alpha2);
#endif

        skillOnePressed |= OVRInput.GetDown(OVRInput.Button.One, OVRInput.Controller.Touch);
        skillTwoPressed |= OVRInput.GetDown(OVRInput.Button.Two, OVRInput.Controller.Touch);

        if (skillOnePressed)
        {
            TryCastSkillOne();
        }
        else if (skillTwoPressed)
        {
            TryCastSkillTwo();
        }
    }

    // Nắm tay phải đủ lâu để mở menu; sau đó duỗi riêng ngón trỏ hoặc giữa và giữ ổn định để chọn kỹ năng.
    private void ReadHandSkillGestures()
    {
        if (!enableHandSkillGestures || movementController == null
            || !movementController.TryGetSkillHandState(
                out Vector3 palmPosition,
                out bool fist,
                out float index,
                out float middle,
                out float ring,
                out float little))
        {
            ResetHandGestureState();
            return;
        }

        if (!skillMenuOpen)
        {
            if (!fist)
            {
                fistPoseSince = -1f;
                currentHandGesture = "None";
                return;
            }

            if (fistPoseSince < 0f)
            {
                fistPoseSince = Time.unscaledTime;
            }

            currentHandGesture = "Fist - opening menu";
            if (Time.unscaledTime - fistPoseSince >= fistMenuHoldSeconds)
            {
                OpenSkillMenu(palmPosition);
            }

            return;
        }

        UpdateSkillMenuPose(palmPosition);
        HandSkillGesture detectedGesture = DetectSkillFinger(index, middle, ring, little);
        highlightedSkill = detectedGesture;
        UpdateSkillMenuVisualState();

        if (fist || detectedGesture == HandSkillGesture.None)
        {
            currentHandGesture = fist ? "Menu open - fist" : "Menu open - choose one finger";
            pendingHandGesture = HandSkillGesture.None;
            pendingHandGestureSince = -1f;
            return;
        }

        currentHandGesture = detectedGesture == HandSkillGesture.SkillOne
            ? "Index - Skill 1"
            : "Middle - Skill 2";
        bool selectedSkillOnCooldown = detectedGesture == HandSkillGesture.SkillOne
            ? skillOneRemaining > 0f
            : skillTwoRemaining > 0f;
        if (selectedSkillOnCooldown)
        {
            currentHandGesture = $"{detectedGesture} cooldown";
            pendingHandGesture = HandSkillGesture.None;
            pendingHandGestureSince = -1f;
            return;
        }

        if (pendingHandGesture != detectedGesture)
        {
            pendingHandGesture = detectedGesture;
            pendingHandGestureSince = Time.unscaledTime;
            return;
        }

        if (Time.unscaledTime - pendingHandGestureSince < handGestureHoldSeconds)
        {
            return;
        }

        bool castSucceeded = detectedGesture == HandSkillGesture.SkillOne
            ? TryCastSkillOne()
            : TryCastSkillTwo();
        currentHandGesture = castSucceeded
            ? $"{detectedGesture} cast"
            : $"{detectedGesture} blocked";

        if (castSucceeded)
        {
            CloseSkillMenu(false);
        }
    }

    // Nhận đúng một ngón duỗi: trỏ là chiêu 1, giữa là chiêu 2; các ngón còn lại phải đang co.
    private HandSkillGesture DetectSkillFinger(float index, float middle, float ring, float little)
    {
        float extended = movementController.extendedRatio;
        float curled = movementController.curledRatio;
        if (index >= extended && middle <= curled && ring <= curled && little <= curled)
        {
            return HandSkillGesture.SkillOne;
        }

        if (middle >= extended && index <= curled && ring <= curled && little <= curled)
        {
            return HandSkillGesture.SkillTwo;
        }

        return HandSkillGesture.None;
    }

    // Xóa trạng thái gesture và đóng menu khi tracking mất để lần nhận tay kế tiếp phải nắm tay mở lại từ đầu.
    private void ResetHandGestureState()
    {
        currentHandGesture = "None";
        pendingHandGesture = HandSkillGesture.None;
        pendingHandGestureSince = -1f;
        fistPoseSince = -1f;
        CloseSkillMenu(false);
    }

    // Tạo hai bong bóng kỹ năng bằng primitive runtime để scene không cần prefab hoặc thao tác kéo thả bổ sung.
    private void CreateSkillMenuVisuals()
    {
        skillMenuRoot = new GameObject("Right Hand Skill Menu");
        skillMenuRoot.hideFlags = HideFlags.DontSave;
        skillOneMaterial = CombatActor.CreateMaterial(new Color(0.05f, 0.8f, 1f));
        skillTwoMaterial = CombatActor.CreateMaterial(new Color(1f, 0.58f, 0.08f));
        cooldownMaterial = CombatActor.CreateMaterial(new Color(0.22f, 0.24f, 0.28f));
        highlightMaterial = CombatActor.CreateMaterial(new Color(0.35f, 1f, 0.45f));

        CreateSkillBubble("Skill 1 Bubble", "INDEX\nS1", skillOneMaterial,
            out skillOneBubble, out skillOneBubbleRenderer, out skillOneLabel);
        CreateSkillBubble("Skill 2 Bubble", "MIDDLE\nS2", skillTwoMaterial,
            out skillTwoBubble, out skillTwoBubbleRenderer, out skillTwoLabel);
        skillMenuRoot.SetActive(false);
    }

    // Dựng một quả cầu cùng nhãn chữ, đồng thời bỏ collider để menu không cản raycast và vật lý trong game.
    private void CreateSkillBubble(
        string objectName,
        string labelText,
        Material material,
        out Transform bubble,
        out Renderer bubbleRenderer,
        out TextMeshPro label)
    {
        GameObject bubbleObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        bubbleObject.name = objectName;
        bubbleObject.hideFlags = HideFlags.DontSave;
        bubbleObject.transform.SetParent(skillMenuRoot.transform, false);
        bubbleObject.transform.localScale = Vector3.one * skillBubbleSize;
        Collider bubbleCollider = bubbleObject.GetComponent<Collider>();
        if (bubbleCollider != null)
        {
            Destroy(bubbleCollider);
        }

        bubble = bubbleObject.transform;
        bubbleRenderer = bubbleObject.GetComponent<Renderer>();
        bubbleRenderer.sharedMaterial = material;

        GameObject labelObject = new GameObject($"{objectName} Label");
        labelObject.hideFlags = HideFlags.DontSave;
        labelObject.transform.SetParent(skillMenuRoot.transform, false);
        label = labelObject.AddComponent<TextMeshPro>();
        label.text = labelText;
        label.alignment = TextAlignmentOptions.Center;
        label.fontSize = 2.2f;
        label.color = Color.white;
        label.enableWordWrapping = false;
        label.rectTransform.sizeDelta = new Vector2(0.18f, 0.08f);
    }

    // Mở menu tại lòng bàn tay hiện tại và xóa ứng viên cũ để tránh tung chiêu ngay từ frame mở.
    private void OpenSkillMenu(Vector3 palmPosition)
    {
        skillMenuOpen = true;
        fistPoseSince = -1f;
        pendingHandGesture = HandSkillGesture.None;
        pendingHandGestureSince = -1f;
        skillMenuRoot.SetActive(true);
        UpdateSkillMenuPose(palmPosition);
        UpdateSkillMenuVisualState();
        currentHandGesture = "Menu open";
    }

    // Đặt menu phía trên lòng bàn tay và billboard nhãn theo camera để người chơi luôn đọc được.
    private void UpdateSkillMenuPose(Vector3 palmPosition)
    {
        Camera camera = Camera.main;
        Vector3 right = camera != null ? camera.transform.right : Vector3.right;
        Vector3 up = camera != null ? camera.transform.up : Vector3.up;
        Vector3 towardCamera = camera != null
            ? (camera.transform.position - palmPosition).normalized
            : Vector3.back;
        Vector3 center = palmPosition + up * skillMenuHeight + towardCamera * skillMenuCameraOffset;

        skillMenuRoot.transform.position = center;
        skillOneBubble.position = center - right * skillBubbleSpacing;
        skillTwoBubble.position = center + right * skillBubbleSpacing;
        skillOneLabel.transform.position = skillOneBubble.position + towardCamera * (skillBubbleSize * 0.55f);
        skillTwoLabel.transform.position = skillTwoBubble.position + towardCamera * (skillBubbleSize * 0.55f);
        if (camera != null)
        {
            skillOneLabel.transform.rotation = camera.transform.rotation;
            skillTwoLabel.transform.rotation = camera.transform.rotation;
        }
    }

    // Đổi màu bong bóng theo cooldown và ngón đang được giữ để phản hồi lựa chọn trước khi kích hoạt.
    private void UpdateSkillMenuVisualState()
    {
        skillOneBubbleRenderer.sharedMaterial = skillOneRemaining > 0f
            ? cooldownMaterial
            : highlightedSkill == HandSkillGesture.SkillOne ? highlightMaterial : skillOneMaterial;
        skillTwoBubbleRenderer.sharedMaterial = skillTwoRemaining > 0f
            ? cooldownMaterial
            : highlightedSkill == HandSkillGesture.SkillTwo ? highlightMaterial : skillTwoMaterial;
        skillOneLabel.text = skillOneRemaining > 0f ? $"S1\n{skillOneRemaining:F1}s" : "INDEX\nS1";
        skillTwoLabel.text = skillTwoRemaining > 0f ? $"S2\n{skillTwoRemaining:F1}s" : "MIDDLE\nS2";
    }

    // Đóng menu và tùy chọn xóa chuỗi debug để có thể giữ lại kết quả cast vừa thực hiện.
    private void CloseSkillMenu(bool clearDebugText)
    {
        skillMenuOpen = false;
        highlightedSkill = HandSkillGesture.None;
        pendingHandGesture = HandSkillGesture.None;
        pendingHandGestureSince = -1f;
        fistPoseSince = -1f;
        if (skillMenuRoot != null)
        {
            skillMenuRoot.SetActive(false);
        }

        if (clearDebugText)
        {
            currentHandGesture = "None";
        }
    }

    // Hủy các material runtime khi controller bị phá để không giữ tài nguyên sau khi đổi scene.
    private void OnDestroy()
    {
        if (skillMenuRoot != null)
        {
            Destroy(skillMenuRoot);
        }

        Destroy(skillOneMaterial);
        Destroy(skillTwoMaterial);
        Destroy(cooldownMaterial);
        Destroy(highlightMaterial);
    }

    // Chọn enemy theo khoảng cách và hướng nhìn, đồng thời giữ mục tiêu cũ đủ lâu để tránh nhảy khóa liên tục.
    private void RefreshTarget()
    {
        CombatActor bestTarget = null;
        float bestScore = float.MaxValue;
        foreach (CombatActor candidate in CombatActor.All)
        {
            if (!IsTargetValid(candidate, scanRadius))
            {
                continue;
            }

            float score = ScoreTarget(candidate);
            if (score < bestScore)
            {
                bestTarget = candidate;
                bestScore = score;
            }
        }

        if (currentTarget != null && IsTargetValid(currentTarget, scanRadius))
        {
            if (Time.time - targetLockedAt < minimumTargetLockTime
                || bestTarget == null
                || bestTarget == currentTarget
                || bestScore >= ScoreTarget(currentTarget) * switchTargetScoreRatio)
            {
                return;
            }
        }

        SetTarget(bestTarget);
    }

    // Chấm điểm thấp cho enemy gần và nằm gần tâm hướng nhìn của người chơi.
    private float ScoreTarget(CombatActor candidate)
    {
        Vector3 toTarget = candidate.AimPoint - actor.AimPoint;
        float distance = toTarget.magnitude;
        Camera camera = Camera.main;
        float anglePenalty = camera != null
            ? Vector3.Angle(camera.transform.forward, toTarget.normalized) / 90f
            : 0f;
        return distance + anglePenalty * 1.5f;
    }

    // Chuyển vòng khóa trực quan từ mục tiêu cũ sang mục tiêu mới.
    private void SetTarget(CombatActor newTarget)
    {
        if (currentTarget != null)
        {
            currentTarget.SetTargeted(false);
        }

        currentTarget = newTarget;
        targetLockedAt = Time.time;
        if (currentTarget != null)
        {
            currentTarget.SetTargeted(true);
        }
    }

    // Bắt đầu đòn đánh thường khi mục tiêu ở trong tầm, không bị tường che và companion không bận dùng chiêu.
    private void TryStartBasicAttack()
    {
        if (isCasting || basicAttackRoutine != null || Time.time < nextBasicAttackTime
            || !IsTargetValid(currentTarget, basicAttackRange))
        {
            return;
        }

        basicAttackRoutine = StartCoroutine(BasicAttackRoutine(currentTarget));
    }

    // Chờ windup ngắn rồi bắn projectile trắng vào mục tiêu đã khóa.
    private IEnumerator BasicAttackRoutine(CombatActor target)
    {
        yield return new WaitForSeconds(basicAttackWindup);
        if (IsTargetValid(target, basicAttackRange))
        {
            CombatProjectile.Launch(actor, target, basicAttackDamage, 5.5f, Color.white, 0.075f);
            nextBasicAttackTime = Time.time + basicAttackInterval;
        }

        basicAttackRoutine = null;
    }

    // Báo hướng chiêu bằng một đường cyan rồi phóng viên đạn mạnh hơn đánh thường.
    private IEnumerator CastSkillOneRoutine(CombatActor target)
    {
        isCasting = true;
        LineRenderer preview = CreateDirectionPreview(target.AimPoint, new Color(0.1f, 0.9f, 1f));
        yield return new WaitForSeconds(0.22f);
        if (preview != null)
        {
            Destroy(preview.gameObject);
        }

        if (IsTargetValid(target, skillOneRange))
        {
            CombatProjectile.Launch(actor, target, skillOneDamage, 8f, new Color(0.1f, 0.9f, 1f), 0.16f);
        }

        isCasting = false;
    }

    // Hiện vòng vàng trên sàn, chờ thời gian báo trước rồi gây sát thương mọi enemy bên trong.
    private IEnumerator CastSkillTwoRoutine(Vector3 center)
    {
        isCasting = true;
        LineRenderer area = CreateAreaPreview(center, skillTwoRadius, new Color(1f, 0.68f, 0.08f));
        yield return new WaitForSeconds(skillTwoDelay);

        foreach (CombatActor candidate in CombatActor.All)
        {
            if (candidate != null && candidate.IsAlive && candidate.Team == CombatTeam.Enemy
                && Vector3.Distance(candidate.transform.position, center) <= skillTwoRadius)
            {
                candidate.TakeDamage(skillTwoDamage);
            }
        }

        if (area != null)
        {
            Destroy(area.gameObject);
        }

        isCasting = false;
    }

    // Kiểm tra đội, khoảng cách, trạng thái sống và đường nhìn trước khi cho phép tấn công.
    private bool IsTargetValid(CombatActor candidate, float maximumRange)
    {
        if (candidate == null || !candidate.IsAlive || candidate.Team != CombatTeam.Enemy)
        {
            return false;
        }

        Vector3 delta = candidate.AimPoint - actor.AimPoint;
        if (delta.sqrMagnitude > maximumRange * maximumRange)
        {
            return false;
        }

        if (Physics.Raycast(actor.AimPoint, delta.normalized, out RaycastHit hit, delta.magnitude,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
        {
            return hit.transform == candidate.transform || hit.transform.IsChildOf(candidate.transform);
        }

        return true;
    }

    // Tạo đường báo hướng chiêu 1 trong không gian thế giới.
    private LineRenderer CreateDirectionPreview(Vector3 destination, Color color)
    {
        GameObject lineObject = new GameObject("Skill One Preview");
        LineRenderer line = lineObject.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.positionCount = 2;
        line.widthMultiplier = 0.035f;
        line.sharedMaterial = CombatActor.CreateMaterial(color);
        line.startColor = color;
        line.endColor = color;
        line.SetPosition(0, actor.AimPoint);
        line.SetPosition(1, destination);
        return line;
    }

    // Tạo vòng báo vùng ảnh hưởng của chiêu 2 trên mặt sàn.
    private static LineRenderer CreateAreaPreview(Vector3 center, float radius, Color color)
    {
        GameObject ringObject = new GameObject("Skill Two Area Preview");
        ringObject.transform.position = center + Vector3.up * 0.02f;
        LineRenderer ring = ringObject.AddComponent<LineRenderer>();
        ring.useWorldSpace = false;
        ring.loop = true;
        ring.positionCount = 56;
        ring.widthMultiplier = 0.055f;
        ring.sharedMaterial = CombatActor.CreateMaterial(color);
        ring.startColor = color;
        ring.endColor = color;
        for (int i = 0; i < ring.positionCount; i++)
        {
            float angle = i * Mathf.PI * 2f / ring.positionCount;
            ring.SetPosition(i, new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius));
        }

        return ring;
    }
}
