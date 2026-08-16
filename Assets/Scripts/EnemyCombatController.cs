using System.Collections;
using UnityEngine;

public sealed class EnemyCombatController : MonoBehaviour
{
    [Header("Enemy Behaviour")]
    [SerializeField, Min(0f)] private float moveSpeed = 0.65f;
    [SerializeField, Min(0.1f)] private float attackRange = 0.75f;
    [SerializeField, Min(0.1f)] private float attackInterval = 1.35f;
    [SerializeField, Min(0f)] private float attackWindup = 0.38f;
    [SerializeField, Min(0f)] private float attackDamage = 5f;

    private CombatActor actor;
    private CombatActor companion;
    private float nextAttackTime;
    private bool isAttacking;

    // Lấy CombatActor của chính enemy khi bắt đầu chạy.
    private void Awake()
    {
        actor = GetComponent<CombatActor>();
    }

    // Tìm companion, áp sát theo mặt phẳng và phản công khi vào đúng khoảng cách.
    private void Update()
    {
        if (actor == null || !actor.IsAlive)
        {
            return;
        }

        if (companion == null || !companion.IsAlive)
        {
            companion = FindCompanion();
        }

        if (companion == null || isAttacking)
        {
            return;
        }

        Vector3 toCompanion = companion.transform.position - transform.position;
        toCompanion.y = 0f;
        if (toCompanion.magnitude > attackRange)
        {
            Vector3 movement = toCompanion.normalized * moveSpeed * Time.deltaTime;
            transform.position += movement;
            transform.forward = Vector3.Slerp(transform.forward, toCompanion.normalized, Time.deltaTime * 8f);
        }
        else if (Time.time >= nextAttackTime)
        {
            StartCoroutine(AttackRoutine());
        }
    }

    // Báo đỏ trước khi đánh để người chơi có thời gian kéo companion ra khỏi vùng nguy hiểm.
    private IEnumerator AttackRoutine()
    {
        isAttacking = true;
        actor.SetTelegraph(true);
        yield return new WaitForSeconds(attackWindup);
        actor.SetTelegraph(false);

        if (companion != null && companion.IsAlive
            && Vector3.Distance(transform.position, companion.transform.position) <= attackRange + 0.15f)
        {
            companion.TakeDamage(attackDamage);
        }

        nextAttackTime = Time.time + attackInterval;
        isAttacking = false;
    }

    // Tìm actor đồng minh đầu tiên còn sống trong danh sách chiến đấu.
    private static CombatActor FindCompanion()
    {
        foreach (CombatActor candidate in CombatActor.All)
        {
            if (candidate != null && candidate.IsAlive && candidate.Team == CombatTeam.Companion)
            {
                return candidate;
            }
        }

        return null;
    }

    // Cho công cụ Editor tạo nhiều loại enemy với tốc độ và sát thương khác nhau.
    public void Configure(float configuredMoveSpeed, float configuredDamage, float configuredAttackInterval)
    {
        moveSpeed = configuredMoveSpeed;
        attackDamage = configuredDamage;
        attackInterval = configuredAttackInterval;
    }
}
