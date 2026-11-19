using System.Numerics;
using YMM43D.Commons;
using YukkuriMovieMaker.Project.Items;

namespace YMM43D.PreviewTool
{
    internal readonly record struct PreviewHandles(IVideoItem Item, IScenePointHandles Source, FrameContext Time, Vector3[] Points, int Selected)
    {
        public static PreviewHandles? Resolve(IVideoItem item, in FrameContext time, in Matrix4x4 itemWorld)
        {
            if (item is not ShapeItem { ShapeParameter: IScenePointHandles source })
                return null;

            var count = source.HandleCount;

            if (count <= 0)
                return null;

            var toWorld = source.GetHandleTransform(time) * itemWorld;
            var points = new Vector3[count];

            for (var i = 0; i < count; i++)
                points[i] = Vector3.Transform(source.GetHandle(i, time), toWorld);

            return new PreviewHandles(item, source, time, points, Math.Clamp(source.SelectedHandle, 0, count - 1));
        }

        public Vector3 Position => Points[Math.Clamp(Selected, 0, Points.Length - 1)];
    }
}
