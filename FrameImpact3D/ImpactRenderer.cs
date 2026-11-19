using System.Numerics;
using System.Windows.Media;
using Vortice.Direct3D11;
using YMM43D.Commons;
using YMM43D.Graphics;
using YMM43D.Graphics.Materials;
using YukkuriMovieMaker.Commons;

namespace FrameImpact3D
{
    internal sealed class ImpactRenderer : IDisposable
    {
        private const string Shader = "FrameImpact3D.hlsl";
        private const int SparkSeedShift = 104729;
        private const int DepthSlot = 12;
        private const float FlashStretch = 1.5f;
        private const float FlashLead = 0.3f;

        private readonly DisposeCollector disposer = new();
        private readonly ID3D11Device device;
        private readonly RenderPipeline<TransformConstants>[] pipelines;
        private readonly ImpactMesh?[] meshes;

        public ImpactRenderer(ID3D11Device device)
        {
            this.device = device;

            var kinds = Enum.GetValues<ImpactKind>();

            pipelines = [.. kinds.Select(kind => disposer.CollectEx(CreatePipeline(device, kind)))];
            meshes = new ImpactMesh?[kinds.Length];
        }

        public void Draw(in Render3DContext render, FrameImpact3DEffect effect, in FrameContext time, in ImpactMoment moment, in ImpactAnchor anchor, in ImpactAim aim, float opacity, bool isAlwaysOnTop)
        {
            var camera = ImpactCamera.From(render.View);
            var scene = render.CreateConstants(Matrix4x4.Identity, opacity, unlit: true);
            var scale = WorldScale.ToWorld(1f) * anchor.Scale;

            var basis = new ImpactConstants
            {
                Center = new Vector4(anchor.Center, moment.Progress),
                AxisU = new Vector4(aim.U, 0f),
                AxisV = new Vector4(aim.V, 0f),
                AxisW = new Vector4(aim.W, 0f),
                Right = new Vector4(camera.Right, 0f),
                Up = new Vector4(camera.Up, 0f),
                Aim = new Vector4(aim.Angle, aim.Spread, 1f, effect.LinesSpread ? 1f : 0f),
            };

            var settings = new DrawSettings
            {
                Blend = BlendMode.Accumulate,
                Culling = FaceCulling.None,
                IgnoreDepth = isAlwaysOnTop,
                SkipDepthWrite = true,
                LiftsLines = true,
            };

            if (effect.IsShockEnabled)
            {
                Draw(render, new ImpactPattern(ImpactKind.Ring, effect.ShockCount, 0), scene, settings, basis with
                {
                    AxisU = new Vector4(aim.U, effect.ShockRadius.GetFloat(time) * scale),
                    AxisV = new Vector4(aim.V, effect.ShockWidth.GetFloat(time) * scale),
                    Tint = Tint(effect.ShockColor, effect.ShockStrength.GetFloat(time) / 100f),
                });
            }

            if (effect.IsLinesEnabled)
            {
                Draw(render, new ImpactPattern(ImpactKind.Lines, effect.LineCount, moment.Seed), scene, settings, basis with
                {
                    AxisU = new Vector4(aim.U, effect.LineReach.GetFloat(time) * scale),
                    AxisV = new Vector4(aim.V, effect.LineWidth.GetFloat(time) * scale),
                    AxisW = new Vector4(aim.W, effect.LineLength.GetFloat(time) * scale),
                    Tint = Tint(effect.LineColor, effect.LineStrength.GetFloat(time) / 100f),
                });
            }

            if (effect.IsSparksEnabled)
            {
                Draw(render, new ImpactPattern(ImpactKind.Sparks, effect.SparkCount, moment.Seed + SparkSeedShift), scene, settings, basis with
                {
                    AxisU = new Vector4(aim.U, effect.SparkReach.GetFloat(time) * scale),
                    AxisV = new Vector4(aim.V, effect.SparkSize.GetFloat(time) * scale),
                    AxisW = new Vector4(aim.W, effect.SparkDrop.GetFloat(time) * scale),
                    Tint = Tint(effect.SparkColor, effect.SparkStrength.GetFloat(time) / 100f),
                });
            }

            if (effect.IsFlashEnabled)
            {
                var size = effect.FlashSize.GetFloat(time) * scale * FrameImpact3DEffect.FlashGrowth(moment.Progress);
                var glow = effect.FlashStrength.GetFloat(time) / 100f * FrameImpact3DEffect.FlashFade(moment.Progress);
                var focus = aim.Focus;
                var twist = focus > 0f ? ScreenAngle(render, anchor.Center, aim.Direction) : moment.Seed % 90 * MathF.PI / 180f;
                var center = anchor.Center + aim.Direction * size * FlashLead * focus;

                DrawFlash(render, scene, settings, basis with
                {
                    Center = new Vector4(TowardCamera(center, camera.Position, size / 2f), moment.Progress),
                    AxisU = new Vector4(aim.U, size),
                    Up = new Vector4(camera.Up, twist),
                    Tint = Tint(effect.FlashColor, glow),
                    Aim = basis.Aim with { Z = 1f + FlashStretch * focus },
                });
            }
        }

        private void DrawFlash(in Render3DContext render, in TransformConstants scene, in DrawSettings settings, in ImpactConstants constants)
        {
            var pattern = new ImpactPattern(ImpactKind.Flash, 1, 0);

            if (settings.IgnoreDepth || render.IsCapturingScene || render.SceneDepth is not { } depth)
            {
                Draw(render, pattern, scene, settings, constants);
                return;
            }

            var context = render.Context;
            var targets = new ID3D11RenderTargetView[1];

            context.OMGetRenderTargets(1, targets, out var depthStencil);

            try
            {
                context.OMSetRenderTargets(targets[0], null);
                context.VSSetShaderResource(DepthSlot, depth);

                Draw(render, pattern, scene, settings with { IgnoreDepth = true }, constants with { AxisV = constants.AxisV with { W = 1f } });
            }
            finally
            {
                context.VSSetShaderResource(DepthSlot, null!);
                context.OMSetRenderTargets(targets[0], depthStencil);

                targets[0]?.Dispose();
                depthStencil?.Dispose();
            }
        }

        private void Draw(in Render3DContext render, in ImpactPattern pattern, in TransformConstants scene, in DrawSettings settings, in ImpactConstants constants)
        {
            if (constants.Tint.W <= 0f)
                return;

            pipelines[(int)pattern.Kind].Draw(render.Context, scene, constants, settings, GetMesh(pattern));
        }

        private ImpactMesh GetMesh(in ImpactPattern pattern)
        {
            var slot = (int)pattern.Kind;

            if (meshes[slot] is { } existing && existing.Pattern == pattern)
                return existing;

            meshes[slot]?.Dispose();

            return meshes[slot] = new ImpactMesh(device, pattern);
        }

        private static float ScreenAngle(in Render3DContext render, Vector3 from, Vector3 direction)
        {
            var start = Vector4.Transform(new Vector4(from, 1f), render.ViewProjection);
            var end = Vector4.Transform(new Vector4(from + direction, 1f), render.ViewProjection);

            if (start.W <= 1e-6f || end.W <= 1e-6f)
                return 0f;

            var shift = new Vector2(end.X / end.W - start.X / start.W, end.Y / end.W - start.Y / start.W);
            var aspect = render.Projection.M11 != 0f ? render.Projection.M22 / render.Projection.M11 : 1f;

            return MathF.Atan2(shift.Y, shift.X * aspect);
        }

        private static Vector3 TowardCamera(Vector3 center, Vector3 eye, float pull)
        {
            var toward = eye - center;
            var distance = toward.Length();

            return distance > 1e-6f ? center + toward / distance * MathF.Min(pull, distance / 2f) : center;
        }

        private static Vector4 Tint(Color color, float strength) => new(color.ToVector3(), strength);

        private static RenderPipeline<TransformConstants> CreatePipeline(ID3D11Device device, ImpactKind kind)
        {
            var name = kind.ToString();
            var material = new ShaderMaterial(device, typeof(ImpactRenderer).Assembly, Shader, "VS" + name, Shader, "PS" + name);

            return new RenderPipeline<TransformConstants>(device, ImpactVertex.InputElements, material);
        }

        public void Dispose()
        {
            for (var i = 0; i < meshes.Length; i++)
            {
                meshes[i]?.Dispose();
                meshes[i] = null;
            }

            disposer.Dispose();
        }
    }
}
