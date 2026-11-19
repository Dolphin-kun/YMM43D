using System.Numerics;
using System.Runtime.InteropServices;
using Vortice.Direct2D1;
using Vortice.DXGI;
using Vortice.Mathematics;
using YMM43D.Graphics.Models;
using AlphaMode = Vortice.DCommon.AlphaMode;
using PixelFormat = Vortice.DCommon.PixelFormat;

namespace YMM43D.PreviewTool.Rendering
{
    internal sealed class UvSheetPainter(ID2D1DeviceContext context) : IDisposable
    {
        private const int GridCount = 8;
        private const float DotSize = 4f;

        private static readonly Color4 Back = new(0.16f, 0.16f, 0.16f, 1f);
        private static readonly Color4 Paper = new(0.24f, 0.24f, 0.24f, 1f);
        private static readonly Color4 Grid = new(1f, 1f, 1f, 0.08f);
        private static readonly Color4 Frame = new(1f, 1f, 1f, 0.35f);
        private static readonly Color4 Wire = new(0.85f, 0.85f, 0.85f, 1f);
        private static readonly Color4 Seam = new(0.95f, 0.12f, 0.12f, 1f);
        private static readonly Color4 Chosen = new(1f, 0.45f, 0.08f, 1f);
        private static readonly Color4 ChosenFill = new(1f, 0.45f, 0.08f, 0.2f);
        private static readonly Color4 BoxLine = new(1f, 1f, 1f, 0.8f);

        private readonly ID2D1SolidColorBrush brush = context.CreateSolidColorBrush(Back);

        private ModelImage? shownImage;
        private ID2D1Bitmap1? bitmap;

        public void Draw(UvSheet sheet, ID2D1Factory1 factory)
        {
            context.Clear(Back);

            var sheetRect = new Rect(sheet.Origin.X, sheet.Origin.Y, sheet.Zoom, sheet.Zoom);

            brush.Color = Paper;
            context.FillRectangle(sheetRect, brush);

            if (BitmapOf(sheet.Image) is { } image)
                context.DrawBitmap(image, sheetRect, 1f, BitmapInterpolationMode.Linear, null);

            brush.Color = Grid;

            for (var i = 1; i < GridCount; i++)
            {
                var step = sheet.Zoom * i / GridCount;

                context.DrawLine(new Vector2(sheet.Origin.X + step, sheet.Origin.Y), new Vector2(sheet.Origin.X + step, sheet.Origin.Y + sheet.Zoom), brush, 1f);
                context.DrawLine(new Vector2(sheet.Origin.X, sheet.Origin.Y + step), new Vector2(sheet.Origin.X + sheet.Zoom, sheet.Origin.Y + step), brush, 1f);
            }

            brush.Color = Frame;
            context.DrawRectangle(sheetRect, brush, 1f);

            foreach (var face in sheet.ChosenFaces)
                FillPolygon(factory, face, ChosenFill);

            Lines(sheet.Lines, Wire, 1f);
            Lines(sheet.SeamLines, Seam, 2f);
            Lines(sheet.ChosenLines, Chosen, 1.5f);
            Dots(sheet.Dots, Wire);
            Dots(sheet.ChosenDots, Chosen);

            if (sheet.Box is { } box)
            {
                var min = Vector2.Min(box.From, box.To);
                var max = Vector2.Max(box.From, box.To);

                brush.Color = BoxLine;
                context.DrawRectangle(new Rect(min.X, min.Y, max.X - min.X, max.Y - min.Y), brush, 1f);
            }
        }

        public void Dispose()
        {
            bitmap?.Dispose();
            bitmap = null;
            brush.Dispose();
        }

        private ID2D1Bitmap1? BitmapOf(ModelImage? image)
        {
            if (ReferenceEquals(image, shownImage))
                return bitmap;

            bitmap?.Dispose();
            bitmap = null;
            shownImage = image;

            if (image is null || image.Width <= 0 || image.Height <= 0)
                return null;

            var handle = GCHandle.Alloc(image.Pixels, GCHandleType.Pinned);

            try
            {
                var properties = new BitmapProperties1(new PixelFormat(Format.B8G8R8A8_UNorm, AlphaMode.Premultiplied), 96f, 96f);

                bitmap = context.CreateBitmap(new SizeI(image.Width, image.Height), handle.AddrOfPinnedObject(), image.Width * 4, properties);
            }
            finally
            {
                handle.Free();
            }

            return bitmap;
        }

        private void Lines(Vector2[] ends, Color4 color, float width)
        {
            brush.Color = color;

            for (var i = 0; i + 1 < ends.Length; i += 2)
                context.DrawLine(ends[i], ends[i + 1], brush, width);
        }

        private void Dots(Vector2[] points, Color4 color)
        {
            brush.Color = color;

            foreach (var point in points)
                context.FillRectangle(new Rect(point.X - DotSize / 2f, point.Y - DotSize / 2f, DotSize, DotSize), brush);
        }

        private void FillPolygon(ID2D1Factory1 factory, Vector2[] corners, Color4 color)
        {
            if (corners.Length < 3)
                return;

            using var shape = factory.CreatePathGeometry();
            using (var sink = shape.Open())
            {
                sink.BeginFigure(corners[0], FigureBegin.Filled);
                sink.AddLines(corners[1..]);
                sink.EndFigure(FigureEnd.Closed);
                sink.Close();
            }

            brush.Color = color;
            context.FillGeometry(shape, brush);
        }
    }
}
