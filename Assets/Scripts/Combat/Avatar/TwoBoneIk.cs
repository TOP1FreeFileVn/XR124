using UnityEngine;

namespace XR124.Combat
{
    // IK hai đốt dùng chung cho tay nhân vật (vai → khuỷu → cổ tay).
    public static class TwoBoneIk
    {
        // Tính vị trí khuỷu trên mặt phẳng chứa vai–mục tiêu–hướng gợi ý (định lý cos), xoay cánh tay trên rồi cẳng tay để
        // cổ tay chạm goal; mục tiêu xa hơn chiều dài tay thì duỗi thẳng về phía đó. hint: hướng khuỷu nên gập tới.
        public static void Solve(Transform upper, Transform lower, Transform wrist, Vector3 goal, Vector3 hint)
        {
            if (upper == null || lower == null || wrist == null)
            {
                return;
            }

            Vector3 a = upper.position;
            float upperLength = Vector3.Distance(a, lower.position);
            float lowerLength = Vector3.Distance(lower.position, wrist.position);
            Vector3 toGoal = goal - a;
            float reach = Mathf.Clamp(toGoal.magnitude, 0.01f, upperLength + lowerLength - 0.001f);
            Vector3 dir = toGoal.sqrMagnitude > 1e-6f ? toGoal.normalized : upper.forward;

            float cosShoulder = Mathf.Clamp((upperLength * upperLength + reach * reach - lowerLength * lowerLength) / (2f * upperLength * reach), -1f, 1f);
            Vector3 bend = Vector3.ProjectOnPlane(hint, dir);
            if (bend.sqrMagnitude < 1e-6f)
            {
                bend = Vector3.ProjectOnPlane(Vector3.down, dir);
            }

            bend.Normalize();
            Vector3 elbow = a + dir * (upperLength * cosShoulder) + bend * (upperLength * Mathf.Sqrt(1f - cosShoulder * cosShoulder));

            upper.rotation = Quaternion.FromToRotation(lower.position - a, elbow - a) * upper.rotation;
            lower.rotation = Quaternion.FromToRotation(wrist.position - lower.position, goal - lower.position) * lower.rotation;
        }
    }
}
