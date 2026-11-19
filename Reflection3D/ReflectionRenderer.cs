using System.Numerics;
using Vortice.Direct3D11;
using Vortice.Mathematics;
using YMM43D.Commons;
using YMM43D.Graphics;
using YMM43D.Graphics.Materials;
using YMM43D.Graphics.Meshes;

namespace Reflection3D
{
    internal sealed class ReflectionRenderer(ID3D11Device device) : IDisposable
    {
        private const int PlanarColorSlot = 12;
        private const int PlanarDepthSlot = 13;
        private const int CubeColorSlot = 14;
        private const int CubeDepthSlot = 15;
        private const int PointSamplerSlot = 2;
        private const int CubeBaseSize = 512;
        private const int LeastCubeSize = 64;
        private const int MostCubeSize = 1024;
        private const float PlanarBlurLevels = 6f;
        private const float CubeBlurLevels = 4f;

        private readonly RenderPipeline<TransformConstants> overlay
            = new(device, ReflectorVertex.InputElements, new ShaderMaterial(device, typeof(ReflectionRenderer).Assembly, "Reflection3D.hlsl"));
        private readonly ReflectionTargets planar = new(device, ReflectionConstants.PlaneSlots);
        private readonly ReflectionTargets cube = new(device, MirrorCamera.FaceCount);

        private (SurfaceGeometry? Source, ReflectorMesh? Mesh) last;

        public void Draw(
            in Render3DContext render,
            SurfaceGeometry geometry,
            in Matrix4x4 world,
            ID3D11ShaderResourceView? mask,
            float opacity,
            bool alwaysOnTop,
            in ReflectionLook look,
            IReadOnlyCollection<object?> selves)
        {
            var context = render.Context;
            var viewportCount = context.RSGetViewports();

            if (viewportCount == 0 || !Matrix4x4.Invert(world, out var inverse) || MeshFor(geometry) is not { } mesh)
                return;

            var viewports = new Viewport[viewportCount];
            context.RSGetViewports(viewports);

            var viewport = viewports[0];
            var normalMatrix = Matrix4x4.Transpose(inverse);
            var camera = render.GetCameraPosition();
            var shape = mesh.Shape;
            var chosen = Choose(shape, world, normalMatrix, camera, out var leftover);

            var constants = new ReflectionConstants { NormalMatrix = Matrix4x4.Transpose(normalMatrix) };
            constants.FillSlots(ReflectionConstants.CubeSlot);

            var planarWidth = Math.Max(1, (int)MathF.Ceiling(viewport.Width * look.Quality));
            var planarHeight = Math.Max(1, (int)MathF.Ceiling(viewport.Height * look.Quality));

            if (chosen.Count > 0)
            {
                planar.Ensure(planarWidth, planarHeight);

                for (var slot = 0; slot < chosen.Count; slot++)
                {
                    var (plane, mirror) = chosen[slot];

                    if (!MirrorCamera.TryReflect(mirror, render.View, render.Projection, camera, out var view, out var projection))
                        continue;

                    planar.Render(slot, render, view, projection, planarWidth, planarHeight, selves, mirrored: true);

                    constants.SetSlot(plane, slot);
                    constants.SetPlaneInverse(slot, Matrix4x4.Invert(view * projection, out var back) ? back : Matrix4x4.Identity);
                }

                planar.Finish(context);
            }

            var hasCube = shape.HasCurved || leftover;

            if (hasCube)
            {
                var side = Math.Clamp((int)(CubeBaseSize * look.Quality), LeastCubeSize, MostCubeSize);
                var center = Vector3.Transform(shape.Center, world);
                var projection = MirrorCamera.FaceProjection;

                cube.Ensure(side, side);

                for (var face = 0; face < MirrorCamera.FaceCount; face++)
                {
                    var view = MirrorCamera.FaceView(center, face);
                    var viewProjection = view * projection;

                    cube.Render(face, render, view, projection, side, side, selves, mirrored: false);
                    constants.SetFace(face, viewProjection, Matrix4x4.Invert(viewProjection, out var back) ? back : Matrix4x4.Identity);
                }

                cube.Finish(context);
                constants.Cube = new Vector4(center, 1f);
            }

            constants.Viewport = new Vector4(viewport.X, viewport.Y, viewport.Width, viewport.Height);
            constants.PlanarScale = new Vector4(planarWidth / (float)Math.Max(planar.Width, 1), planarHeight / (float)Math.Max(planar.Height, 1), 0f, 0f);
            constants.Look = new Vector4(look.Strength, look.Fresnel, look.Blur * PlanarBlurLevels, look.Blur * CubeBlurLevels);
            constants.Options = new Vector4(look.Reach, mask is null ? 0f : 1f, shape.IsOpen ? 1f : 0f, hasCube ? 1f : 0f);
            constants.Ripple = new Vector4(look.RippleAmount, look.RippleScale, look.RipplePhase, 0f);
            constants.Tint = new Vector4(look.Tint, 1f);

            var states = RenderStates.For(device);
            var scene = render.CreateConstants(world, opacity);
            var settings = new DrawSettings
            {
                Blend = BlendMode.Normal,
                SkipDepthWrite = true,
                LiftsLines = true,
                IgnoreDepth = alwaysOnTop,
                Texture = mask,
                Sampler = states.LinearSampler,
            };

            context.PSSetShaderResource(PlanarColorSlot, planar.Color!);
            context.PSSetShaderResource(PlanarDepthSlot, planar.Depth!);
            context.PSSetShaderResource(CubeColorSlot, cube.Color!);
            context.PSSetShaderResource(CubeDepthSlot, cube.Depth!);
            context.PSSetSampler(0, states.LinearSampler);
            context.PSSetSampler(PointSamplerSlot, states.PointSampler);

            try
            {
                overlay.Draw(context, scene, constants, settings, mesh);
            }
            finally
            {
                context.PSSetShaderResource(PlanarColorSlot, null!);
                context.PSSetShaderResource(PlanarDepthSlot, null!);
                context.PSSetShaderResource(CubeColorSlot, null!);
                context.PSSetShaderResource(CubeDepthSlot, null!);
                context.PSSetSampler(PointSamplerSlot, null!);
            }
        }

        public void Dispose()
        {
            last.Mesh?.Dispose();
            last = default;
            cube.Dispose();
            planar.Dispose();
            overlay.Dispose();
        }

        private ReflectorMesh? MeshFor(SurfaceGeometry geometry)
        {
            if (ReferenceEquals(last.Source, geometry))
                return last.Mesh;

            last.Mesh?.Dispose();

            var shape = ReflectorShape.Build(geometry);

            last = (geometry, shape.Vertices.Length > 0 ? new ReflectorMesh(device, shape) : null);

            return last.Mesh;
        }

        private static List<(int Plane, Plane Mirror)> Choose(ReflectorShape shape, in Matrix4x4 world, in Matrix4x4 normalMatrix, in Vector3 camera, out bool leftover)
        {
            var candidates = new List<(int Plane, Plane Mirror, float Score)>();

            for (var i = 0; i < shape.Planes.Length; i++)
            {
                var plane = shape.Planes[i];
                var normal = Vector3.TransformNormal(plane.Normal, normalMatrix);

                if (normal.LengthSquared() < 1e-12f)
                    continue;

                normal = Vector3.Normalize(normal);

                var point = Vector3.Transform(plane.Centroid, world);
                var toCamera = camera - point;
                var facing = Vector3.Dot(normal, toCamera);

                if (facing < 0f && shape.IsOpen)
                {
                    normal = -normal;
                    facing = -facing;
                }

                if (facing <= 1e-6f)
                    continue;

                var distance = MathF.Max(toCamera.Length(), 1e-4f);
                var score = plane.Area * facing / (distance * distance * distance);

                candidates.Add((i, new Plane(normal, -Vector3.Dot(normal, point)), score));
            }

            var chosen = candidates.OrderByDescending(c => c.Score).Take(ReflectionConstants.PlaneSlots).Select(c => (c.Plane, c.Mirror)).ToList();

            leftover = candidates.Count > chosen.Count;

            return chosen;
        }
    }
}
