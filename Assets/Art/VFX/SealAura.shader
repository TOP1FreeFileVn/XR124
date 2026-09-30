// Hiệu ứng tỏa ra khi kết ấn, vẽ hoàn toàn bằng toán trên một quad (UV −1..1), blend cộng sáng.
// _SealType: 0 = Kiếm Chỉ (vòng kiếm khí + 8 lưỡi sáng), 1 = Thuẫn Chưởng (khiên lục giác tổ ong + sóng gợn),
//            2 = Tâm Ấn (hoa sen 8 cánh + sóng hồi phục thu vào), 3 = Ultimate (hai vòng xoay + 12 lưỡi + sóng xung kích).
// _Progress (0→1) do script đẩy qua MaterialPropertyBlock: điều khiển nở ra, sáng lên và tắt dần.
// Hỗ trợ single-pass instanced stereo cho Quest và SRP Batcher (thuộc tính material trong UnityPerMaterial).
Shader "XR124/SealAura"
{
    Properties
    {
        [HDR] _ColorA ("Kiếm Chỉ", Color) = (0.55, 0.95, 1.6, 1)
        [HDR] _ColorD ("Thuẫn Chưởng", Color) = (0.35, 0.6, 1.8, 1)
        [HDR] _ColorH ("Tâm Ấn", Color) = (0.4, 1.6, 0.8, 1)
        [HDR] _ColorU ("Ultimate", Color) = (1.4, 0.6, 1.8, 1)
        [HDR] _ColorGold ("Viền vàng", Color) = (1.6, 1.15, 0.4, 1)
        _SealType ("Loại ấn", Float) = 0
        _Progress ("Tiến trình", Range(0, 1)) = 0.3
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Transparent+10"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "SealAura"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _ColorA;
                half4 _ColorD;
                half4 _ColorH;
                half4 _ColorU;
                half4 _ColorGold;
                float _SealType;
                float _Progress;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // Vertex: chuyển sang clip space, UV về [-1, 1] để tâm hiệu ứng ở (0, 0).
            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv * 2.0 - 1.0;
                return output;
            }

            // Nét vòng tròn khử răng cưa: sáng khi |r − radius| < width, mềm trong 1 pixel.
            float Ring(float r, float radius, float width, float aa)
            {
                return 1.0 - smoothstep(width - aa, width + aa, abs(r - radius));
            }

            // Lưỡi sáng hình tia: count lưỡi quanh tâm, mỗi lưỡi thon dần ra ngoài (dày ở gốc, nhọn ở ngọn) trong [rIn, rOut].
            float Blades(float angle, float r, float count, float rIn, float rOut, float sharpness)
            {
                float t = saturate((r - rIn) / max(rOut - rIn, 1e-4));
                float inside = step(rIn, r) * step(r, rOut);
                float a = abs(frac(angle / 6.28318 * count + 0.5) - 0.5);
                float width = (1.0 - t) * sharpness;
                return inside * smoothstep(width, width * 0.3, a) * (1.0 - t * 0.6);
            }

            // Khoảng cách tới biên lục giác đều (đỉnh hướng lên), bán kính ngoại tiếp ~radius.
            float HexDistance(float2 p, float radius)
            {
                p = abs(p);
                return max(dot(p, float2(0.8660254, 0.5)), p.y) - radius;
            }

            // Lưới tổ ong: trả về khoảng cách tới cạnh ô lục giác gần nhất (0 ở cạnh) cho lưới cỡ scale.
            // Dùng hai lưới chữ nhật lệch nhau nửa ô và lấy tâm gần nhất; mod tự viết để đúng với tọa độ âm.
            float HoneycombEdge(float2 p, float scale)
            {
                p *= scale;
                float2 r = float2(1.0, 1.7320508);
                float2 h = r * 0.5;
                float2 a = p - r * floor(p / r) - h;
                float2 b = (p - h) - r * floor((p - h) / r) - h;
                float2 g = dot(a, a) < dot(b, b) ? a : b;
                float2 q = abs(g);
                float d = max(dot(q, float2(0.5, 0.8660254)), q.x);
                return 0.5 - d;
            }

            // Kiếm Chỉ: vòng kiếm khí nở nhanh, 8 lưỡi sáng xoay nhẹ, viền vàng mảnh và lõi chớp sáng lúc đầu.
            float3 SwordFinger(float r, float angle, float t, float aa)
            {
                float grow = 1.0 - pow(1.0 - t, 3.0);
                float ring = Ring(r, 0.25 + 0.65 * grow, 0.035 * (1.0 - t) + 0.006, aa);
                float gold = Ring(r, 0.18 + 0.5 * grow, 0.006, aa);
                float blades = Blades(angle + t * 1.2, r, 8.0, 0.08, 0.35 + 0.6 * grow, 0.18);
                float core = exp(-r * 14.0) * (1.0 - t) * 2.0;
                return _ColorA.rgb * (ring * 1.4 + blades + core) + _ColorGold.rgb * gold;
            }

            // Thuẫn Chưởng: khiên lục giác nở ra, lưới tổ ong lấp lánh bên trong, sóng gợn chạy từ tâm ra viền.
            float3 PalmShield(float2 p, float r, float t, float aa)
            {
                float grow = 1.0 - pow(1.0 - saturate(t * 1.6), 3.0);
                float size = 0.85 * grow;
                float hex = HexDistance(p, size);
                float border = 1.0 - smoothstep(0.0, 0.03 + aa, abs(hex));
                float inside = step(hex, 0.0);
                float cells = 1.0 - smoothstep(0.0, 0.05, HoneycombEdge(p, 6.0));
                float shimmer = 0.5 + 0.5 * sin(_Time.y * 8.0 + p.x * 9.0 + p.y * 7.0);
                float ripple = Ring(r, frac(t * 2.2) * size, 0.03, aa) * inside;
                float fill = inside * (0.12 + cells * 0.35 * shimmer + ripple * 0.8);
                return _ColorD.rgb * (border * 1.5 + fill) + _ColorGold.rgb * border * 0.25;
            }

            // Tâm Ấn: hoa sen 8 cánh nở ra, các vòng hồi phục thu dần vào tâm, quầng sáng ấm ở giữa.
            float3 HeartLotus(float r, float angle, float t, float aa)
            {
                float grow = 1.0 - pow(1.0 - saturate(t * 1.4), 2.0);
                float petalShape = 0.35 + 0.55 * pow(abs(cos(angle * 4.0 + t * 0.8)), 0.6);
                float petalEdge = Ring(r / max(grow, 1e-3), petalShape, 0.03, aa * 2.0);
                float petalFill = step(r, petalShape * grow) * 0.12;
                float waves = 0.0;
                for (int i = 0; i < 3; i++)
                {
                    float phase = frac(t * 1.5 + i / 3.0);
                    waves += Ring(r, 0.9 * (1.0 - phase), 0.012, aa) * phase;
                }

                float glow = exp(-r * 5.0) * 0.8;
                return _ColorH.rgb * (petalEdge * 1.3 + petalFill + waves + glow) + _ColorGold.rgb * Ring(r, 0.12, 0.01, aa);
            }

            // Ultimate: hai vòng xoay ngược chiều, 12 lưỡi kiếm lớn, sóng xung kích nở rộng và lõi chói.
            float3 Ultimate(float r, float angle, float t, float aa)
            {
                float grow = 1.0 - pow(1.0 - t, 2.0);
                float ringA = Ring(r, 0.55, 0.012, aa) * step(0.5, frac((angle + t * 4.0) / 6.28318 * 18.0));
                float ringB = Ring(r, 0.72, 0.008, aa) * step(0.5, frac((angle - t * 3.0) / 6.28318 * 30.0));
                float blades = Blades(angle - t * 2.0, r, 12.0, 0.1, 0.95, 0.12);
                float shock = Ring(r, 0.2 + 0.8 * grow, 0.05 * (1.0 - t) + 0.005, aa);
                float core = exp(-r * 8.0) * (1.2 - t);
                return _ColorU.rgb * (blades + shock * 1.5 + core) + _ColorGold.rgb * (ringA + ringB) * 1.2;
            }

            // Fragment: chọn kiểu theo _SealType, nhân độ sáng theo tiến trình (lên nhanh, tắt dần) và làm mờ mép quad.
            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 p = input.uv;
                float r = length(p);
                if (r > 1.0)
                {
                    return 0;
                }

                float aa = max(fwidth(r), 1e-4);
                float angle = atan2(p.y, p.x);
                float t = saturate(_Progress);
                int type = (int)round(_SealType);

                float3 color;
                if (type == 0) color = SwordFinger(r, angle, t, aa);
                else if (type == 1) color = PalmShield(p, r, t, aa);
                else if (type == 2) color = HeartLotus(r, angle, t, aa);
                else color = Ultimate(r, angle, t, aa);

                // Sáng lên trong 12% đầu rồi tắt dần tới cuối.
                float intensity = smoothstep(0.0, 0.12, t) * (1.0 - smoothstep(0.55, 1.0, t));
                float edgeFade = smoothstep(1.0, 0.9, r);
                return half4(color * intensity * edgeFade, 1.0);
            }
            ENDHLSL
        }
    }
}
