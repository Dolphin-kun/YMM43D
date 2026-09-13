using System.Numerics;
using Vortice.Direct3D11;
using Vortice.Mathematics;
using YMM43D.Commons;
using YMM43D.Graphics;

namespace YMM43D.Player
{
    public static class SceneShadows
    {
        private const float MinRadius = 0.5f;

        private const float ConeMargin = 1.1f;

        private static readonly DeviceResourceCache<ShadowCache> caches = new(_ => new ShadowCache());

        public static SceneLighting Build(
            ID3D11Device device,
            ID3D11DeviceContext context,
            SceneLighting lighting,
            IReadOnlyList<SceneDepthCollector.Occluder> casters,
            object requester)
        {
            if (casters.Count == 0 || !WantsShadow(lighting.Lights))
                return lighting;

            var lights = lighting.Lights;
            var maps = ShadowMapArray.For(device);
            var cache = caches.Get(device);

            if (cache.TryReuse(lighting, casters, requester) is { } reused)
            {
                maps.Bind(context);
                return reused;
            }

            ShadowMapArray.Unbind(context);

            var bounds = Cover(casters);
            var placed = lights.ToArray();
            var slice = 0;

            var previousTargets = new ID3D11RenderTargetView[1];
            context.OMGetRenderTargets(1, previousTargets, out var previousDepth);

            var viewportCount = context.RSGetViewports();
            var previousViewports = viewportCount > 0 ? new Viewport[viewportCount] : null;
            if (previousViewports is not null)
                context.RSGetViewports(previousViewports);

            try
            {
                for (var i = 0; i < placed.Length && slice < ShadowMapArray.MaxSlices; i++)
                {
                    if (!placed[i].Shadow.IsWanted || !placed[i].CanCastShadow)
                        continue;

                    if (!TryLookFrom(placed[i], bounds, out var view, out var projection))
                        continue;

                    Draw(device, context, maps.SliceAt(slice), casters, view, projection);

                    placed[i] = placed[i].PlacedAt(slice, ShadowMapArray.Texel, view * projection);
                    slice++;
                }
            }
            finally
            {
                context.OMSetRenderTargets(previousTargets[0], previousDepth);
                previousTargets[0]?.Dispose();
                previousDepth?.Dispose();

                if (previousViewports is not null)
                    context.RSSetViewports(previousViewports);
            }

            maps.Bind(context);

            var built = new SceneLighting(placed, lighting.Ambient, lighting.Fog);

            cache.Remember(lighting, casters, built, requester);

            return built;
        }

        private static bool WantsShadow(IReadOnlyList<SceneLight> lights)
        {
            foreach (var light in lights)
            {
                if (light.Shadow.IsWanted && light.CanCastShadow)
                    return true;
            }

            return false;
        }

        private sealed class ShadowCache : IDisposable
        {
            private readonly HashSet<object> served = new(ReferenceEqualityComparer.Instance);

            private SceneLighting? source;
            private SceneDepthCollector.Occluder[] casters = [];
            private SceneLighting? result;

            public SceneLighting? TryReuse(
                SceneLighting lighting, IReadOnlyList<SceneDepthCollector.Occluder> current, object requester)
            {
                if (result is null || !Matches(lighting, current) || !served.Add(requester))
                    return null;

                return result;
            }

            private bool Matches(SceneLighting lighting, IReadOnlyList<SceneDepthCollector.Occluder> current)
            {
                if (source is null || !source.NearlyEquals(lighting) || casters.Length != current.Count)
                    return false;

                for (var i = 0; i < casters.Length; i++)
                {
                    if (!ReferenceEquals(casters[i].Provider, current[i].Provider)
                        || casters[i].World != current[i].World
                        || casters[i].Time.Frame != current[i].Time.Frame)
                    {
                        return false;
                    }
                }

                return true;
            }

            public void Remember(
                SceneLighting lighting,
                IReadOnlyList<SceneDepthCollector.Occluder> current,
                SceneLighting built,
                object requester)
            {
                source = lighting;
                casters = [.. current];
                result = built;
                served.Clear();
                served.Add(requester);
            }

            public void Dispose()
            {
                source = null;
                casters = [];
                result = null;
                served.Clear();
            }
        }

        private static void Draw(
            ID3D11Device device,
            ID3D11DeviceContext context,
            ID3D11DepthStencilView target,
            IReadOnlyList<SceneDepthCollector.Occluder> casters,
            in Matrix4x4 view,
            in Matrix4x4 projection)
        {
            context.OMSetRenderTargets([], target);
            context.ClearDepthStencilView(target, DepthStencilClearFlags.Depth, 1f, 0);
            context.RSSetViewport(new Viewport(0, 0, ShadowMapArray.Size, ShadowMapArray.Size));

            var render = new Render3DContext(device, context, view, projection);

            foreach (var caster in casters)
            {
                var item = new DrawContext3D
                {
                    World = caster.World,
                    Opacity = 1f,
                    Time = caster.Time,
                    DepthOnly = true,
                };

                try
                {
                    caster.Provider.Draw(render, item);
                }
                catch
                {
                }
            }
        }

        public static WorldBounds Cover(IReadOnlyList<SceneDepthCollector.Occluder> casters)
        {
            var min = new Vector3(float.MaxValue);
            var max = new Vector3(float.MinValue);
            var found = false;

            foreach (var caster in casters)
            {
                if (caster.Provider is not I3DBounds source)
                    continue;

                var world = source.GetLocalBounds(caster.Time).Transform(caster.World);

                min = Vector3.Min(min, world.Min);
                max = Vector3.Max(max, world.Max);
                found = true;
            }

            return found ? new WorldBounds(min, max) : WorldBounds.Empty;
        }

        public static bool TryLookFrom(
            in SceneLight light, in WorldBounds cover, out Matrix4x4 view, out Matrix4x4 projection)
        {
            view = Matrix4x4.Identity;
            projection = Matrix4x4.Identity;

            return light.Kind switch
            {
                LightKind.Directional => TryLookAlong(light, cover, out view, out projection),
                LightKind.Spot => TryLookThroughCone(light, out view, out projection),
                _ => false,
            };
        }

        private static bool TryLookAlong(
            in SceneLight light, in WorldBounds cover, out Matrix4x4 view, out Matrix4x4 projection)
        {
            view = Matrix4x4.Identity;
            projection = Matrix4x4.Identity;

            var toLight = light.Vector;

            if (toLight.LengthSquared() < 1e-8f)
                return false;

            toLight = Vector3.Normalize(toLight);

            var center = cover.Center;
            var radius = MathF.Max(Vector3.Distance(cover.Max, cover.Min) / 2f, MinRadius);

            if (!float.IsFinite(radius) || !IsFinite(center))
                return false;

            var eye = center + toLight * (radius * 2f);

            view = Matrix4x4.CreateLookAt(eye, center, Upward(toLight));
            projection = Matrix4x4.CreateOrthographic(
                radius * 2f * ConeMargin, radius * 2f * ConeMargin, radius * 0.5f, radius * 4f);

            return true;
        }

        private static bool TryLookThroughCone(
            in SceneLight light, out Matrix4x4 view, out Matrix4x4 projection)
        {
            view = Matrix4x4.Identity;
            projection = Matrix4x4.Identity;

            var axis = light.Axis;

            if (axis.LengthSquared() < 1e-8f || !IsFinite(light.Vector))
                return false;

            axis = Vector3.Normalize(axis);

            var reach = MathF.Max(light.Reach, 0.05f);
            var near = MathF.Max(reach * 0.01f, 0.01f);

            var angle = Math.Clamp(
                float.DegreesToRadians(light.OuterAngle * 2f) * ConeMargin, 0.05f, 3f);

            view = Matrix4x4.CreateLookAt(light.Vector, light.Vector + axis * reach, Upward(axis));
            projection = Matrix4x4.CreatePerspectiveFieldOfView(angle, 1f, near, reach);

            return true;
        }

        private static Vector3 Upward(in Vector3 forward)
            => MathF.Abs(forward.Y) > 0.99f ? Vector3.UnitZ : Vector3.UnitY;

        private static bool IsFinite(in Vector3 value)
            => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
    }
}
