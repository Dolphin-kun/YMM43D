using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using Vortice.DXGI;
using Vortice.Direct3D11;
using YMM43D.Commons;
using YMM43D.Graphics;
using YukkuriMovieMaker.Commons;

namespace YMM43D.PreviewTool.Views
{
    public partial class D3D11Host : HwndHost
    {
        private DisposeCollector? swapChainDisposer;
        private DeviceLease? lease;
        private ID3D11Device? device;
        private ID3D11DeviceContext? deviceContext;
        private IDXGISwapChain? swapChain;

        public ID3D11RenderTargetView? RenderTargetView { get; private set; }
        public ID3D11DepthStencilView? DepthStencilView { get; private set; }

        public event Action<ID3D11Device, ID3D11DeviceContext, int, int>? Render;
        public event Action<Point, MouseEventKind, int>? MouseAction;

        public Func<Key, ModifierKeys, bool>? KeyHandler { get; set; }
        public enum MouseEventKind { Down, Move, Up, Wheel, RightDown, RightUp, MiddleDown, MiddleUp }

        public static ModifierKeys CurrentModifiers
        {
            get
            {
                var modifiers = ModifierKeys.None;

                if (IsDown(VirtualKeyControl)) modifiers |= ModifierKeys.Control;
                if (IsDown(VirtualKeyShift)) modifiers |= ModifierKeys.Shift;
                if (IsDown(VirtualKeyAlt)) modifiers |= ModifierKeys.Alt;

                return modifiers;
            }
        }

        private const int VirtualKeyShift = 0x10;
        private const int VirtualKeyControl = 0x11;
        private const int VirtualKeyAlt = 0x12;

        private static bool IsDown(int virtualKey) => (GetKeyState(virtualKey) & 0x8000) != 0;

        private const string WindowClassName = "YMM43D_PreviewHost_Independent";
        private static bool isClassRegistered = false;
        private static WndProcDelegate? defWndProc;

        public D3D11Host()
        {
            Loaded += (s, e) => InitializeIndependent();
            Unloaded += (s, e) => DisposeAll();
        }

        public void InitializeIndependent()
        {
            if (lease is not null)
                return;

            var acquired = GraphicsDevicePool.Acquire();
            lease = acquired;
            device = acquired.Device;
            deviceContext = acquired.Context;

            CreateSwapChain();
        }

        public void RenderFrame()
        {
            if (device == null || deviceContext == null || swapChain == null) return;

            lock (D2DGate.Sync)
            lock (device)
            {
                Render?.Invoke(device, deviceContext, (int)ActualWidth, (int)ActualHeight);
                swapChain.Present(1, PresentFlags.None);
            }
        }

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
                        style = 0x0008,
                        lpfnWndProc = Marshal.GetFunctionPointerForDelegate(defWndProc),
                        hInstance = nint.Zero,
                        hCursor = LoadCursor(nint.Zero, (nint)32512),
                        hbrBackground = GetStockObject(4),
                        lpszClassName = classNamePtr
                    };
                    RegisterClassEx(ref wndClass);
                }
                finally
                {
                    Marshal.FreeHGlobal(classNamePtr);
                }
                isClassRegistered = true;
            }

            int w = Math.Max(1, (int)ActualWidth);
            int h = Math.Max(1, (int)ActualHeight);

            var classPtr = Marshal.StringToHGlobalUni(WindowClassName);
            var windowPtr = Marshal.StringToHGlobalUni("");
            try
            {
                var hwnd = CreateWindowEx(0, classPtr, windowPtr, 0x40000000 | 0x10000000, 0, 0, w, h, hwndParent.Handle, nint.Zero, nint.Zero, nint.Zero);
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
            DestroyWindow(hwnd.Handle);
            DisposeAll();
            deviceContext = null;
            device = null;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                DisposeAll();
            }

            base.Dispose(disposing);
        }

        private void DisposeAll()
        {
            CleanupSwapChain();
            ReleaseIndependentDeviceIfHeld();
        }

        private void ReleaseIndependentDeviceIfHeld()
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
            if (device == null || Handle == nint.Zero) return;
            if (ActualWidth <= 0 || ActualHeight <= 0) return;

            CleanupSwapChain();

            try
            {
                swapChainDisposer = new DisposeCollector();
                using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
                var desc = new SwapChainDescription
                {
                    BufferCount = 1,
                    BufferDescription = new ModeDescription((int)ActualWidth, (int)ActualHeight, new Rational(60, 1), Format.R8G8B8A8_UNorm),
                    BufferUsage = Usage.RenderTargetOutput,
                    OutputWindow = Handle,
                    SampleDescription = new SampleDescription(1, 0),
                    Windowed = true,
                    SwapEffect = SwapEffect.Discard
                };
                swapChain = factory.CreateSwapChain(device, desc);
                swapChainDisposer.Collect(swapChain);

                using var backBuffer = swapChain.GetBuffer<ID3D11Texture2D>(0);
                RenderTargetView = device.CreateRenderTargetView(backBuffer);
                swapChainDisposer.Collect(RenderTargetView);

                var dsDesc = new Texture2DDescription
                {
                    Width = (int)ActualWidth,
                    Height = (int)ActualHeight,
                    MipLevels = 1,
                    ArraySize = 1,
                    Format = Format.D24_UNorm_S8_UInt,
                    SampleDescription = new SampleDescription(1, 0),
                    Usage = ResourceUsage.Default,
                    BindFlags = BindFlags.DepthStencil
                };
                var depthBuffer = device.CreateTexture2D(dsDesc);
                swapChainDisposer.Collect(depthBuffer);
                DepthStencilView = device.CreateDepthStencilView(depthBuffer);
                swapChainDisposer.Collect(DepthStencilView);
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
            RenderTargetView = null;
            DepthStencilView = null;
        }

        private static Point GetPoint(nint lParam)
        {
            int x = (short)((int)lParam & 0xFFFF);
            int y = (short)((int)lParam >> 16);
            return new Point(x, y);
        }

        protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo) { base.OnRenderSizeChanged(sizeInfo); CreateSwapChain(); }

        protected override nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
        {
            switch (msg)
            {
                case 0x0201:
                    SetFocus(hwnd);
                    this.Focus();
                    SetCapture(hwnd);
                    MouseAction?.Invoke(GetPoint(lParam), MouseEventKind.Down, 0);
                    handled = true;
                    break;
                case 0x0202:
                    ReleaseCapture();
                    MouseAction?.Invoke(GetPoint(lParam), MouseEventKind.Up, 0);
                    handled = true;
                    break;
                case 0x0204:
                    SetFocus(hwnd);
                    this.Focus();
                    SetCapture(hwnd);
                    MouseAction?.Invoke(GetPoint(lParam), MouseEventKind.RightDown, 0);
                    handled = true;
                    break;
                case 0x0205:
                    ReleaseCapture();
                    MouseAction?.Invoke(GetPoint(lParam), MouseEventKind.RightUp, 0);
                    handled = true;
                    break;
                case 0x0207:
                    SetFocus(hwnd);
                    this.Focus();
                    SetCapture(hwnd);
                    MouseAction?.Invoke(GetPoint(lParam), MouseEventKind.MiddleDown, 0);
                    handled = true;
                    break;
                case 0x0208:
                    ReleaseCapture();
                    MouseAction?.Invoke(GetPoint(lParam), MouseEventKind.MiddleUp, 0);
                    handled = true;
                    break;
                case 0x0200:
                    MouseAction?.Invoke(GetPoint(lParam), MouseEventKind.Move, 0);
                    handled = true;
                    break;
                case 0x020A:
                    short delta = (short)((long)wParam >> 16);
                    MouseAction?.Invoke(new Point(0, 0), MouseEventKind.Wheel, delta);
                    handled = true;
                    break;

                case 0x0100:
                case 0x0104:
                    if (KeyHandler?.Invoke(KeyInterop.KeyFromVirtualKey((int)wParam), CurrentModifiers) == true)
                    {
                        handled = true;
                        return nint.Zero;
                    }

                    break;
            }
            return base.WndProc(hwnd, msg, wParam, lParam, ref handled);
        }

        #region Win32 API
        private delegate nint WndProcDelegate(nint hWnd, int msg, nint wParam, nint lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct WNDCLASSEX
        {
            public int cbSize; public int style; public nint lpfnWndProc; public int cbClsExtra; public int cbWndExtra;
            public nint hInstance; public nint hIcon; public nint hCursor; public nint hbrBackground;
            public nint lpszMenuName; public nint lpszClassName; public nint hIconSm;
        }

        [LibraryImport("user32.dll", EntryPoint = "RegisterClassExW", SetLastError = true)]
        private static partial short RegisterClassEx(ref WNDCLASSEX lpwcx);

        [LibraryImport("user32.dll", EntryPoint = "DefWindowProcW")]
        private static partial nint DefWindowProc(nint hWnd, int msg, nint wParam, nint lParam);

        [LibraryImport("user32.dll", EntryPoint = "CreateWindowExW", SetLastError = true)]
        private static partial nint CreateWindowEx(int dwExStyle, nint lpClassName, nint lpWindowName, int dwStyle, int x, int y, int nWidth, int nHeight, nint hWndParent, nint hMenu, nint hInstance, nint lpParam);

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

        [LibraryImport("user32.dll")]
        private static partial short GetKeyState(int nVirtKey);
        #endregion
    }
}
