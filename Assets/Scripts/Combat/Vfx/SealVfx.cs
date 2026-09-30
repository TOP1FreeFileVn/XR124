using UnityEngine;

namespace XR124.Combat
{
    // Hiệu ứng tỏa ra mỗi khi kết ấn, dùng shader HLSL XR124/SealAura trên các quad dựng sẵn (pool, không Instantiate lúc chơi):
    //   A – Kiếm Chỉ: vòng kiếm khí ở đầu ngón trỏ phải, mặt vòng vuông góc hướng chỉ.
    //   D – Thuẫn Chưởng: khiên lục giác trước lòng bàn tay phải, quay theo hướng nhìn.
    //   H – Tâm Ấn: hoa sen trước ngực, quay về phía mắt để thấy được ở góc nhìn thứ nhất.
    //   Ultimate: pháp ấn lớn trước mặt.
    // Hiệu ứng bám theo xương tay của nhân vật full body (PlayerBodyAvatar) nếu có, không thì theo anchor tay của OVRCameraRig.
    public sealed class SealVfx : MonoBehaviour
    {
        private static readonly int SealTypeId = Shader.PropertyToID("_SealType");
        private static readonly int ProgressId = Shader.PropertyToID("_Progress");

        private enum Kind { Sword = 0, Shield = 1, Heart = 2, Ultimate = 3 }

        [SerializeField] private Material auraMaterial;
        [SerializeField] private SealComboCaster caster;
        [Tooltip("Số hiệu ứng có thể hiện cùng lúc.")]
        [Min(1)] [SerializeField] private int poolSize = 6;

        [Header("Thời lượng (giây) và cỡ (m)")]
        [SerializeField] private Vector2 swordTimeSize = new Vector2(0.9f, 0.45f);
        [SerializeField] private Vector2 shieldTimeSize = new Vector2(1.2f, 0.7f);
        [SerializeField] private Vector2 heartTimeSize = new Vector2(1.4f, 0.55f);
        [SerializeField] private Vector2 ultimateTimeSize = new Vector2(1.6f, 1.3f);

        private sealed class Effect
        {
            public GameObject go;
            public MeshRenderer renderer;
            public Kind kind;
            public float age;
            public float duration;
            public float size;
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

        // Mỗi ấn một kiểu hiệu ứng riêng.
        private void HandleSeal(SealType seal)
        {
            switch (seal)
            {
                case SealType.A: Spawn(Kind.Sword, swordTimeSize); break;
                case SealType.D: Spawn(Kind.Shield, shieldTimeSize); break;
                case SealType.H: Spawn(Kind.Heart, heartTimeSize); break;
            }
        }

        // Ultimate dùng hiệu ứng lớn trước mặt.
        private void HandleUltimate(ActionResult result)
        {
            Spawn(Kind.Ultimate, ultimateTimeSize);
        }

        // Lấy quad rảnh (hoặc quad sắp hết nhất nếu đầy) và bắt đầu hiệu ứng.
        private void Spawn(Kind kind, Vector2 timeSize)
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
                if (progress >= 1f)
                {
                    effect.go.SetActive(false);
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
