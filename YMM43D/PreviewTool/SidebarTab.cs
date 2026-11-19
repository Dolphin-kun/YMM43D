using System.Numerics;

namespace YMM43D.PreviewTool
{
    internal static class SidebarTab
    {
        public const float Width = 22f;
        public const float Height = 72f;
        public const float Top = 40f;
        public const float Chamfer = 10f;

        public static Vector2[] Outline(float viewWidth)
        {
            var right = viewWidth;
            var left = viewWidth - Width;

            return
            [
                new Vector2(right, Top),
                new Vector2(right, Top + Height),
                new Vector2(left, Top + Height - Chamfer),
                new Vector2(left, Top + Chamfer),
            ];
        }

        public static Vector2[] Arrow(float viewWidth, bool open)
        {
            var center = new Vector2(viewWidth - Width / 2f, Top + Height / 2f);
            var tip = open ? 4f : -4f;

            return [center + new Vector2(tip, 0f), center + new Vector2(-tip, -5f), center + new Vector2(-tip, 5f)];
        }

        public static bool Contains(Vector2 point, float viewWidth)
            => point.X >= viewWidth - Width && point.X <= viewWidth && point.Y >= Top && point.Y <= Top + Height;
    }
}
