using System.Numerics;
using Vortice.Direct3D11;
using Vortice.Mathematics;
using YMM43D.Commons;
using YMM43D.Graphics;
using YMM43D.Graphics.Materials;
using YMM43D.Graphics.Meshes;

namespace YMM43D.PreviewTool.Rendering
{
    internal sealed class ScreenOverlayRenderer : IDisposable
    {
        private const int CircleSides = 48;
        private const float DashLength = 8f;
        private const float AxisReach = 1000f;
        private const float CursorPixels = 14f;
        private const float MarkPixels = 4f;

        private static readonly Color4 RegionColor = new(1f, 1f, 1f, 1f);
        private static readonly Color4 GuideColor = new(0.85f, 0.85f, 0.85f, 1f);
        private static readonly Color4 CursorRed = new(0.95f, 0.2f, 0.2f, 1f);
        private static readonly Color4 CursorWhite = new(1f, 1f, 1f, 1f);
        private static readonly Color4 LineColor = new(0.3f, 1f, 0.45f, 1f);
        private static readonly Color4 MarkColor = new(1f, 0.85f, 0.2f, 1f);
        private static readonly Color4[] AxisColors = [new(0.95f, 0.3f, 0.3f, 1f), new(0.45f, 0.85f, 0.3f, 1f), new(0.3f, 0.55f, 1f, 1f)];

        private readonly DeviceResourceCache<RenderPipeline<TransformConstants>> pipelines = new(device => new RenderPipeline<TransformConstants>(device, Vertex.InputElements, new VertexColorMaterial(device)));

        public void Draw(in Render3DContext render, ScreenOverlay overlay, in ViewSnapshot view, in Vector3 right, in Vector3 up)
        {
            if (!pipelines.TryGet(render.Device, out var pipeline))
                return;

            var device = render.Device;
            var context = render.Context;
            var flat = TransformConstants.CreateUnlit(Matrix4x4.Identity, 1f);
            var placed = TransformConstants.CreateUnlit(render.ViewProjection, 1f);
            var onTop = new DrawSettings { IgnoreDepth = true };

            foreach (var (direction, axis) in overlay.Axes)
            {
                Draw(device, context, pipeline, placed, onTop, [overlay.AxisOrigin - direction * AxisReach, overlay.AxisOrigin + direction * AxisReach], AxisColors[Math.Clamp(axis, 0, 2)]);
            }

            if (overlay.Cursor is { } cursor)
            {
                var size = CursorPixels / MathF.Max(view.PixelsPerUnit(cursor), 1e-3f);
                var ring = new List<Vector3>();
                var cross = new List<Vector3>();

                MeshEditRenderer.AddRing(ring, cursor, right, up, size);

                cross.Add(cursor - right * size * 1.6f);
                cross.Add(cursor - right * size * 0.4f);
                cross.Add(cursor + right * size * 0.4f);
                cross.Add(cursor + right * size * 1.6f);
                cross.Add(cursor - up * size * 1.6f);
                cross.Add(cursor - up * size * 0.4f);
                cross.Add(cursor + up * size * 0.4f);
                cross.Add(cursor + up * size * 1.6f);

                Draw(device, context, pipeline, placed, onTop, ring, CursorRed);
                Draw(device, context, pipeline, placed, onTop, cross, CursorWhite);
            }

            if (overlay.Region is { } region)
                Draw(device, context, pipeline, flat, onTop, Outline(region, view), RegionColor);

            if (overlay.GuideFrom is { } from && overlay.GuideTo is { } to)
                Draw(device, context, pipeline, flat, onTop, Dashed(from, to, view), GuideColor);

            foreach (var line in overlay.Lines)
                Draw(device, context, pipeline, flat, onTop, Joined(line, view), LineColor);

            if (overlay.Marks.Count > 0)
                Draw(device, context, pipeline, flat, onTop, Squares(overlay.Marks, view), MarkColor);
        }

        private static List<Vector3> Joined(IReadOnlyList<Vector2> line, in ViewSnapshot view)
        {
            var points = new List<Vector3>();

            for (var i = 0; i + 1 < line.Count; i++)
            {
                points.Add(ToClip(line[i], view));
                points.Add(ToClip(line[i + 1], view));
            }

            return points;
        }

        private static List<Vector3> Squares(IReadOnlyList<Vector2> marks, in ViewSnapshot view)
        {
            var points = new List<Vector3>();

            foreach (var mark in marks)
            {
                Vector2[] corners = [mark + new Vector2(-MarkPixels, -MarkPixels), mark + new Vector2(MarkPixels, -MarkPixels), mark + new Vector2(MarkPixels, MarkPixels), mark + new Vector2(-MarkPixels, MarkPixels)];

                for (var i = 0; i < 4; i++)
                {
                    points.Add(ToClip(corners[i], view));
                    points.Add(ToClip(corners[(i + 1) % 4], view));
                }
            }

            return points;
        }

        private static List<Vector3> Outline(ScreenRegion region, in ViewSnapshot view)
        {
            var points = new List<Vector3>();

            if (region.Shape is SelectShape.Circle)
            {
                for (var i = 0; i < CircleSides; i++)
                {
                    var from = MathF.Tau * i / CircleSides;
                    var to = MathF.Tau * (i + 1) / CircleSides;

                    points.Add(ToClip(region.Center + new Vector2(MathF.Cos(from), MathF.Sin(from)) * region.Radius, view));
                    points.Add(ToClip(region.Center + new Vector2(MathF.Cos(to), MathF.Sin(to)) * region.Radius, view));
                }

                return points;
            }

            var outline = region.Outline;

            for (var i = 0; i < outline.Count; i++)
            {
                points.Add(ToClip(outline[i], view));
                points.Add(ToClip(outline[(i + 1) % outline.Count], view));
            }

            return points;
        }

        private static List<Vector3> Dashed(in Vector2 from, in Vector2 to, in ViewSnapshot view)
        {
            var points = new List<Vector3>();
            var span = to - from;
            var length = span.Length();

            if (length < 1f)
                return points;

            var direction = span / length;

            for (var at = 0f; at < length; at += DashLength * 2f)
            {
                points.Add(ToClip(from + direction * at, view));
                points.Add(ToClip(from + direction * MathF.Min(at + DashLength, length), view));
            }

            return points;
        }

        private static Vector3 ToClip(in Vector2 screen, in ViewSnapshot view)
            => new(screen.X / MathF.Max(view.Width, 1f) * 2f - 1f, 1f - screen.Y / MathF.Max(view.Height, 1f) * 2f, 0f);

        private static void Draw(
            ID3D11Device device,
            ID3D11DeviceContext context,
            RenderPipeline<TransformConstants> pipeline,
            in TransformConstants constants,
            in DrawSettings settings,
            List<Vector3> points,
            in Color4 color)
        {
            if (points.Count < 2)
                return;

            using var line = new LineMesh(device, [.. points], color);

            pipeline.Draw(context, constants, settings, line);
        }

        public void Dispose() => pipelines.Dispose();
    }
}
