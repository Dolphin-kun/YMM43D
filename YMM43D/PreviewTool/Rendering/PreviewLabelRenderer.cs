using System.Numerics;
using Vortice.Direct2D1;
using Vortice.Direct3D11;
using Vortice.DirectWrite;
using Vortice.DXGI;
using Vortice.Mathematics;
using YMM43D.Commons;
using YukkuriMovieMaker.Commons;

namespace YMM43D.PreviewTool.Rendering
{
    internal sealed class PreviewLabelRenderer : IDisposable
    {
        private const string FontName = "Yu Gothic UI";
        private const float FontSize = 12f;
        private const float Margin = 8f;
        private const float PadX = 6f;
        private const float PadY = 2f;
        private const float Gap = 4f;
        private const float Corner = 3f;

        private static readonly Color4 Back = new(0f, 0f, 0f, 0.6f);

        private readonly DisposeCollector disposer = new();
        private readonly ID2D1Factory1 factory;
        private readonly ID2D1DeviceContext context;
        private readonly ID2D1Bitmap1 target;
        private readonly IDWriteFactory writer;
        private readonly IDWriteTextFormat format;
        private readonly ID2D1SolidColorBrush backBrush;
        private readonly ID2D1SolidColorBrush inkBrush;
        private readonly UvSheetPainter sheetPainter;
        private readonly float width;
        private readonly float height;
        private readonly float dpi;

        public PreviewLabelRenderer(ID3D11Device device, ID3D11Texture2D backBuffer, float width, float height, float scale)
        {
            this.width = width;
            this.height = height;
            dpi = 96f * scale;

            using var dxgi = device.QueryInterface<IDXGIDevice>();
            factory = disposer.CollectEx(D2D1.D2D1CreateFactory<ID2D1Factory1>());
            using var surface = backBuffer.QueryInterface<IDXGISurface>();

            var d2d = disposer.CollectEx(factory.CreateDevice(dxgi));

            context = disposer.CollectEx(d2d.CreateDeviceContext(DeviceContextOptions.None));
            target = disposer.CollectEx(context.CreateBitmapFromDxgiSurface(surface));
            writer = disposer.CollectEx(DWrite.DWriteCreateFactory<IDWriteFactory>());
            format = disposer.CollectEx(writer.CreateTextFormat(FontName, FontWeight.Normal, FontStyle.Normal, FontStretch.Normal, FontSize));
            format.WordWrapping = WordWrapping.Wrap;
            backBrush = disposer.CollectEx(context.CreateSolidColorBrush(Back));
            inkBrush = disposer.CollectEx(context.CreateSolidColorBrush(new Color4(1f, 1f, 1f, 1f)));
            sheetPainter = disposer.CollectEx(new UvSheetPainter(context));
        }

        public void Draw(UvSheet? sheet, IReadOnlyList<PreviewLabel> labels, bool sidebarOpen, Color4 tab, Color4 arrow)
        {
            context.Target = target;
            context.SetDpi(dpi, dpi);
            context.BeginDraw();

            if (sheet is not null)
                sheetPainter.Draw(sheet, factory);

            Fill(SidebarTab.Outline(width), tab);
            Fill(SidebarTab.Arrow(width, sidebarOpen), arrow);

            var bottom = height - Margin;
            var down = Margin;
            var room = MathF.Max(width - Margin * 2f - PadX * 2f, 1f);

            for (var i = labels.Count - 1; i >= 0; i--)
            {
                if (labels[i].AtTop)
                    continue;

                using var layout = writer.CreateTextLayout(labels[i].Text, format, room, height);

                var top = bottom - layout.Metrics.Height - PadY * 2f;

                Box(labels[i], layout, top);
                bottom = top - Gap;
            }

            foreach (var label in labels)
            {
                if (!label.AtTop)
                    continue;

                using var layout = writer.CreateTextLayout(label.Text, format, room, height);

                Box(label, layout, down);
                down += layout.Metrics.Height + PadY * 2f + Gap;
            }

            context.EndDraw();
            context.Target = null;
        }

        public void Dispose() => disposer.Dispose();

        private void Box(PreviewLabel label, IDWriteTextLayout layout, float top)
        {
            var metrics = layout.Metrics;

            backBrush.Color = new Color4(Back.R, Back.G, Back.B, Back.A * label.Color.A);
            context.FillRoundedRectangle(new RoundedRectangle(new Rect(Margin, top, metrics.Width + PadX * 2f, metrics.Height + PadY * 2f), Corner, Corner), backBrush);
            inkBrush.Color = label.Color;
            context.DrawTextLayout(new Vector2(Margin + PadX, top + PadY), layout, inkBrush);
        }

        private void Fill(Vector2[] corners, Color4 color)
        {
            using var shape = factory.CreatePathGeometry();
            using (var sink = shape.Open())
            {
                sink.BeginFigure(corners[0], FigureBegin.Filled);
                sink.AddLines(corners[1..]);
                sink.EndFigure(FigureEnd.Closed);
                sink.Close();
            }

            inkBrush.Color = color;
            context.FillGeometry(shape, inkBrush);
        }
    }
}
