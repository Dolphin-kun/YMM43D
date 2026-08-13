namespace YMM43D.Graphics.Materials
{
    public static class ShaderSource
    {
        public const string LightStruct = """
            struct Light
            {
                float4 Vector;
                float4 Color;
            };
            """;

        public const string TransformFields = """
                matrix WorldViewProjection;
                matrix World;
                matrix WorldInverse;
                float4 CameraPosition;
                float4 Ambient;
                float4 FogColor;
                float4 Options;
                Light  Lights[4];
            """;

        public const string TransformNames = """
            #define Opacity  Options.x
            #define Unlit    Options.y
            #define FogStart Options.z
            #define FogEnd   Options.w
            """;

        public static string TransformBuffer =>
            $"{LightStruct}\ncbuffer TransformBuffer : register(b0)\n{{\n{TransformFields}\n}};\n{TransformNames}";

        public const string VertexInput = """
            struct VS_IN
            {
                float3 Pos : POSITION;
                float4 Col : COLOR;
                float2 Tex : TEXCOORD0;
                float3 Nrm : NORMAL;
            };
            """;

        public const string PixelInput = """
            struct PS_IN
            {
                float4 Pos : SV_POSITION;
                float4 Col : COLOR;
                float2 Tex : TEXCOORD0;
                float3 Nrm : NORMAL;
                float3 World : TEXCOORD1;
            };
            """;

        public const string VertexShaderMain = """
            PS_IN VSMain(VS_IN input)
            {
                PS_IN output;
                output.Pos = mul(float4(input.Pos, 1.0), WorldViewProjection);
                output.Col = input.Col;
                output.Tex = input.Tex;
                output.Nrm = mul(float4(input.Nrm, 0.0), WorldInverse).xyz;
                output.World = mul(float4(input.Pos, 1.0), World).xyz;
                return output;
            }
            """;

        public const string LightingFunctions = """
            float3 ApplyLight(float3 color, float3 normal, float3 world)
            {
                if (Unlit > 0.5 || dot(normal, normal) < 1e-8)
                    return color;

                float3 n = normalize(normal);
                float3 toEye = CameraPosition.xyz - world;

                if (dot(n, toEye) < 0.0)
                    n = -n;

                float3 sum = Ambient.rgb;

                [unroll]
                for (int i = 0; i < 4; i++)
                {
                    float3 toLight = Lights[i].Vector.xyz;
                    float fade = 1.0;

                    if (Lights[i].Vector.w > 0.5)
                    {
                        float3 offset = Lights[i].Vector.xyz - world;
                        float distance = length(offset);
                        toLight = distance > 1e-6 ? offset / distance : float3(0.0, 0.0, 1.0);
                        fade = saturate(1.0 - distance / max(Lights[i].Color.a, 1e-6));
                        fade *= fade;
                    }

                    sum += Lights[i].Color.rgb * saturate(dot(n, toLight)) * fade;
                }

                return color * sum;
            }

            float3 ApplyFog(float3 color, float3 world)
            {
                if (FogColor.a <= 0.0)
                    return color;

                float distance = length(CameraPosition.xyz - world);
                float amount = saturate((distance - FogStart) / max(FogEnd - FogStart, 1e-6));

                return lerp(color, FogColor.rgb, amount * FogColor.a);
            }

            """;

        public const string Shading = """
            float4 Shade(float4 color, PS_IN input)
            {
                color.rgb = ApplyLight(color.rgb, input.Nrm, input.World);
                color.rgb = ApplyFog(color.rgb, input.World);
                color.a *= Opacity;
                return color;
            }
            """;

        public static string StandardPrologue =>
            $"{TransformBuffer}\n{VertexInput}\n{PixelInput}\n{VertexShaderMain}\n"
          + $"{LightingFunctions}\n{Shading}\n";
    }
}
