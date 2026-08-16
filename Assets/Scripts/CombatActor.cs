using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public enum CombatTeam
{
    Companion,
    Enemy
}

public sealed class CombatActor : MonoBehaviour
{
    private static readonly List<CombatActor> ActiveActors = new List<CombatActor>();

    [Header("Identity")]
    [SerializeField] private CombatTeam team;
    [SerializeField, Min(1f)] private float maximumHealth = 100f;
    [SerializeField] private bool destroyWhenDefeated;
    [SerializeField] private Color actorColor = Color.white;

    [Header("Runtime")]
    [SerializeField] private float currentHealth;
    [SerializeField] private bool isDefeated;

    private Transform healthFill;
    private GameObject targetIndicator;
    private Renderer bodyRenderer;
    private Color baseColor = Color.white;

    public static IReadOnlyList<CombatActor> All => ActiveActors;
    public CombatTeam Team => team;
    public bool IsAlive => !isDefeated && currentHealth > 0f;
    public float HealthNormalized => maximumHealth > 0f ? currentHealth / maximumHealth : 0f;
    public Vector3 AimPoint => transform.position + Vector3.up * 0.18f;

    // Đăng ký actor để các bộ điều khiển có thể tìm mục tiêu mà không quét toàn bộ scene mỗi khung hình.
    private void OnEnable()
    {
        if (!ActiveActors.Contains(this))
        {
            ActiveActors.Add(this);
        }
    }

    // Loại actor khỏi danh sách chiến đấu khi object bị tắt hoặc scene được đóng.
    private void OnDisable()
    {
        ActiveActors.Remove(this);
    }

    // Khởi tạo máu, màu gốc và các dấu hiệu trực quan gắn trực tiếp lên actor.
    private void Awake()
    {
        currentHealth = maximumHealth;
        bodyRenderer = GetComponentInChildren<Renderer>();
        if (bodyRenderer != null)
        {
            bodyRenderer.material = CreateMaterial(actorColor);
            baseColor = actorColor;
        }

        CreateHealthBar();
        CreateTargetIndicator();
        RefreshHealthBar();
    }

    // Nhận sát thương, cập nhật thanh máu và xử lý trạng thái bị hạ gục.
    public void TakeDamage(float damage)
    {
        if (!IsAlive || damage <= 0f)
        {
            return;
        }

        currentHealth = Mathf.Max(0f, currentHealth - damage);
        RefreshHealthBar();
        StartCoroutine(FlashDamage());

        if (currentHealth <= 0f)
        {
            Defeat();
        }
    }

    // Bật hoặc tắt vòng khóa mục tiêu dưới chân actor.
    public void SetTargeted(bool targeted)
    {
        if (targetIndicator != null)
        {
            targetIndicator.SetActive(targeted && IsAlive);
        }
    }

    // Cho enemy AI đổi màu báo trước đòn đánh nhưng luôn trả về màu gốc sau đó.
    public void SetTelegraph(bool active)
    {
        if (bodyRenderer != null)
        {
            bodyRenderer.material.color = active
                ? Color.Lerp(baseColor, new Color(1f, 0.18f, 0.08f), 0.7f)
                : baseColor;
        }
    }

    // Đặt thông số actor từ công cụ dựng prototype trong Editor.
    public void Configure(CombatTeam configuredTeam, float health, bool destroyOnDefeat, Color color)
    {
        team = configuredTeam;
        maximumHealth = Mathf.Max(1f, health);
        destroyWhenDefeated = destroyOnDefeat;
        actorColor = color;
        currentHealth = maximumHealth;
    }

    // Chớp sáng ngắn khi trúng đòn để người chơi nhận ra sát thương ngay cả khi không nhìn thanh máu.
    private IEnumerator FlashDamage()
    {
        if (bodyRenderer == null)
        {
            yield break;
        }

        bodyRenderer.material.color = Color.white;
        yield return new WaitForSeconds(0.08f);
        if (!isDefeated)
        {
            bodyRenderer.material.color = baseColor;
        }
    }

    // Khóa mọi tương tác chiến đấu và dọn enemy sau khi hiệu ứng kết thúc.
    private void Defeat()
    {
        isDefeated = true;
        SetTargeted(false);
        SetTelegraph(false);

        if (destroyWhenDefeated)
        {
            Destroy(gameObject, 0.25f);
        }
    }

    // Tạo thanh máu world-space bằng hai khối mỏng để không phụ thuộc Canvas hoặc overlay.
    private void CreateHealthBar()
    {
        GameObject barRoot = new GameObject("Health Bar");
        barRoot.transform.SetParent(transform, false);
        barRoot.transform.localPosition = new Vector3(0f, 1.15f, 0f);

        Transform background = CreateBarPart("Background", barRoot.transform, new Color(0.08f, 0.1f, 0.12f), 0.72f);
        healthFill = CreateBarPart("Fill", barRoot.transform,
            team == CombatTeam.Enemy ? new Color(1f, 0.2f, 0.16f) : new Color(0.1f, 0.9f, 0.65f), 0.68f);
        background.localPosition = new Vector3(0f, 0f, 0.01f);
        healthFill.localPosition = new Vector3(0f, 0f, -0.01f);
    }

    // Tạo một phần thanh máu có shader unlit để dễ đọc trong cả passthrough và môi trường tối.
    private static Transform CreateBarPart(string objectName, Transform parent, Color color, float width)
    {
        GameObject part = GameObject.CreatePrimitive(PrimitiveType.Cube);
        part.name = objectName;
        part.transform.SetParent(parent, false);
        part.transform.localScale = new Vector3(width, 0.07f, 0.025f);
        Destroy(part.GetComponent<Collider>());

        Renderer renderer = part.GetComponent<Renderer>();
        renderer.material = CreateMaterial(color);
        return part.transform;
    }

    // Tạo vòng trắng dưới enemy đang được companion khóa làm mục tiêu.
    private void CreateTargetIndicator()
    {
        targetIndicator = new GameObject("Target Indicator");
        targetIndicator.transform.SetParent(transform, false);
        targetIndicator.transform.localPosition = new Vector3(0f, -0.48f, 0f);

        LineRenderer ring = targetIndicator.AddComponent<LineRenderer>();
        ring.useWorldSpace = false;
        ring.loop = true;
        ring.positionCount = 40;
        ring.widthMultiplier = 0.035f;
        ring.sharedMaterial = CreateMaterial(new Color(0.95f, 0.98f, 1f));
        ring.startColor = Color.white;
        ring.endColor = Color.white;
        for (int i = 0; i < ring.positionCount; i++)
        {
            float angle = i * Mathf.PI * 2f / ring.positionCount;
            ring.SetPosition(i, new Vector3(Mathf.Cos(angle) * 0.48f, 0f, Mathf.Sin(angle) * 0.48f));
        }

        targetIndicator.SetActive(false);
    }

    // Thay đổi chiều dài thanh máu nhưng giữ mép trái cố định.
    private void RefreshHealthBar()
    {
        if (healthFill == null)
        {
            return;
        }

        float normalized = HealthNormalized;
        Vector3 scale = healthFill.localScale;
        scale.x = 0.68f * normalized;
        healthFill.localScale = scale;
        healthFill.localPosition = new Vector3(-0.34f * (1f - normalized), 0f, -0.01f);
    }

    // Tạo material runtime dùng chung quy tắc shader với URP và có fallback khi shader không tồn tại.
    public static Material CreateMaterial(Color color)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
        {
            shader = Shader.Find("Unlit/Color");
        }

        if (shader == null)
        {
            shader = Shader.Find("Sprites/Default");
        }

        Material material = new Material(shader);
        material.color = color;
        return material;
    }
}
