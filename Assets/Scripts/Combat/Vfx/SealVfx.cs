using UnityEngine;

namespace XR124.Combat
{
    // Hiệu ứng tỏa ra mỗi khi kết ấn, dùng shader HLSL XR124/SealAura trên các quad dựng sẵn (pool, không Instantiate lúc chơi):
    //   A – Kiếm Chỉ: vòng kiếm khí ở đầu ngón trỏ phải, mặt vòng vuông góc hướng chỉ.
    //   D – Thuẫn Chưởng: khiên lục giác trước lòng bàn tay phải, quay theo hướng nhìn.
    //   H – Tâm Ấn: hoa sen trước ngực, quay về phía mắt để thấy được ở góc nhìn thứ nhất.
    //   Ultimate: pháp ấn lớn trước mặt.
    // Đồng thời ấn kí vừa dùng hiện trên đầu pet của người chơi (quay về camera, bay lên nhẹ) để thấy pet đang nhận lệnh gì.
    // Hiệu ứng bám theo xương tay của nhân vật full body (PlayerBodyAvatar) nếu có, không thì theo anchor tay của OVRCameraRig.
    public sealed class SealVfx : MonoBehaviour
    {
        private static readonly int SealTypeId = Shader.PropertyToID("_SealType");
        private static readonly int ProgressId = Shader.PropertyToID("_Progress");

        private enum Kind { Sword = 0, Shield = 1, Heart = 2, Ultimate = 3 }

        [SerializeField] private Material auraMaterial;
        [SerializeField] private SealComboCaster caster;
        [Tooltip("Số hiệu ứng có thể hiện cùng lúc.")]
        [Min(1)] [SerializeField] private int poolSize = 10;

        [Header("Thời lượng (giây) và cỡ (m)")]
        [SerializeField] private Vector2 swordTimeSize = new Vector2(0.5f, 0.45f);
        [SerializeField] private Vector2 shieldTimeSize = new Vector2(0.6f, 0.7f);
        [SerializeField] private Vector2 heartTimeSize = new Vector2(0.7f, 0.55f);
        [SerializeField] private Vector2 ultimateTimeSize = new Vector2(0.9f, 1.3f);

        [Header("Ấn kí trên đầu pet")]
        [SerializeField] private bool showOverPet = true;
        [SerializeField] private Vector2 overPetTimeSize = new Vector2(0.9f, 0.5f);
        [Tooltip("Độ cao trên đỉnh thân pet (m, cộng PetDefinition.bodyHeight); cao hơn HUD máu (0.4 m) để không che thanh máu.")]
        [SerializeField] private float overPetHeight = 0.75f;
        [Tooltip("Quãng bay lên (m) trong suốt thời gian hiện.")]
        [SerializeField] private float overPetRise = 0.12f;

        private sealed class Effect
        {
            public GameObject go;
            public MeshRenderer renderer;
            public Kind kind;
            public float age;
            public float duration;
            public float size;
            // true = hiệu ứng nổi trên đầu pet thay vì bám tay người chơi.
            public bool overPet;
            public PetCombatant pet;
        }

        private Effect[] pool;
        private MaterialPropertyBlock block;
        private Transform head;
        private Transform rightAnchor;
        private Transform rightIndexTip, rightIndexProximal, rightWrist;

        // Gán material và bộ ghép ấn từ công cụ Editor.
        public void Configure(Material material, SealComboCaster sealCaster)
        {
            auraMaterial = material;
            caster = sealCaster;
        }

        // Dựng pool quad ẩn, tìm camera/anchor tay và xương tay nhân vật, nối sự kiện kết ấn.
        private void Start()
        {
            block = new MaterialPropertyBlock();
            pool = new Effect[poolSize];
            for (int i = 0; i < poolSize; i++)
            {
                GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                quad.name = "SealAura_" + i;
                Destroy(quad.GetComponent<Collider>());
                quad.transform.SetParent(transform, false);
                MeshRenderer renderer = quad.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = auraMaterial;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                quad.SetActive(false);
                pool[i] = new Effect { go = quad, renderer = renderer };
            }

            OVRCameraRig rig = FindFirstObjectByType<OVRCameraRig>();
            head = rig != null ? rig.centerEyeAnchor : (Camera.main != null ? Camera.main.transform : null);
            rightAnchor = rig != null ? rig.rightHandAnchor : null;

            GameObject avatar = GameObject.Find("PlayerBodyAvatar");
            if (avatar != null)
            {
                foreach (Transform bone in avatar.GetComponentsInChildren<Transform>(true))
                {
                    if (bone.name == "RightHandIndexTip") rightIndexTip = bone;
                    else if (bone.name == "RightHandIndexProximal") rightIndexProximal = bone;
                    else if (bone.name == "RightHandWrist") rightWrist = bone;
                }
            }

            if (caster == null) caster = FindFirstObjectByType<SealComboCaster>();
            if (caster != null)
            {
                caster.SealQueued += HandleSeal;
                caster.UltimateCast += HandleUltimate;
            }
        }

        // Hủy nối sự kiện.
        private void OnDestroy()
        {
            if (caster != null)
            {
                caster.SealQueued -= HandleSeal;
                caster.UltimateCast -= HandleUltimate;
            }
        }

        // Mỗi ấn một kiểu hiệu ứng riêng ở tay người chơi, kèm ấn kí cùng kiểu trên đầu pet.
        private void HandleSeal(SealType seal)
        {
            switch (seal)
            {
                case SealType.A: Spawn(Kind.Sword, swordTimeSize); SpawnOverPet(Kind.Sword); break;
                case SealType.D: Spawn(Kind.Shield, shieldTimeSize); SpawnOverPet(Kind.Shield); break;
                case SealType.H: Spawn(Kind.Heart, heartTimeSize); SpawnOverPet(Kind.Heart); break;
            }
        }

        // Ultimate dùng hiệu ứng lớn trước mặt, kèm pháp ấn Ultimate trên đầu pet.
        private void HandleUltimate(ActionResult result)
        {
            Spawn(Kind.Ultimate, ultimateTimeSize);
            SpawnOverPet(Kind.Ultimate);
        }

        // Hiện ấn kí trên đầu pet đang nhận lệnh (pet của SealComboCaster); bỏ qua nếu tắt tính năng hoặc chưa triệu hồi pet.
        private void SpawnOverPet(Kind kind)
        {
            PetCombatant pet = caster != null ? caster.Pet : null;
            if (!showOverPet || pet == null || !pet.gameObject.activeInHierarchy)
            {
                return;
            }

            Spawn(kind, overPetTimeSize, pet);
        }

        // Lấy quad rảnh (hoặc quad sắp hết nhất nếu đầy) và bắt đầu hiệu ứng; overPet khác null thì hiệu ứng nổi trên đầu pet.
        private void Spawn(Kind kind, Vector2 timeSize, PetCombatant overPet = null)
        {
            if (pool == null || auraMaterial == null)
            {
                return;
            }

            Effect chosen = pool[0];
            for (int i = 0; i < pool.Length; i++)
            {
                if (!pool[i].go.activeSelf)
                {
                    chosen = pool[i];
                    break;
                }

                if (pool[i].age / pool[i].duration > chosen.age / chosen.duration)
                {
                    chosen = pool[i];
                }
            }

            chosen.kind = kind;
            chosen.age = 0f;
            chosen.duration = Mathf.Max(0.1f, timeSize.x);
            chosen.size = timeSize.y;
            chosen.overPet = overPet != null;
            chosen.pet = overPet;
            chosen.go.SetActive(true);
            Place(chosen);
        }

        // LateUpdate (sau IK/diễn ấn của nhân vật): tăng tuổi, đẩy tiến trình vào shader, bám theo tay; hết giờ thì ẩn.
        private void LateUpdate()
        {
            if (pool == null)
            {
                return;
            }

            for (int i = 0; i < pool.Length; i++)
            {
                Effect effect = pool[i];
                if (!effect.go.activeSelf)
                {
                    continue;
                }

                effect.age += Time.deltaTime;
                float progress = effect.age / effect.duration;
                // Hết giờ, hoặc pet mang ấn kí đã bị hủy/ẩn (thu hồi, thua) thì tắt hiệu ứng.
                bool petGone = effect.overPet && (effect.pet == null || !effect.pet.gameObject.activeInHierarchy);
                if (progress >= 1f || petGone)
                {
                    effect.go.SetActive(false);
                    effect.overPet = false;
                    effect.pet = null;
                    continue;
                }

                Place(effect);
                block.SetFloat(SealTypeId, (float)effect.kind);
                block.SetFloat(ProgressId, progress);
                effect.renderer.SetPropertyBlock(block);
            }
        }

        // Đặt vị trí/hướng/cỡ quad theo kiểu hiệu ứng (quad vẽ hai mặt nên chỉ cần pháp tuyến đúng trục).
        private void Place(Effect effect)
        {
            if (head == null)
            {
                return;
            }

            Vector3 forward = Vector3.ProjectOnPlane(head.forward, Vector3.up);
            forward = forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.forward;
            Vector3 position;
            Vector3 normal;
            if (effect.overPet)
            {
                // Trên đầu pet: theo chiều cao thân trong PetDefinition (TransformPoint tính cả scale pet), bay lên dần,
                // mặt quad quay về mắt người chơi như billboard.
                float bodyHeight = effect.pet.Definition != null ? effect.pet.Definition.bodyHeight : 0.5f;
                float rise = overPetRise * Mathf.Clamp01(effect.age / effect.duration);
                position = effect.pet.transform.TransformPoint(new Vector3(0f, bodyHeight, 0f)) + Vector3.up * (overPetHeight + rise);
                normal = position - head.position;
                normal = normal.sqrMagnitude > 0.0001f ? normal.normalized : forward;
                effect.go.transform.SetPositionAndRotation(position, Quaternion.LookRotation(normal, Vector3.up));
                effect.go.transform.localScale = Vector3.one * effect.size;
                return;
            }

            switch (effect.kind)
            {
                case Kind.Sword:
                    if (rightIndexTip != null && rightIndexProximal != null)
                    {
                        normal = (rightIndexTip.position - rightIndexProximal.position).normalized;
                        position = rightIndexTip.position + normal * 0.03f;
                    }
                    else
                    {
                        normal = rightAnchor != null ? rightAnchor.forward : forward;
                        position = (rightAnchor != null ? rightAnchor.position : head.position + forward * 0.4f) + normal * 0.12f;
                    }
                    break;
                case Kind.Shield:
                    Vector3 hand = rightWrist != null ? rightWrist.position : (rightAnchor != null ? rightAnchor.position : head.position);
                    position = hand + forward * 0.15f + Vector3.up * 0.05f;
                    normal = forward;
                    break;
                case Kind.Heart:
                    position = head.position - Vector3.up * 0.38f + forward * 0.22f;
                    normal = (position - head.position).normalized;
                    break;
                default:
                    position = head.position + forward * 0.65f - Vector3.up * 0.12f;
                    normal = forward;
                    break;
            }

            effect.go.transform.SetPositionAndRotation(position, Quaternion.LookRotation(normal, Vector3.up));
            effect.go.transform.localScale = Vector3.one * effect.size;
        }
    }
}
