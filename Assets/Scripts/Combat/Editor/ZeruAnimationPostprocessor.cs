using UnityEditor;
using UnityEngine;

namespace XR124.Combat.EditorTools
{
    // Sửa lỗi chân Zeru "to rồi bé": rig Auto-Rig Pro bake độ co giãn chân (IK stretch) thành localPosition/localScale
    // của các đốt chân, nên đoạn cẳng chân dài/ngắn liên tục (idle lệch tới ~10 cm). Khi import Zeru.fbx, bỏ các kênh
    // vị trí/scale của những đốt nằm dưới c_thigh_b để chân giữ độ dài lúc bind; kênh xoay vẫn giữ nguyên.
    public sealed class ZeruAnimationPostprocessor : AssetPostprocessor
    {
        private const string ZeruFbx = "Assets/Art/Pets/Zeru/Zeru.fbx";

        // Tên (tiền tố) các đốt chân bị co giãn; áp dụng cả chân trước (_dupli_001) lẫn chân sau, trái/phải.
        private static readonly string[] StretchBonePrefixes =
        {
            "thigh_twist", "thigh_stretch", "leg_stretch", "leg_twist", "foot", "toes"
        };

        // Tăng số này khi đổi luật để Unity import lại Zeru.fbx.
        public override uint GetVersion()
        {
            return 1;
        }

        // Unity gọi cho từng clip sau khi import model; chỉ xử lý Zeru.fbx.
        private void OnPostprocessAnimation(GameObject root, AnimationClip clip)
        {
            if (assetPath != ZeruFbx)
            {
                return;
            }

            int removed = 0;
            EditorCurveBinding[] bindings = AnimationUtility.GetCurveBindings(clip);
            for (int i = 0; i < bindings.Length; i++)
            {
                EditorCurveBinding binding = bindings[i];
                bool isPositionOrScale = binding.propertyName.StartsWith("m_LocalPosition") || binding.propertyName.StartsWith("m_LocalScale");
                if (isPositionOrScale && IsStretchBone(binding.path))
                {
                    AnimationUtility.SetEditorCurve(clip, binding, null);
                    removed++;
                }
            }

            if (removed > 0)
            {
                Debug.Log($"[Art] Zeru/{clip.name}: bỏ {removed} kênh vị trí/scale ở đốt chân để chân không co giãn.");
            }
        }

        // Đốt cuối trong đường dẫn có tên bắt đầu bằng một tiền tố co giãn không (c_thigh_b gắn vào hông thì giữ).
        private static bool IsStretchBone(string path)
        {
            int slash = path.LastIndexOf('/');
            string bone = slash >= 0 ? path.Substring(slash + 1) : path;
            for (int i = 0; i < StretchBonePrefixes.Length; i++)
            {
                if (bone.StartsWith(StretchBonePrefixes[i]))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
