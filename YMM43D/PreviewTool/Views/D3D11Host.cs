using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using SharpGen.Runtime;
using Vortice.DXGI;
using Vortice.Direct3D11;
using YMM43D.Graphics;
using YMM43D.Player;
using YukkuriMovieMaker.Commons;

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

        private const int WmKeyDown = 0x0100;
        private const int WmSysKeyDown = 0x0104;
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

        public ID3D11RenderTargetView? RenderTargetView { get; private set; }
        public ID3D11DepthStencilView? DepthStencilView { get; private set; }

        public event Action<ID3D11Device>? Preparing;
        public event Action<ID3D11Device, ID3D11DeviceContext, int, int>? Render;
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
            if (device is null || deviceContext is null || swapChain is null)
                return;

            Preparing?.Invoke(device);

            lock (D2DGate.Sync)
            lock (device)
            {
                if (DeviceHealth.IsLost(device, "3Dプレビュー", out _))
                    return;

                try
                {
                    Render?.Invoke(device, deviceContext, (int)ActualWidth, (int)ActualHeight);
                    swapChain.Present(1, PresentFlags.None);
                }
                catch (SharpGenException) when (DeviceHealth.IsLost(device, "3Dプレビュー", out _))
                {
                }
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
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                DisposeAll();

            base.Dispose(disposing);
        }

        private void DisposeAll()
        {
            CleanupSwapChain();
            ReleaseDevice();
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
            if (device is null || Handle == nint.Zero || ActualWidth <= 0 || ActualHeight <= 0)
                return;

            CleanupSwapChain();

            var width = (int)ActualWidth;
            var height = (int)ActualHeight;

            try
            {
                swapChainDisposer = new DisposeCollector();

                using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();

                swapChain = factory.CreateSwapChain(device, new SwapChainDescription
                {
                    BufferCount = 1,
                    BufferDescription = new ModeDescription(width, height, new Rational(60, 1), Format.R8G8B8A8_UNorm),
                    BufferUsage = Usage.RenderTargetOutput,
                    OutputWindow = Handle,
                    SampleDescription = new SampleDescription(1, 0),
                    Windowed = true,
                    SwapEffect = SwapEffect.Discard,
                });
                swapChainDisposer.Collect(swapChain);

                using var backBuffer = swapChain.GetBuffer<ID3D11Texture2D>(0);
                RenderTargetView = device.CreateRenderTargetView(backBuffer);
                swapChainDisposer.Collect(RenderTargetView);

                var depthBuffer = device.CreateTexture2D(new Texture2DDescription
                {
                    Width = width,
                    Height = height,
                    MipLevels = 1,
                    ArraySize = 1,
                    Format = Format.D24_UNorm_S8_UInt,
                    SampleDescription = new SampleDescription(1, 0),
                    Usage = ResourceUsage.Default,
                    BindFlags = BindFlags.DepthStencil,
                });
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
            => new((short)((int)lParam & 0xFFFF), (short)((int)lParam >> 16));

        protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
        {
            base.OnRenderSizeChanged(sizeInfo);
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

                case WmKeyDown:
                case WmSysKeyDown:
                    if (KeyHandler?.Invoke(KeyInterop.KeyFromVirtualKey((int)wParam), Keyboard.Modifiers) == true)
                    {
                        handled = true;
                        return nint.Zero;
                    }

                    break;
            }

            return base.WndProc(hwnd, msg, wParam, lParam, ref handled);
        }

        private bool Press(nint hwnd, nint lParam, MouseEventKind kind)
        {
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
    }
}
