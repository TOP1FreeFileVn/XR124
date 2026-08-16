using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class CombatPrototypeBuilder
{
    private const string PrototypeRootName = "Combat Prototype";

    // Tạo companion combat cùng ba enemy mẫu xung quanh Controlled Object hiện tại.
    [MenuItem("Tools/XR124/Create Combat Prototype")]
    public static void CreatePrototype()
    {
        PocketControl pocketControl = Object.FindFirstObjectByType<PocketControl>(FindObjectsInactive.Include);
        if (pocketControl == null || pocketControl.controlledObject == null)
        {
            Debug.LogError("PocketControl hoặc Controlled Object chưa được gán.");
            return;
        }

        GameObject existing = GameObject.Find(PrototypeRootName);
        if (existing != null)
        {
            Undo.DestroyObjectImmediate(existing);
        }

        GameObject root = new GameObject(PrototypeRootName);
        Undo.RegisterCreatedObjectUndo(root, "Create Combat Prototype");

        ConfigureCompanion(pocketControl);
        Vector3 center = pocketControl.controlledObject.transform.position;
        CreateEnemy(root.transform, "Enemy Scout", center + new Vector3(2.4f, 0f, 1.3f),
            new Color(1f, 0.18f, 0.12f), 70f, 0.85f, 4f, 1.15f);
        CreateEnemy(root.transform, "Enemy Guard", center + new Vector3(-2.8f, 0f, 2.1f),
            new Color(0.78f, 0.12f, 0.32f), 120f, 0.48f, 8f, 1.65f);
        CreateEnemy(root.transform, "Enemy Caster", center + new Vector3(0.8f, 0f, 3.8f),
            new Color(0.62f, 0.2f, 0.95f), 85f, 0.62f, 6f, 1.4f);

        Selection.activeGameObject = root;
        EditorSceneManager.MarkSceneDirty(root.scene);
        EditorSceneManager.SaveOpenScenes();
        Debug.Log("Created combat prototype with auto attack, two skills and three enemies.", root);
    }

    // Gắn hệ combat lên Controlled Object và làm companion đủ lớn để quan sát trong kính.
    private static void ConfigureCompanion(PocketControl pocketControl)
    {
        GameObject companion = pocketControl.controlledObject;
        companion.transform.localScale = Vector3.one * 0.34f;

        CombatActor actor = companion.GetComponent<CombatActor>();
        if (actor == null)
        {
            actor = companion.AddComponent<CombatActor>();
        }
        actor.Configure(CombatTeam.Companion, 250f, false, new Color(0.08f, 0.82f, 0.9f));

        CompanionCombatController combat = companion.GetComponent<CompanionCombatController>();
        if (combat == null)
        {
            combat = companion.AddComponent<CompanionCombatController>();
        }

        SerializedObject serialized = new SerializedObject(combat);
        serialized.FindProperty("actor").objectReferenceValue = actor;
        serialized.FindProperty("movementController").objectReferenceValue = pocketControl;
        serialized.FindProperty("skillHand").objectReferenceValue = FindOppositeHand(pocketControl.skeleton);
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    // Tìm OVRHand đối diện tay joystick để kỹ năng và di chuyển không tranh cùng một dáng tay.
    private static OVRHand FindOppositeHand(OVRSkeleton movementSkeleton)
    {
        OVRSkeleton.SkeletonType movementType = movementSkeleton != null
            ? movementSkeleton.GetSkeletonType()
            : OVRSkeleton.SkeletonType.None;
        bool movementUsesRightHand = movementType == OVRSkeleton.SkeletonType.XRHandRight
            || movementType == OVRSkeleton.SkeletonType.HandRight;
        bool movementUsesLeftHand = movementType == OVRSkeleton.SkeletonType.XRHandLeft
            || movementType == OVRSkeleton.SkeletonType.HandLeft;

        OVRHand fallback = null;
        OVRHand[] hands = Object.FindObjectsByType<OVRHand>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (OVRHand hand in hands)
        {
            fallback ??= hand;
            OVRSkeleton skeleton = hand.GetComponent<OVRSkeleton>();
            if (skeleton == null)
            {
                continue;
            }

            OVRSkeleton.SkeletonType type = skeleton.GetSkeletonType();
            bool isRightHand = type == OVRSkeleton.SkeletonType.XRHandRight
                || type == OVRSkeleton.SkeletonType.HandRight;
            bool isLeftHand = type == OVRSkeleton.SkeletonType.XRHandLeft
                || type == OVRSkeleton.SkeletonType.HandLeft;
            if ((movementUsesRightHand && isLeftHand)
                || (movementUsesLeftHand && isRightHand))
            {
                return hand;
            }
        }

        return fallback;
    }

    // Tạo enemy có thân capsule, hai mắt, máu và AI độc lập.
    private static void CreateEnemy(
        Transform parent,
        string enemyName,
        Vector3 position,
        Color color,
        float health,
        float speed,
        float damage,
        float interval)
    {
        GameObject enemy = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        enemy.name = enemyName;
        enemy.transform.SetParent(parent, false);
        enemy.transform.position = new Vector3(position.x, position.y + 0.42f, position.z);
        enemy.transform.localScale = new Vector3(0.44f, 0.42f, 0.44f);

        CreateEye(enemy.transform, new Vector3(-0.16f, 0.28f, 0.38f));
        CreateEye(enemy.transform, new Vector3(0.16f, 0.28f, 0.38f));

        CombatActor actor = enemy.AddComponent<CombatActor>();
        actor.Configure(CombatTeam.Enemy, health, true, color);
        EnemyCombatController controller = enemy.AddComponent<EnemyCombatController>();
        controller.Configure(speed, damage, interval);
    }

    // Tạo mắt sáng để hướng nhìn của enemy dễ nhận ra khi nó xoay về companion.
    private static void CreateEye(Transform parent, Vector3 localPosition)
    {
        GameObject eye = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        eye.name = "Eye";
        eye.transform.SetParent(parent, false);
        eye.transform.localPosition = localPosition;
        eye.transform.localScale = Vector3.one * 0.12f;
        Object.DestroyImmediate(eye.GetComponent<Collider>());
        eye.GetComponent<Renderer>().sharedMaterial = CombatActor.CreateMaterial(Color.white);
    }
}
