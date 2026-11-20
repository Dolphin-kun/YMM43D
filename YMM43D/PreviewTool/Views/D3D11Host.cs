using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Input;
using System.Windows.Interop;
using SharpGen.Runtime;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using YMM43D.Commons;
using YMM43D.Graphics;
using YMM43D.Player;
using YMM43D.PreviewTool.Rendering;
using YukkuriMovieMaker.Commons;
using Color4 = Vortice.Mathematics.Color4;

namespace YMM43D.PreviewTool.Views
{
    public partial class D3D11Host : HwndHost
    {
        private const string WindowClassName = "YMM43D_PreviewHost_Independent";
        private const int ClassStyleDoubleClicks = 0x0008;
        private const int WindowStyleChild = 0x40000000;
        private const int WindowStyleVisible = 0x10000000;
        private const int StandardArrowCursor = 32512;
        private const int BlackBrush = 4;
        private const int WmNonClientLeftDown = 0x00A1;
        private const int WmNonClientRightDown = 0x00A4;
        private const int WmNonClientMiddleDown = 0x00A7;
        private const int WmMouseMove = 0x0200;
        private const int WmLeftButtonDown = 0x0201;
        private const int WmLeftButtonUp = 0x0202;
        private const int WmRightButtonDown = 0x0204;
        private const int WmRightButtonUp = 0x0205;
        private const int WmMiddleButtonDown = 0x0207;
        private const int WmMiddleButtonUp = 0x0208;
        private const int WmMouseWheel = 0x020A;

        private static bool isClassRegistered;
        private static WndProcDelegate? defWndProc;

        private DisposeCollector? swapChainDisposer;
        private DeviceLease? lease;
        private ID3D11Device? device;
        private ID3D11DeviceContext? deviceContext;
        private IDXGISwapChain? swapChain;
        private PreviewLabelRenderer? labels;
        private readonly Lock frameGate = new();
        private Task? drawing;
        private bool listensKeys;
        private bool engaged;

        public ID3D11RenderTargetView? RenderTargetView { get; private set; }
        public ID3D11DepthStencilView? DepthStencilView { get; private set; }
        public ID3D11ShaderResourceView? DepthView { get; private set; }

        public event Action<ID3D11Device>? Preparing;

        public IReadOnlyList<PreviewLabel> Labels { get; set; } = [];

        internal UvSheet? UvSheet { get; set; }

        public bool SidebarOpen { get; set; }

        private Point? polled;
        private Size hostPixels = Size.Empty;

        public void PollMouse()
        {
            if (Handle == 0 || !GetCursorPos(out var screen) || !ScreenToClient(Handle, ref screen))
                return;

            var at = ToDips(new Point(screen.X, screen.Y));

            if (polled == at)
                return;

            polled = at;
            MouseAction?.Invoke(at, MouseEventKind.Move, 0);
        }

        public float PixelScale { get; private set; } = 1f;
        public event Action<ID3D11Device, ID3D11DeviceContext, bool>? Render;
        public event Action<Point, MouseEventKind, int>? MouseAction;

        public Func<Key, ModifierKeys, bool>? KeyHandler { get; set; }

        public enum MouseEventKind { Down, Move, Up, Wheel, RightDown, RightUp, MiddleDown, MiddleUp }

        public D3D11Host()
        {
            Loaded += (s, e) => InitializeIndependent();
            Unloaded += (s, e) => DisposeAll();
        }

        public void InitializeIndependent()
        {
            lock (frameGate)
            {
                if (lease is not null)
                    return;

                var acquired = GraphicsDevicePool.Acquire();
                lease = acquired;
                device = acquired.Device;
                deviceContext = acquired.Context;

                CreateSwapChain();
            }
        }

        public bool IsDrawing
        {
            get
            {
                if (drawing is { IsFaulted: true } failed)
                {
                    drawing = null;
                    ExceptionDispatchInfo.Capture(failed.Exception.InnerException ?? failed.Exception).Throw();
                }

                return drawing is { IsCompleted: false };
            }
        }

        public void RenderFrame()
        {
            lock (frameGate)
            {
                if (device is null || swapChain is null)
                    return;

                Preparing?.Invoke(device);
                Draw(Capture(), true);
            }
        }

        public void RenderFrameInBackground()
        {
            if (device is null || swapChain is null || IsDrawing)
                return;

            Preparing?.Invoke(device);

            var overlay = Capture();

            drawing = Task.Run(() =>
            {
                lock (frameGate)
                    Draw(overlay, false);
            });
        }

        private Overlay Capture() => new(UvSheet, Labels, SidebarOpen, ThemeColor(SystemColors.ControlBrushKey), ThemeColor(SystemColors.ControlTextBrushKey));

        private void Draw(Overlay overlay, bool resolvesPick)
        {
            if (device is null || deviceContext is null || swapChain is null)
                return;

            using var muted = SceneRevision.Mute();

            lock (D2DGate.Sync)
            lock (device)
            {
                if (DeviceHealth.IsLost(device, "3Dプレビュー", out _))
                    return;

                try
                {
                    Render?.Invoke(device, deviceContext, resolvesPick);
                    labels?.Draw(overlay.UvSheet, overlay.Labels, overlay.SidebarOpen, overlay.Back, overlay.Fore);
                    swapChain.Present(0, PresentFlags.None);
                }
                catch (SharpGenException) when (DeviceHealth.IsLost(device, "3Dプレビュー", out _))
                {
                }
            }
        }

        private sealed record Overlay(UvSheet? UvSheet, IReadOnlyList<PreviewLabel> Labels, bool SidebarOpen, Color4 Back, Color4 Fore);

        protected override HandleRef BuildWindowCore(HandleRef hwndParent)
        {
            if (!isClassRegistered)
            {
                defWndProc = DefWindowProc;
                var classNamePtr = Marshal.StringToHGlobalUni(WindowClassName);

                try
                {
                    var wndClass = new WNDCLASSEX
                    {
                        cbSize = Marshal.SizeOf<WNDCLASSEX>(),
                        style = ClassStyleDoubleClicks,
                        lpfnWndProc = Marshal.GetFunctionPointerForDelegate(defWndProc),
                        hInstance = nint.Zero,
                        hCursor = LoadCursor(nint.Zero, StandardArrowCursor),
                        hbrBackground = GetStockObject(BlackBrush),
                        lpszClassName = classNamePtr,
                    };

                    RegisterClassEx(ref wndClass);
                }
                finally
                {
                    Marshal.FreeHGlobal(classNamePtr);
                }

                isClassRegistered = true;
            }

            var width = Math.Max(1, (int)ActualWidth);
            var height = Math.Max(1, (int)ActualHeight);

            var classPtr = Marshal.StringToHGlobalUni(WindowClassName);
            var windowPtr = Marshal.StringToHGlobalUni(string.Empty);

            try
            {
                var hwnd = CreateWindowEx(
                    0, classPtr, windowPtr, WindowStyleChild | WindowStyleVisible,
                    0, 0, width, height, hwndParent.Handle, nint.Zero, nint.Zero, nint.Zero);

                ImmAssociateContext(hwnd, nint.Zero);
                ListenKeys(true);

                return new HandleRef(this, hwnd);
            }
            finally
            {
                Marshal.FreeHGlobal(classPtr);
                Marshal.FreeHGlobal(windowPtr);
            }
        }

        protected override void DestroyWindowCore(HandleRef hwnd)
        {
            ListenKeys(false);
            DestroyWindow(hwnd.Handle);
            DisposeAll();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                ListenKeys(false);
                DisposeAll();
            }

            base.Dispose(disposing);
        }

        private void DisposeAll()
        {
            lock (frameGate)
            {
                CleanupSwapChain();
                ReleaseDevice();
            }
        }

        private void ReleaseDevice()
        {
            if (lease is not { } held)
                return;

            lease = null;
            device = null;
            deviceContext = null;
            held.Dispose();
        }

        private void CreateSwapChain()
        {
            if (device is null || Handle == nint.Zero || ActualWidth <= 0 || ActualHeight <= 0 || hostPixels.IsEmpty)
                return;

            CleanupSwapChain();

            PixelScale = (float)(hostPixels.Width / ActualWidth);

            var width = (int)hostPixels.Width;
            var height = (int)hostPixels.Height;

            try
            {
                swapChainDisposer = new DisposeCollector();

                using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();

                swapChain = swapChainDisposer.CollectEx(factory.CreateSwapChain(device, new SwapChainDescription
                {
                    BufferCount = 1,
                    BufferDescription = new ModeDescription(width, height, new Rational(60, 1), Format.R8G8B8A8_UNorm),
                    BufferUsage = Usage.RenderTargetOutput,
                    OutputWindow = Handle,
                    SampleDescription = new SampleDescription(1, 0),
                    Windowed = true,
                    SwapEffect = SwapEffect.Discard,
                }));

                using var backBuffer = swapChain.GetBuffer<ID3D11Texture2D>(0);
                RenderTargetView = swapChainDisposer.CollectEx(device.CreateRenderTargetView(backBuffer));
                labels = swapChainDisposer.CollectEx(new PreviewLabelRenderer(device, backBuffer, (float)ActualWidth, (float)ActualHeight, PixelScale));

                var depthBuffer = swapChainDisposer.CollectEx(device.CreateTexture2D(new Texture2DDescription
                {
                    Width = width,
                    Height = height,
                    MipLevels = 1,
                    ArraySize = 1,
                    Format = Format.R24G8_Typeless,
                    SampleDescription = new SampleDescription(1, 0),
                    Usage = ResourceUsage.Default,
                    BindFlags = BindFlags.DepthStencil | BindFlags.ShaderResource,
                }));

                var stencilDesc = new DepthStencilViewDescription(depthBuffer, DepthStencilViewDimension.Texture2D, Format.D24_UNorm_S8_UInt);
                var viewDesc = new ShaderResourceViewDescription(depthBuffer, ShaderResourceViewDimension.Texture2D, Format.R24_UNorm_X8_Typeless);

                DepthStencilView = swapChainDisposer.CollectEx(device.CreateDepthStencilView(depthBuffer, stencilDesc));
                DepthView = swapChainDisposer.CollectEx(device.CreateShaderResourceView(depthBuffer, viewDesc));
            }
            catch
            {
                CleanupSwapChain();
            }
        }

        private void CleanupSwapChain()
        {
            swapChainDisposer?.Dispose();
            swapChainDisposer = null;

            swapChain = null;
            labels = null;
            RenderTargetView = null;
            DepthStencilView = null;
            DepthView = null;
        }

        private Point ToDips(Point device)
        {
            if (hostPixels.IsEmpty || ActualWidth <= 0 || ActualHeight <= 0)
                return device;

            return new Point(device.X * ActualWidth / hostPixels.Width, device.Y * ActualHeight / hostPixels.Height);
        }

        private Point GetPoint(nint lParam) => ToDips(new Point((short)((int)lParam & 0xFFFF), (short)((int)lParam >> 16)));

        private Color4 ThemeColor(object key)
        {
            return TryFindResource(key) is SolidColorBrush { Color: var color }
                ? new Color4(color.R / 255f, color.G / 255f, color.B / 255f, 1f)
                : new Color4(0.5f, 0.5f, 0.5f, 1f);
        }

        protected override void OnWindowPositionChanged(Rect rcBoundingBox)
        {
            base.OnWindowPositionChanged(rcBoundingBox);

            var pixels = new Size(Math.Max(1, Math.Round(rcBoundingBox.Width)), Math.Max(1, Math.Round(rcBoundingBox.Height)));

            if (pixels == hostPixels && swapChain is not null)
                return;

            hostPixels = pixels;

            lock (frameGate)
                CreateSwapChain();
        }

        protected override nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
        {
            switch (msg)
            {
                case WmLeftButtonDown:
                    handled = Press(hwnd, lParam, MouseEventKind.Down);
                    break;

                case WmRightButtonDown:
                    handled = Press(hwnd, lParam, MouseEventKind.RightDown);
                    break;

                case WmMiddleButtonDown:
                    handled = Press(hwnd, lParam, MouseEventKind.MiddleDown);
                    break;

                case WmLeftButtonUp:
                    handled = Release(lParam, MouseEventKind.Up);
                    break;

                case WmRightButtonUp:
                    handled = Release(lParam, MouseEventKind.RightUp);
                    break;

                case WmMiddleButtonUp:
                    handled = Release(lParam, MouseEventKind.MiddleUp);
                    break;

                case WmMouseMove:
                    MouseAction?.Invoke(GetPoint(lParam), MouseEventKind.Move, 0);
                    handled = true;
                    break;

                case WmMouseWheel:
                    MouseAction?.Invoke(new Point(0, 0), MouseEventKind.Wheel, (short)((long)wParam >> 16));
                    handled = true;
                    break;

            }

            return base.WndProc(hwnd, msg, wParam, lParam, ref handled);
        }

        private void ListenKeys(bool wanted)
        {
            if (listensKeys == wanted)
                return;

            listensKeys = wanted;

            if (wanted)
            {
                InputManager.Current.PreProcessInput += OnPreProcessInput;
                ComponentDispatcher.ThreadPreprocessMessage += OnPreprocessMessage;
            }
            else
            {
                InputManager.Current.PreProcessInput -= OnPreProcessInput;
                ComponentDispatcher.ThreadPreprocessMessage -= OnPreprocessMessage;
            }
        }

        private void OnPreprocessMessage(ref MSG msg, ref bool handled)
        {
            if (!handled
                && msg.message is WmLeftButtonDown or WmRightButtonDown or WmMiddleButtonDown or WmNonClientLeftDown or WmNonClientRightDown or WmNonClientMiddleDown)
            {
                engaged = msg.hwnd == Handle || IsInScope(msg.pt_x, msg.pt_y);
            }
        }

        private void OnPreProcessInput(object sender, PreProcessInputEventArgs e)
        {
            if (!engaged || !IsVisible)
                return;

            switch (e.StagingItem.Input)
            {
                case KeyEventArgs pressed when pressed.RoutedEvent == Keyboard.PreviewKeyDownEvent && Keyboard.FocusedElement is not (MenuItem or MenuBase or TextBox):
                    if (Consume(RealKey(pressed), Keyboard.Modifiers))
                        pressed.Handled = true;

                    break;

                case TextCompositionEventArgs typed when typed.RoutedEvent == TextCompositionManager.PreviewTextInputEvent && Keyboard.FocusedElement is not TextBox:
                    e.Cancel();
                    break;
            }
        }

        private static Key RealKey(KeyEventArgs pressed) => pressed.Key switch
        {
            Key.ImeProcessed => pressed.ImeProcessedKey,
            Key.System => pressed.SystemKey,
            _ => pressed.Key,
        };

        private bool Consume(Key key, ModifierKeys modifiers)
            => KeyHandler?.Invoke(key, modifiers) == true || IsTyping(key, modifiers) || key is Key.Enter || IsForeignShortcut(key, modifiers);

        private static bool IsForeignShortcut(Key key, ModifierKeys modifiers)
            => (modifiers & (ModifierKeys.Control | ModifierKeys.Alt)) != 0
            && key is (>= Key.A and <= Key.Z) or (>= Key.D0 and <= Key.D9)
            && !((modifiers & ModifierKeys.Alt) == 0 && key is Key.Z or Key.Y or Key.S);

        private bool IsInScope(int x, int y)
        {
            if (Scope() is not { IsVisible: true } scope)
                return false;

            try
            {
                var from = scope.PointToScreen(new Point(0, 0));
                var to = scope.PointToScreen(new Point(scope.ActualWidth, scope.ActualHeight));

                return x >= Math.Min(from.X, to.X)
                    && x <= Math.Max(from.X, to.X)
                    && y >= Math.Min(from.Y, to.Y)
                    && y <= Math.Max(from.Y, to.Y);
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        private FrameworkElement? Scope()
        {
            DependencyObject? current = this;

            while (current is not null)
            {
                if (current is Preview3DView view)
                    return view;

                current = VisualTreeHelper.GetParent(current) ?? LogicalTreeHelper.GetParent(current);
            }

            return this;
        }

        private static bool IsTyping(Key key, ModifierKeys modifiers)
            => (modifiers & (ModifierKeys.Control | ModifierKeys.Alt)) == 0
            && key is (>= Key.A and <= Key.Z)
                or (>= Key.D0 and <= Key.D9)
                or (>= Key.NumPad0 and <= Key.Divide)
                or (>= Key.Oem1 and <= Key.OemBackslash);

        private bool Press(nint hwnd, nint lParam, MouseEventKind kind)
        {
            engaged = true;
            SetFocus(hwnd);
            Focus();
            SetCapture(hwnd);
            MouseAction?.Invoke(GetPoint(lParam), kind, 0);
            return true;
        }

        private bool Release(nint lParam, MouseEventKind kind)
        {
            ReleaseCapture();
            MouseAction?.Invoke(GetPoint(lParam), kind, 0);
            return true;
        }

        private delegate nint WndProcDelegate(nint hWnd, int msg, nint wParam, nint lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct WNDCLASSEX
        {
            public int cbSize;
            public int style;
            public nint lpfnWndProc;
            public int cbClsExtra;
            public int cbWndExtra;
            public nint hInstance;
            public nint hIcon;
            public nint hCursor;
            public nint hbrBackground;
            public nint lpszMenuName;
            public nint lpszClassName;
            public nint hIconSm;
        }

        [LibraryImport("user32.dll", EntryPoint = "RegisterClassExW", SetLastError = true)]
        private static partial short RegisterClassEx(ref WNDCLASSEX lpwcx);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool GetCursorPos(out CursorPoint point);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool ScreenToClient(nint hWnd, ref CursorPoint point);

        [StructLayout(LayoutKind.Sequential)]
        private struct CursorPoint
        {
            public int X;
            public int Y;
        }

        [LibraryImport("user32.dll", EntryPoint = "DefWindowProcW")]
        private static partial nint DefWindowProc(nint hWnd, int msg, nint wParam, nint lParam);

        [LibraryImport("user32.dll", EntryPoint = "CreateWindowExW", SetLastError = true)]
        private static partial nint CreateWindowEx(
            int dwExStyle,
            nint lpClassName,
            nint lpWindowName,
            int dwStyle,
            int x,
            int y,
            int nWidth,
            int nHeight,
            nint hWndParent,
            nint hMenu,
            nint hInstance,
            nint lpParam);

        [LibraryImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool DestroyWindow(nint hwnd);

        [LibraryImport("gdi32.dll", SetLastError = true)]
        private static partial nint GetStockObject(int fnObject);

        [LibraryImport("user32.dll", EntryPoint = "LoadCursorW", SetLastError = true)]
        private static partial nint LoadCursor(nint hInstance, nint lpCursorName);

        [LibraryImport("user32.dll")]
        private static partial nint SetCapture(nint hWnd);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool ReleaseCapture();

        [LibraryImport("user32.dll", SetLastError = true)]
        private static partial nint SetFocus(nint hWnd);

        [LibraryImport("imm32.dll")]
        private static partial nint ImmAssociateContext(nint hWnd, nint hImc);
    }
}
