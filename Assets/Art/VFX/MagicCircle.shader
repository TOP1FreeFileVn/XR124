// Pháp trận triệu hồi vẽ hoàn toàn bằng toán trong fragment shader (không cần texture): vòng ngoài, dải rune xoay,
// ngôi sao sáu cánh, vạch chia độ và lõi đập nhịp. _Open (0–1) điều khiển hiệu ứng "vẽ dần" từ tâm ra ngoài khi mở cổng.
// Blend premultiplied (One, OneMinusSrcAlpha): nền xanh đậm bán trong suốt làm tối sàn trắng để nét sáng nổi lên,
// còn nét vẽ cộng sáng lên trên. Không ghi depth, hai mặt; hỗ trợ single-pass instanced stereo cho Quest và SRP Batcher.
Shader "XR124/MagicCircle"
{
    Properties
    {
        [HDR] _Color ("Màu chính (vòng, sao)", Color) = (1.6, 1.05, 0.35, 1)
        [HDR] _Color2 ("Màu phụ (rune, quầng)", Color) = (0.35, 0.85, 1.6, 1)
        _Open ("Độ mở", Range(0, 1)) = 1
        _Spin ("Tốc độ xoay (vòng/giây)", Float) = 0.08
        _RuneCount ("Số rune vòng ngoài", Float) = 28
        _Glow ("Quầng sáng", Range(0, 2)) = 0.7
        _Pulse ("Nhịp đập", Range(0, 1)) = 0.25
        _BackColor ("Màu nền pháp trận", Color) = (0.02, 0.04, 0.14, 1)
        _BackOpacity ("Độ đậm nền", Range(0, 1)) = 0.7
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "MagicCircle"
            Tags { "LightMode" = "UniversalForward" }
            Blend One OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half4 _Color2;
                half _Open;
                half _Spin;
                half _RuneCount;
                half _Glow;
                half _Pulse;
                half4 _BackColor;
                half _BackOpacity;
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

            // Vertex: chuyển sang clip space, đưa UV về [-1, 1] để tâm pháp trận ở (0, 0).
            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv * 2.0 - 1.0;
                return output;
            }

            // Xoay vector 2D quanh gốc một góc a (radian).
            float2 Rotate(float2 p, float a)
            {
                float s = sin(a);
                float c = cos(a);
                return float2(c * p.x - s * p.y, s * p.x + c * p.y);
            }

            // Nét vẽ khử răng cưa: 1 khi khoảng cách d nhỏ hơn nửa bề dày w, mờ dần trong 1 pixel (fwidth).
            float Stroke(float d, float w, float aa)
            {
                return 1.0 - smoothstep(w - aa, w + aa, d);
            }

            // Khoảng cách từ p tới đoạn thẳng ab.
            float SegmentDistance(float2 p, float2 a, float2 b)
            {
                float2 pa = p - a;
                float2 ba = b - a;
                float h = saturate(dot(pa, ba) / dot(ba, ba));
                return length(pa - ba * h);
            }

            // Hash 1D ổn định cho mỗi ô rune (chọn nét vẽ ngẫu nhiên nhưng cố định).
            float Hash(float n)
            {
                return frac(sin(n * 127.1) * 43758.5453);
            }

            // Tam giác đều nội tiếp bán kính R, lệch góc offset: trả về khoảng cách nhỏ nhất tới 3 cạnh.
            float TriangleDistance(float2 p, float R, float offset)
            {
                float2 v0 = R * float2(cos(offset), sin(offset));
                float2 v1 = R * float2(cos(offset + 2.0944), sin(offset + 2.0944));
                float2 v2 = R * float2(cos(offset + 4.1888), sin(offset + 4.1888));
                return min(SegmentDistance(p, v0, v1), min(SegmentDistance(p, v1, v2), SegmentDistance(p, v2, v0)));
            }

            // Một dải rune: chia vòng thành 'count' ô theo góc; mỗi ô vẽ 2–3 nét (sọc dọc, gạch ngang, chéo, chấm)
            // chọn bằng hash của chỉ số ô. rIn/rOut là mép trong/ngoài của dải; angle là góc đã cộng độ xoay.
            float RuneBand(float r, float angle, float rIn, float rOut, float count, float aa)
            {
                float band = rOut - rIn;
                float v = (r - rIn) / band;
                if (v < 0.0 || v > 1.0)
                {
                    return 0.0;
                }

                float t = angle / 6.28318 * count;
                float cell = floor(t);
                float u = frac(t);
                // Tọa độ trong ô, chỉnh tỉ lệ theo độ dài cung để nét không bị kéo méo.
                float cellWidth = 6.28318 * r / count;
                float2 p = float2((u - 0.5) * cellWidth / band, v - 0.5);
                float h1 = Hash(cell);
                float h2 = Hash(cell + 17.0);
                float h3 = Hash(cell + 41.0);
                float w = 0.07;
                float aaLocal = aa / band;
                float glyph = 0.0;
                glyph = max(glyph, Stroke(SegmentDistance(p, float2(0.0, -0.32), float2(0.0, 0.32)), w, aaLocal) * step(0.35, h1));
                glyph = max(glyph, Stroke(SegmentDistance(p, float2(-0.22, 0.12 * sign(h2 - 0.5)), float2(0.22, 0.12 * sign(h2 - 0.5))), w, aaLocal) * step(0.3, h2));
                glyph = max(glyph, Stroke(SegmentDistance(p, float2(-0.2, -0.28), float2(0.2, 0.28)), w, aaLocal) * step(0.6, h3));
                glyph = max(glyph, Stroke(abs(length(p - float2(0.0, -0.18)) - 0.1), w * 0.8, aaLocal) * step(h3, 0.3));
                return glyph;
            }

            // Fragment: ghép các lớp pháp trận, nhân mặt nạ "vẽ dần" theo _Open và nhịp đập, xuất màu cộng sáng.
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
                float time = _Time.y;
                float spin = time * _Spin * 6.28318;
                float angle = atan2(p.y, p.x) + 3.14159;

                // Mặt nạ mở: nét hiện dần từ tâm ra ngoài; viền mặt nạ sáng hơn như đang "khắc" pháp trận.
                float reveal = _Open * 1.25;
                float revealMask = smoothstep(reveal, reveal - 0.12, r);
                float revealEdge = smoothstep(0.12, 0.0, abs(r - reveal + 0.06)) * step(_Open, 0.99);

                // Lớp vàng: các vòng tròn đồng tâm.
                float gold = 0.0;
                gold = max(gold, Stroke(abs(r - 0.965), 0.012, aa));
                gold = max(gold, Stroke(abs(r - 0.93), 0.005, aa));
                gold = max(gold, Stroke(abs(r - 0.79), 0.008, aa));
                gold = max(gold, Stroke(abs(r - 0.62), 0.006, aa));
                gold = max(gold, Stroke(abs(r - 0.44), 0.005, aa));

                // Ngôi sao sáu cánh (hai tam giác lệch 60°) nội tiếp vòng 0.62, xoay ngược chiều vòng ngoài.
                float2 pStar = Rotate(p, -spin * 0.6);
                float star = min(TriangleDistance(pStar, 0.62, 1.5708), TriangleDistance(pStar, 0.62, -1.5708));
                gold = max(gold, Stroke(star, 0.006, aa));

                // Vạch chia độ giữa hai vòng 0.62–0.79: mỗi 10° một vạch, vạch dài mỗi 30°.
                float tickCoord = frac((angle + spin * 0.3) / 6.28318) * 36.0;
                float tickAngle = frac(tickCoord);
                // Chỉ số vạch chia hết cho 3 → vạch dài (30°).
                float tickLong = step(fmod(floor(tickCoord), 3.0), 0.5);
                float tickMask = step(0.64, r) * step(r, lerp(0.69, 0.77, tickLong));
                gold = max(gold, Stroke(abs(tickAngle - 0.5) * 6.28318 * r / 36.0, 0.004, aa) * tickMask);

                // Lớp xanh: dải rune ngoài (xoay thuận) và dải rune trong (xoay ngược, thưa hơn).
                float cyan = 0.0;
                cyan = max(cyan, RuneBand(r, frac((angle + spin) / 6.28318) * 6.28318, 0.815, 0.915, _RuneCount, aa));
                cyan = max(cyan, RuneBand(r, frac((angle - spin * 1.4) / 6.28318) * 6.28318, 0.46, 0.58, _RuneCount * 0.5, aa));

                // Lõi: vòng nhỏ + chấm sáng đập nhịp.
                float pulse = 1.0 + _Pulse * sin(time * 5.0);
                float core = Stroke(abs(r - 0.14 * pulse), 0.01, aa) + smoothstep(0.08, 0.0, r) * 0.8;

                // Quầng sáng mềm: lấp đầy nhẹ trong lòng và loang ra ngoài vòng biên.
                float glow = _Glow * (0.08 * (1.0 - r) + 0.25 * exp(-abs(r - 0.965) * 30.0) + 0.15 * exp(-r * 10.0));

                float3 color = _Color.rgb * (gold + core) + _Color2.rgb * (cyan + glow);
                color += (_Color.rgb + _Color2.rgb) * 0.5 * revealEdge * 0.6;
                // Mép ngoài mờ dần để không thấy cạnh vuông của quad.
                float edgeFade = smoothstep(1.0, 0.95, r);
                float brightness = saturate(_Open * 1.6) * (0.9 + 0.1 * pulse);
                float mask = revealMask * edgeFade;
                // Nền: phủ tối trong lòng vòng biên (đậm hơn ở giữa) theo độ mở; alpha này che bớt sàn phía sau.
                float backAlpha = _BackOpacity * mask * saturate(_Open * 1.6) * lerp(1.0, 0.75, r);
                float3 lines = color * mask * brightness;
                // Premultiplied: màu nền đã nhân alpha + nét sáng cộng thêm (nét không làm tăng alpha nên vẫn "phát sáng").
                return half4(_BackColor.rgb * backAlpha + lines, backAlpha);
            }
            ENDHLSL
        }
    }
}
