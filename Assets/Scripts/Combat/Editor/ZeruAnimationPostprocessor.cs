using UnityEditor;
using UnityEngine;

namespace XR124.Combat.EditorTools
{
    // Sửa animation chân của Zeru khi import Zeru.fbx:
    // 1) "Chân to rồi bé": rig Auto-Rig Pro bake độ co giãn chân (IK stretch) thành localPosition/localScale của các đốt chân,
    //    nên cẳng chân dài/ngắn liên tục. Bỏ kênh vị trí/scale của các đốt dưới c_thigh_b để chân giữ độ dài lúc bind.
    // 2) "Nhảy giật giật": xoay của đốt chân (bàn/ngón chân, xương xoắn/co giãn) sau retarget đi kiểu bậc thang – đứng yên vài
    //    frame rồi bật 40–90° trong 1 frame. Lấy mẫu lại 30 fps rồi làm mượt quaternion bằng bộ lọc nhị thức 1-4-6-4-1 (2 lượt);
    //    clip nối vòng (đầu ≈ cuối) được làm mượt vòng quanh để chỗ lặp không giật.
    public sealed class ZeruAnimationPostprocessor : AssetPostprocessor
    {
        private const string ZeruFbx = "Assets/Art/Pets/Zeru/Zeru.fbx";
        private const float SampleRate = 30f;
        private const int SmoothPasses = 2;
        // Đầu và cuối clip lệch dưới góc này (độ) ở mọi đốt được làm mượt thì coi là clip nối vòng.
        private const float LoopMatchDegrees = 3f;
        private static readonly float[] Kernel = { 1f, 4f, 6f, 4f, 1f };
        private static readonly string[] Axes = { "x", "y", "z", "w" };

        // Tên (tiền tố) các đốt chân bị co giãn; áp dụng cả chân trước (_dupli_001) lẫn chân sau, trái/phải.
        private static readonly string[] StretchBonePrefixes =
        {
            "thigh_twist", "thigh_stretch", "leg_stretch", "leg_twist", "foot", "toes"
        };

        // Tăng số này khi đổi luật để Unity import lại Zeru.fbx.
        public override uint GetVersion()
        {
            return 3;
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

            int smoothed = SmoothLegRotations(clip);
            if (removed > 0 || smoothed > 0)
            {
                Debug.Log($"[Art] Zeru/{clip.name}: bỏ {removed} kênh vị trí/scale, làm mượt xoay {smoothed} đốt chân.");
            }
        }

        // Gom 4 kênh quaternion của từng đốt chân, lấy mẫu lại theo SampleRate, làm mượt rồi ghi đè; trả về số đốt đã làm mượt.
        private static int SmoothLegRotations(AnimationClip clip)
        {
            var groups = new System.Collections.Generic.Dictionary<string, AnimationCurve[]>();
            foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(clip))
            {
                if (!binding.propertyName.StartsWith("m_LocalRotation.") || !IsStretchBone(binding.path))
                {
                    continue;
                }

                if (!groups.TryGetValue(binding.path, out AnimationCurve[] curves))
                {
                    curves = new AnimationCurve[4];
                    groups[binding.path] = curves;
                }

                int axis = System.Array.IndexOf(Axes, binding.propertyName.Substring(binding.propertyName.Length - 1));
                if (axis >= 0)
                {
                    curves[axis] = AnimationUtility.GetEditorCurve(clip, binding);
                }
            }

            int frames = Mathf.Max(2, Mathf.RoundToInt(clip.length * SampleRate) + 1);
            var sampled = new System.Collections.Generic.Dictionary<string, Quaternion[]>();
            bool loops = true;
            foreach (var pair in groups)
            {
                AnimationCurve[] c = pair.Value;
                if (c[0] == null || c[1] == null || c[2] == null || c[3] == null)
                {
                    continue;
                }

                Quaternion[] q = new Quaternion[frames];
                for (int f = 0; f < frames; f++)
                {
                    float t = Mathf.Min(f / SampleRate, clip.length);
                    q[f] = new Quaternion(c[0].Evaluate(t), c[1].Evaluate(t), c[2].Evaluate(t), c[3].Evaluate(t));
                    // Giữ cùng bán cầu với frame trước để trung bình không bị triệt tiêu.
                    if (f > 0 && Quaternion.Dot(q[f - 1], q[f]) < 0f)
                    {
                        q[f] = new Quaternion(-q[f].x, -q[f].y, -q[f].z, -q[f].w);
                    }
                }

                loops &= Quaternion.Angle(q[0], q[frames - 1]) < LoopMatchDegrees;
                sampled[pair.Key] = q;
            }

            foreach (var pair in sampled)
            {
                Quaternion[] q = pair.Value;
                for (int pass = 0; pass < SmoothPasses; pass++)
                {
                    q = SmoothOnce(q, loops);
                }

                for (int axis = 0; axis < 4; axis++)
                {
                    Keyframe[] keys = new Keyframe[frames];
                    for (int f = 0; f < frames; f++)
                    {
                        keys[f] = new Keyframe(Mathf.Min(f / SampleRate, clip.length), q[f][axis]);
                    }

                    AnimationCurve curve = new AnimationCurve(keys);
                    for (int f = 0; f < frames; f++)
                    {
                        curve.SmoothTangents(f, 0f);
                    }

                    AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(pair.Key, typeof(Transform), "m_LocalRotation." + Axes[axis]), curve);
                }
            }

            return sampled.Count;
        }

        // Một lượt lọc nhị thức 1-4-6-4-1 trên dãy quaternion (trung bình có trọng số rồi chuẩn hóa, cùng bán cầu với tâm).
        // Clip nối vòng: lấy mẫu vòng quanh (bỏ frame cuối trùng frame đầu); clip thường: kẹp chỉ số ở hai đầu (vẫn làm mượt
        // frame đầu/cuối để không còn bước nhảy giữa frame 0 chưa lọc và frame 1 đã lọc).
        private static Quaternion[] SmoothOnce(Quaternion[] q, bool loops)
        {
            int n = q.Length;
            int period = loops ? n - 1 : n;
            Quaternion[] result = new Quaternion[n];
            for (int i = 0; i < n; i++)
            {
                Vector4 sum = Vector4.zero;
                float weight = 0f;
                Quaternion center = q[i];
                for (int k = -2; k <= 2; k++)
                {
                    int j = i + k;
                    j = loops ? ((j % period) + period) % period : Mathf.Clamp(j, 0, n - 1);
                    Quaternion s = q[j];
                    if (Quaternion.Dot(center, s) < 0f)
                    {
                        s = new Quaternion(-s.x, -s.y, -s.z, -s.w);
                    }

                    float w = Kernel[k + 2];
                    sum += new Vector4(s.x, s.y, s.z, s.w) * w;
                    weight += w;
                }

                sum /= weight;
                result[i] = new Quaternion(sum.x, sum.y, sum.z, sum.w).normalized;
            }

            if (loops)
            {
                result[n - 1] = result[0];
            }

            return result;
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
