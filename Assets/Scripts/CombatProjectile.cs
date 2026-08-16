using UnityEngine;

public sealed class CombatProjectile : MonoBehaviour
{
    private CombatActor source;
    private CombatActor target;
    private float damage;
    private float speed;
    private float remainingLifetime;

    // Tạo một projectile có hình ảnh thật để quan sát rõ đường bay của đòn đánh hoặc kỹ năng.
    public static void Launch(
        CombatActor sourceActor,
        CombatActor targetActor,
        float projectileDamage,
        float projectileSpeed,
        Color color,
        float size)
    {
        if (sourceActor == null || targetActor == null || !targetActor.IsAlive)
        {
            return;
        }

        GameObject projectileObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        projectileObject.name = "Combat Projectile";
        projectileObject.transform.position = sourceActor.AimPoint;
        projectileObject.transform.localScale = Vector3.one * size;
        Destroy(projectileObject.GetComponent<Collider>());
        projectileObject.GetComponent<Renderer>().material = CombatActor.CreateMaterial(color);

        CombatProjectile projectile = projectileObject.AddComponent<CombatProjectile>();
        projectile.source = sourceActor;
        projectile.target = targetActor;
        projectile.damage = projectileDamage;
        projectile.speed = projectileSpeed;
        projectile.remainingLifetime = 4f;
    }

    // Bay về điểm ngắm hiện tại của mục tiêu và gây sát thương khi đến đủ gần.
    private void Update()
    {
        remainingLifetime -= Time.deltaTime;
        if (remainingLifetime <= 0f || source == null || target == null || !target.IsAlive)
        {
            Destroy(gameObject);
            return;
        }

        Vector3 destination = target.AimPoint;
        float step = speed * Time.deltaTime;
        transform.position = Vector3.MoveTowards(transform.position, destination, step);
        if ((transform.position - destination).sqrMagnitude <= 0.015f)
        {
            target.TakeDamage(damage);
            Destroy(gameObject);
        }
    }
}
