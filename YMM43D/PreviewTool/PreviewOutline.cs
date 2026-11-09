using System.Numerics;
using YMM43D.Commons;
using YMM43D.Graphics.Meshes;
using YMM43D.Project.Shape;
using YukkuriMovieMaker.Project.Items;

namespace YMM43D.PreviewTool
{
    internal readonly record struct PreviewOutline(
        IVideoItem Item,
        Shape3DParameter Parameter,
        FrameContext Time,
        Matrix4x4 ToWorld,
        Vector3[] Points)
    {
        public static PreviewOutline? Resolve(IVideoItem item, in FrameContext time, in Matrix4x4 itemWorld)
        {
            if (item is not ShapeItem { ShapeParameter: Shape3DParameter { IsCustom: true } parameter })
                return null;

            var points = parameter.OutlinePoints(time);

            if (points.Length < OutlineSolid.MinPoints || points.Length != parameter.Outline.Count)
                return null;

            var unit = OutlineSolid.UnitScale(points, parameter.Form, parameter.Depth / 100f);
            var toWorld = Matrix4x4.CreateScale(unit) * parameter.GetLocalMatrix(time) * itemWorld;

            var placed = new Vector3[points.Length];

            for (var i = 0; i < points.Length; i++)
                placed[i] = Vector3.Transform(new Vector3(points[i], 0f), toWorld);

            return new PreviewOutline(item, parameter, time, toWorld, placed);
        }
    }
}
