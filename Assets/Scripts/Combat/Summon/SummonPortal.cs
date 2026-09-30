using UnityEngine;

namespace XR124.Combat
{
    // Pháp trận / cổng không gian trên sàn. Hình là một quad nằm phẳng dùng shader XR124/MagicCircle (HLSL):
    // mở cổng = tăng _Open 0 → 1 (pháp trận "vẽ dần" từ tâm ra) kèm phóng nhẹ; đóng = giảm _Open rồi ẩn.
    public sealed class SummonPortal : MonoBehaviour
    {
        private static readonly int OpenId = Shader.PropertyToID("_Open");

        [Tooltip("Hình pháp trận (quad có material MagicCircle); được xoay chậm và phóng nhẹ khi mở.")]
        [SerializeField] private Transform visualRoot;
        [Tooltip("Particle phát khi mở cổng (tùy chọn).")]
        [SerializeField] private ParticleSystem openParticles;
        [SerializeField] private AudioSource openSound;
        [Min(0.01f)] [SerializeField] private float openSeconds = 0.8f;
        [Min(0.01f)] [SerializeField] private float closeSeconds = 0.6f;
        [Min(0f)] [SerializeField] private float spinDegreesPerSecond = 20f;
        [Tooltip("Tỉ lệ kích thước lúc bắt đầu mở (phóng dần lên 1).")]
        [Range(0f, 1f)] [SerializeField] private float startScale = 0.7f;

        [Header("Runtime (chỉ đọc)")]
        [SerializeField] private float openAmount;
        [SerializeField] private bool isOpening;

        private Vector3 fullScale = Vector3.one;
        private Renderer[] renderers;
        private MaterialPropertyBlock block;

        public float OpenSeconds => openSeconds;
        public float CloseSeconds => closeSeconds;

        // Gán hình pháp trận từ công cụ Editor.
        public void Configure(Transform visual)
        {
            visualRoot = visual;
        }

        // Lưu kích thước đầy đủ, lấy renderer để đẩy _Open qua MaterialPropertyBlock (không tạo bản sao material),
        // rồi đưa về trạng thái đóng. Không SetActive(false) ở đây vì Awake có thể chạy ngay trong Open().
        private void Awake()
        {
            if (visualRoot == null)
            {
                visualRoot = transform;
            }

            fullScale = visualRoot.localScale;
            renderers = visualRoot.GetComponentsInChildren<Renderer>(true);
            block = new MaterialPropertyBlock();
            ApplyAmount(0f);
        }

        // Mở cổng tại điểm sàn; cổng nằm phẳng, nhích lên 5 mm để không z-fighting với sàn.
        public void Open(Vector3 floorPoint)
        {
            transform.SetPositionAndRotation(floorPoint + Vector3.up * 0.005f, Quaternion.identity);
            gameObject.SetActive(true);
            isOpening = true;
            openAmount = 0f;
            ApplyAmount(0f);

            if (openParticles != null)
            {
                openParticles.Play(true);
            }

            if (openSound != null)
            {
                openSound.Play();
            }
        }

        // Bắt đầu đóng cổng; tự ẩn khi đóng hết.
        public void Close()
        {
            isOpening = false;
        }

        // Nội suy độ mở theo thời gian, xoay chậm pháp trận quanh trục đứng; đóng hết thì ẩn.
        private void Update()
        {
            float target = isOpening ? 1f : 0f;
            float duration = isOpening ? openSeconds : closeSeconds;
            openAmount = Mathf.MoveTowards(openAmount, target, Time.deltaTime / duration);
            ApplyAmount(openAmount);
            visualRoot.Rotate(0f, spinDegreesPerSecond * Time.deltaTime, 0f, Space.World);

            if (!isOpening && openAmount <= 0f)
            {
                gameObject.SetActive(false);
            }
        }

        // Đẩy độ mở (easing smoothstep) vào shader và phóng từ startScale lên kích thước đầy đủ.
        private void ApplyAmount(float amount)
        {
            float eased = amount * amount * (3f - 2f * amount);
            visualRoot.localScale = fullScale * Mathf.Lerp(startScale, 1f, eased);
            if (renderers == null)
            {
                return;
            }

            for (int i = 0; i < renderers.Length; i++)
            {
                renderers[i].GetPropertyBlock(block);
                block.SetFloat(OpenId, eased);
                renderers[i].SetPropertyBlock(block);
            }
        }
    }
}
