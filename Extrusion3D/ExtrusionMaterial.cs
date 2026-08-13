using System.Numerics;
using System.Runtime.InteropServices;
using Vortice.Direct3D11;
using YMM43D.Graphics;
using YukkuriMovieMaker.Commons;

namespace Extrusion3D
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct ExtrusionConstants
    {
        public Matrix4x4 WorldViewProjection;
        public Vector4 SideColor;

        public Vector3 CameraLocalPos;

        public float Opacity;

        public int ExtrusionType;

        public float Attenuation;

        private Vector2 padding;
    }

    internal sealed class ExtrusionMaterial : IMaterial
    {
        private readonly DisposeCollector disposer = new();

        public ID3D11VertexShader VertexShader { get; }
        public ID3D11PixelShader PixelShader { get; }
        public byte[] VertexShaderBytecode { get; }

        private const string SharedDeclarations = """
            cbuffer ExtrusionConstants : register(b0)
            {
                matrix WorldViewProjection;
                float4 SideColor;
                float3 CameraLocalPos;
                float  Opacity;
                int    ExtrusionType;
                float  Attenuation;
                float2 Padding;
            };

            struct VS_INPUT
            {
                float3 Position : POSITION;
                float4 Color    : COLOR;
                float2 TexCoord : TEXCOORD;
            };

            struct PS_INPUT
            {
                float4 Position : SV_POSITION;
                float4 Color    : COLOR;
                float2 TexCoord : TEXCOORD;
                float3 LocalPos : LOCPOS;
            };
            """;

        private const string VertexShaderSource = """
            PS_INPUT VSMain(VS_INPUT input)
            {
                PS_INPUT output;
                output.Position = mul(float4(input.Position, 1.0f), WorldViewProjection);
                output.Color    = input.Color;
                output.TexCoord = input.TexCoord;
                output.LocalPos = input.Position;
                return output;
            }
            """;

        private const string PixelShaderSource = """
            Texture2D    txDiffuse : register(t0);
            SamplerState samLinear : register(s0);

            static const int TypeImage = 1;
            static const int StepCount = 128;
            static const int RefineCount = 10;
            static const float AlphaThreshold = 0.5;
            static const float FaceEpsilon = 0.005;

            struct PS_OUTPUT
            {
                float4 Color : SV_Target;
                float  Depth : SV_Depth;
            };

            // ローカル座標をテクスチャ座標に変換する。Y は画像側が下向きなので反転する。
            float2 LocalToUV(float3 pos)
            {
                return float2(pos.x + 0.5, -pos.y + 0.5);
            }

            float SampleAlpha(float2 uv)
            {
                return txDiffuse.SampleLevel(samLinear, uv, 0).a;
            }

            PS_OUTPUT main(PS_INPUT input)
            {
                PS_OUTPUT output;

                float3 rayOrigin = CameraLocalPos;
                float3 rayDir = normalize(input.LocalPos - CameraLocalPos);

                // ボックスとの交差区間を求める（スラブ法）
                float3 invDir = 1.0 / rayDir;
                float3 t0 = (float3(-0.5, -0.5, 0.0) - rayOrigin) * invDir;
                float3 t1 = (float3( 0.5,  0.5, 1.0) - rayOrigin) * invDir;
                float3 tMin = min(t0, t1);
                float3 tMax = max(t0, t1);

                float tNear = max(max(tMin.x, tMin.y), tMin.z);
                float tFar  = min(min(tMax.x, tMax.y), tMax.z);
                if (tNear > tFar || tFar < 0.0) discard;

                // 一定間隔で進めると縞模様が出るため、開始位置をピクセルごとにずらす
                float noise = frac(sin(dot(input.Position.xy, float2(12.9898, 78.233))) * 43758.5453);
                float stepSize = (tFar - tNear) / StepCount;
                float t = max(tNear, 0.0) + stepSize * noise;

                float hitT = -1.0;
                float3 hitPos = float3(0, 0, 0);
                float2 hitUV = float2(0, 0);

                for (int i = 0; i < StepCount; i++)
                {
                    float3 pos = rayOrigin + rayDir * t;
                    if (SampleAlpha(LocalToUV(pos)) > AlphaThreshold)
                    {
                        // 粗く見つけた交点を二分探索で詰める
                        float low = max(tNear, t - stepSize);
                        float high = t;
                        for (int j = 0; j < RefineCount; j++)
                        {
                            float mid = (low + high) * 0.5;
                            if (SampleAlpha(LocalToUV(rayOrigin + rayDir * mid)) > AlphaThreshold)
                                high = mid;
                            else
                                low = mid;
                        }
                        hitT = high;
                        hitPos = rayOrigin + rayDir * hitT;
                        hitUV = LocalToUV(hitPos);
                        break;
                    }
                    t += stepSize;
                }

                if (hitT < 0.0) discard;

                // 交点の実際の位置で深度を書き、他の3D物体と正しく前後判定させる
                float4 clipPos = mul(float4(hitPos, 1.0), WorldViewProjection);
                output.Depth = clipPos.z / clipPos.w;

                bool isFrontFace = abs(hitPos.z - 0.0) < FaceEpsilon;
                bool isBackFace  = abs(hitPos.z - 1.0) < FaceEpsilon;

                if (isFrontFace || isBackFace)
                {
                    float4 texColor = txDiffuse.SampleLevel(samLinear, hitUV, 0);
                    output.Color = float4(texColor.rgb, texColor.a * Opacity);
                    return output;
                }

                // 側面。画像のアルファ勾配から法線を求めて陰影を付ける
                float3 color;
                float shade = 1.0;

                if (ExtrusionType == TypeImage)
                {
                    float eps = 0.01;
                    float right = SampleAlpha(hitUV + float2(eps, 0));
                    float left  = SampleAlpha(hitUV - float2(eps, 0));
                    float up    = SampleAlpha(hitUV + float2(0, eps));
                    float down  = SampleAlpha(hitUV - float2(0, eps));
                    float3 normal = normalize(float3(left - right, up - down, FaceEpsilon));

                    color = txDiffuse.SampleLevel(samLinear, hitUV, 0).rgb;
                    float light = max(0.3, dot(normal, normalize(float3(0.5, 0.8, -0.5))));
                    shade = lerp(1.0, light, Attenuation);
                }
                else
                {
                    color = SideColor.rgb;
                }

                output.Color = float4(color * shade, Opacity);
                return output;
            }
            """;

        public ExtrusionMaterial(ID3D11Device device)
        {
            VertexShaderBytecode = ShaderCompiler.Compile(
                SharedDeclarations + VertexShaderSource, "VSMain", "vs_5_0", nameof(ExtrusionMaterial));
            VertexShader = device.CreateVertexShader(VertexShaderBytecode);
            disposer.Collect(VertexShader);

            var pixelShaderBytecode = ShaderCompiler.Compile(
                SharedDeclarations + PixelShaderSource, "main", "ps_5_0", nameof(ExtrusionMaterial));
            PixelShader = device.CreatePixelShader(pixelShaderBytecode);
            disposer.Collect(PixelShader);
        }

        public void Dispose() => disposer.Dispose();
    }
}
