using System.Numerics;
using YMM43D.Graphics;

namespace YMM43D.Commons
{
    public sealed class SceneLighting(
        IReadOnlyList<SceneLight> lights,
        Vector3 ambient,
        SceneFog fog,
        int shadowResolution = ShadowMapArray.DefaultSize,
        Vector3? ground = null,
        SceneMirror mirror = default)
    {
        public const float DefaultYaw = 20f;
        public const float DefaultPitch = 30f;
        public const float DefaultBrightness = 0.8f;
        public const float DefaultAmbient = 0.4f;

        public static SceneLighting Default { get; } = new(
            [SceneLight.FromAngles(DefaultYaw, DefaultPitch, new Vector3(DefaultBrightness))],
            new Vector3(DefaultAmbient),
            SceneFog.None);

        public IReadOnlyList<SceneLight> Lights { get; } = lights;

        public Vector3 Ambient { get; } = ambient;

        public SceneFog Fog { get; } = fog;

        public Vector3? Ground { get; } = ground;

        public SceneMirror Mirror { get; } = mirror.HasImage ? mirror : SceneMirror.None;

        public int ShadowResolution { get; } = ShadowMapArray.ClampSize(shadowResolution);

        public IReadOnlyList<LightConstants> LightBuffer => lightBuffer ??= [.. Lights.Select(SceneConstants.ToConstants)];
        private LightConstants[]? lightBuffer;

        public bool NearlyEquals(SceneLighting other)
        {
            if (ReferenceEquals(this, other))
                return true;

            if (Lights.Count != other.Lights.Count
                || Ambient != other.Ambient
                || Fog != other.Fog
                || Ground != other.Ground
                || Mirror != other.Mirror
                || ShadowResolution != other.ShadowResolution)
            {
                return false;
            }

            for (var i = 0; i < Lights.Count; i++)
            {
                if (Lights[i] != other.Lights[i])
                    return false;
            }

            return true;
        }
    }
}
