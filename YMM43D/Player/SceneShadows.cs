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

        // 光源から場をもう一度描いて、いちばん手前にある物までの距離を板に残す。
        // 影を落とす光には板の番号と行列を持たせて返す。
        public static SceneLighting Build(
            ID3D11Device device,
            ID3D11DeviceContext context,
            SceneLighting lighting,
            IReadOnlyList<SceneDepthCollector.Occluder> casters)
        {
            if (casters.Count == 0)
                return lighting;

            var lights = lighting.Lights;

            if (!lights.Any(light => light.Shadow.IsWanted && light.CanCastShadow))
                return lighting;

            var maps = ShadowMapArray.For(device);
            var cache = caches.Get(device);

            // 場も光も動いていなければ、さっき描いた板をそのまま使う。
            // 1コマの中では、どのアイテムを描くときも同じ板でよい。
            if (cache.Matches(lighting, casters) && cache.Result is { } reused)
            {
                maps.Bind(context);
                return reused;
            }

            // 描く先の板を、同時に読んでいる状態にしない。
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

            cache.Remember(lighting, casters, built);

            return built;
        }

        private sealed class ShadowCache : IDisposable
        {
            private SceneLighting? source;
            private SceneDepthCollector.Occluder[] casters = [];

            public SceneLighting? Result { get; private set; }

            public bool Matches(SceneLighting lighting, IReadOnlyList<SceneDepthCollector.Occluder> current)
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
                SceneLighting built)
            {
                source = lighting;
                casters = [.. current];
                Result = built;
            }

            public void Dispose()
            {
                source = null;
                casters = [];
                Result = null;
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

        // 影を落とすものすべてを包む箱。平行光の写す範囲を決めるのに使う。
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

        // 平行光には置き場所が無いので、場を包む球をちょうど収める箱で写す。
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

        // スポットは円錐そのものが写す範囲になる。ふちが切れないよう少しだけ広く取る。
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
                Rotation3D.ToRadians(light.OuterAngle * 2f) * ConeMargin, 0.05f, 3f);

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
