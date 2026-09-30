using UnityEngine;

namespace XR124.Combat
{
    // Chọn hình hiển thị cho từng tay: có dữ liệu bàn tay (hand tracking, hoặc Capsense khi OVRManager bật
    // Controller Driven Hand Poses = Natural) thì ẩn model tay cầm để thấy ngón tay kết ấn; không có dữ liệu bàn tay
    // (ví dụ XR Simulator ở chế độ Controller, hoặc runtime không hỗ trợ Capsense) thì hiện model tay cầm để tay không "biến mất".
    public sealed class HandControllerVisualSwitcher : MonoBehaviour
    {
        [Tooltip("Hình tay cầm trái/phải của rig ISDK (OVRControllerVisualLeft/Right); để trống thì tự tìm theo tên.")]
        [SerializeField] private GameObject leftControllerVisual;
        [SerializeField] private GameObject rightControllerVisual;
        [Tooltip("Bộ đọc xương tay trái/phải; để trống thì tự tìm theo SkeletonType của OVRSkeleton.")]
        [SerializeField] private HandSkeletonReader leftHand;
        [SerializeField] private HandSkeletonReader rightHand;
        [Tooltip("Mất dữ liệu bàn tay liên tục bao lâu (giây) mới hiện lại tay cầm, tránh nhấp nháy khi tracking chập chờn.")]
        [Min(0f)] [SerializeField] private float showControllerDelay = 0.3f;

        [Header("Runtime (chỉ đọc)")]
        [SerializeField] private bool leftShowsHand;
        [SerializeField] private bool rightShowsHand;

        private float leftLostTime;
        private float rightLostTime;

        // Tự tìm hình tay cầm và bộ đọc xương tay nếu chưa gán.
        private void Start()
        {
            if (leftControllerVisual == null) leftControllerVisual = FindSceneObject("OVRControllerVisualLeft");
            if (rightControllerVisual == null) rightControllerVisual = FindSceneObject("OVRControllerVisualRight");

            HandSkeletonReader[] readers = FindObjectsByType<HandSkeletonReader>(FindObjectsSortMode.None);
            for (int i = 0; i < readers.Length; i++)
            {
                OVRSkeleton skeleton = readers[i].Skeleton;
                if (skeleton == null)
                {
                    continue;
                }

                OVRSkeleton.SkeletonType type = skeleton.GetSkeletonType();
                bool isLeft = type == OVRSkeleton.SkeletonType.XRHandLeft || type == OVRSkeleton.SkeletonType.HandLeft;
                if (isLeft && leftHand == null) leftHand = readers[i];
                else if (!isLeft && rightHand == null) rightHand = readers[i];
            }
        }

        // Mỗi khung hình: tay có dữ liệu bàn tay → ẩn tay cầm; mất dữ liệu quá showControllerDelay → hiện tay cầm.
        private void Update()
        {
            leftShowsHand = UpdateSide(leftHand, leftControllerVisual, ref leftLostTime);
            rightShowsHand = UpdateSide(rightHand, rightControllerVisual, ref rightLostTime);
        }

        // Xử lý một bên tay; trả về true nếu đang hiển thị bàn tay.
        private bool UpdateSide(HandSkeletonReader hand, GameObject controllerVisual, ref float lostTime)
        {
            bool handValid = hand != null && hand.IsValid;
            if (handValid)
            {
                lostTime = 0f;
            }
            else
            {
                lostTime += Time.deltaTime;
            }

            bool showController = !handValid && lostTime >= showControllerDelay;
            if (controllerVisual != null && controllerVisual.activeSelf != showController)
            {
                controllerVisual.SetActive(showController);
            }

            return !showController;
        }

        // Tìm GameObject trong scene theo tên, kể cả đang tắt (Find thường bỏ qua object tắt).
        private static GameObject FindSceneObject(string objectName)
        {
            Transform[] all = FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i].name == objectName)
                {
                    return all[i].gameObject;
                }
            }

            return null;
        }
    }
}
