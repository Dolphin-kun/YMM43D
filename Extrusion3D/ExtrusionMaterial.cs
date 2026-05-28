using Vortice.Direct3D11;
using YMM43D.Rendering;
using YMM43D.Rendering.Materials;
using YMM43D.Commons;

namespace Extrusion3D
{
    internal class ExtrusionMaterial : I3DMaterial
    {
        public ID3D11VertexShader VertexShader { get; }
        public ID3D11PixelShader PixelShader { get; }
        public byte[] VertexShaderBytecode { get; }

        public ExtrusionMaterial(ID3D11Device device)
        {
            string vsSrc = @"
struct VS_INPUT {
    float3 Position : POSITION;
    float4 Color : COLOR;
    float2 TexCoord : TEXCOORD;
};
struct PS_INPUT {
    float4 Position : SV_POSITION;
    float4 Color : COLOR;
    float2 TexCoord : TEXCOORD;
    float3 LocalPos : LOCPOS;
};
cbuffer ConstantData : register(b0) {
    matrix WorldViewProjection;
    float4 SideColor;
    float Opacity;
    int UseTextureSides;
};
PS_INPUT main(VS_INPUT input) {
    PS_INPUT output;
    output.Position = mul(float4(input.Position, 1.0f), WorldViewProjection);
    output.Color = input.Color;
    output.TexCoord = input.TexCoord;
    output.LocalPos = input.Position;
    return output;
}";

            string psSrc = @"
struct PS_INPUT {
    float4 Position : SV_POSITION;
    float4 Color : COLOR;
    float2 TexCoord : TEXCOORD;
    float3 LocalPos : LOCPOS;
};
Texture2D txDiffuse : register(t0);
SamplerState samLinear : register(s0);
cbuffer ConstantData : register(b0) {
    matrix WorldViewProjection;
    float4 SideColor;
    float3 CameraLocalPos;
    float Opacity;
    int ExtrusionType;
    float Attenuation;
    float2 padding;
};

struct PS_OUTPUT {
    float4 Color : SV_Target;
    float Depth  : SV_Depth;
};

PS_OUTPUT main(PS_INPUT input) {
    PS_OUTPUT output;
    
    float noise = frac(sin(dot(input.Position.xy, float2(12.9898, 78.233))) * 43758.5453);

    float3 ro = CameraLocalPos;
    float3 rd = normalize(input.LocalPos - CameraLocalPos);

    float3 invDir = 1.0 / rd;
    float3 t0 = (-1.0 - ro) * invDir;
    float3 t1 = (1.0 - ro) * invDir;
    
    float3 tmin = min(t0, t1);
    float3 tmax = max(t0, t1);
    
    float tNear = max(max(tmin.x, tmin.y), tmin.z);
    float tFar = min(min(tmax.x, tmax.y), tmax.z);
    
    if (tNear > tFar || tFar < 0.0) discard;
    
    int numSteps = 128;
    float stepSize = (tFar - tNear) / numSteps;
    float t = max(tNear, 0.0) + stepSize * noise;
    
    float hitT = -1.0;
    float2 hitUV = float2(0, 0);
    float3 hitPos = float3(0, 0, 0);
    
    for (int i = 0; i < numSteps; i++) {
        float3 pos = ro + rd * t;
        float2 uv = float2(pos.x * 0.5 + 0.5, -pos.y * 0.5 + 0.5);
        
        float a = txDiffuse.SampleLevel(samLinear, uv, 0).a;
        if (a > 0.5) {
            float tLow = max(tNear, t - stepSize);
            float tHigh = t;
            for(int j=0; j<10; j++) {
                float tMid = (tLow + tHigh) * 0.5;
                float3 mPos = ro + rd * tMid;
                float2 mUV = float2(mPos.x * 0.5 + 0.5, -mPos.y * 0.5 + 0.5);
                float mA = txDiffuse.SampleLevel(samLinear, mUV, 0).a;
                if(mA > 0.5) tHigh = tMid;
                else tLow = tMid;
            }
            hitT = tHigh;
            hitPos = ro + rd * hitT;
            hitUV = float2(hitPos.x * 0.5 + 0.5, -hitPos.y * 0.5 + 0.5);
            break;
        }
        t += stepSize;
    }
    
    if (hitT < 0.0) discard;

    float4 clipPos = mul(float4(hitPos, 1.0), WorldViewProjection);
    output.Depth = clipPos.z / clipPos.w;

    bool isFace = (abs(hitPos.z) > 0.999);

    float3 normal = float3(0,0,0);
    if (abs(hitPos.z - (-1.0)) < 0.005) {
        normal = float3(0, 0, -1);
    } else if (abs(hitPos.z - 1.0) < 0.005) {
        normal = float3(0, 0, 1);
    } else {
        float eps = 0.01;
        float aR = txDiffuse.SampleLevel(samLinear, hitUV + float2(eps, 0), 0).a;
        float aL = txDiffuse.SampleLevel(samLinear, hitUV - float2(eps, 0), 0).a;
        float aU = txDiffuse.SampleLevel(samLinear, hitUV + float2(0, eps), 0).a;
        float aD = txDiffuse.SampleLevel(samLinear, hitUV - float2(0, eps), 0).a;
        
        normal = normalize(float3(aL - aR, aU - aD, 0.005)); 
    }

    if (isFace) {
        float4 texColor = txDiffuse.SampleLevel(samLinear, hitUV, 0);
        output.Color = float4(texColor.rgb, texColor.a * Opacity);
    } else {
        float3 col;
        float shade = 1.0;
        
        if (ExtrusionType == 1) {
            col = txDiffuse.SampleLevel(samLinear, hitUV, 0).rgb;
            float light = max(0.3, dot(normal, normalize(float3(0.5, 0.8, -0.5))));
            shade = lerp(1.0, light, Attenuation);
        } else {
            col = SideColor.rgb;
        }
        
        col *= shade;
        output.Color = float4(col, 1.0 * Opacity);
    }

    return output;
}"
;

            var vs = D3D11Helper.CompileShader(vsSrc, "main", "vs_5_0");
            var ps = D3D11Helper.CompileShader(psSrc, "main", "ps_5_0");

            VertexShaderBytecode = vs;
            VertexShader = device.CreateVertexShader(vs);
            PixelShader = device.CreatePixelShader(ps);
        }

        public void Dispose()
        {
            VertexShader.Dispose();
            PixelShader.Dispose();
        }
    }
}
