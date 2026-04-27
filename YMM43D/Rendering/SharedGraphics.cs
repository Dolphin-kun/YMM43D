using System;
using System.Collections.Generic;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using YukkuriMovieMaker.Commons;

namespace YMM43D.Rendering
{
    public static class SharedGraphics
    {
        public static IGraphicsDevicesAndContext? Devices { get; set; }

        private static ID3D11Device? independentDevice;
        private static ID3D11DeviceContext? independentContext;

        private static readonly Lock syncLock = new();
        private static readonly List<WeakReference<IDisposable>> cleanupList = [];

        static SharedGraphics()
        {
            AppDomain.CurrentDomain.ProcessExit += (s, e) => Dispose();
        }

        public static void RegisterForCleanup(IDisposable resource)
        {
            lock (syncLock)
            {
                cleanupList.Add(new WeakReference<IDisposable>(resource));
            }
        }

        public static ID3D11Device IndependentDevice
        {
            get
            {
                lock (syncLock)
                {
                    if (independentDevice == null)
                    {
                        D3D11.D3D11CreateDevice(
                            null,
                            DriverType.Hardware,
                            DeviceCreationFlags.BgraSupport | DeviceCreationFlags.VideoSupport,
                            [],
                            out independentDevice,
                            out independentContext).CheckError();
                    }
                    return independentDevice;
                }
            }
        }

        public static ID3D11DeviceContext IndependentContext
        {
            get
            {
                var _ = IndependentDevice;
                return independentContext!;
            }
        }

        public static void Dispose()
        {
            lock (syncLock)
            {
                foreach (var weakRef in cleanupList.ToArray())
                {
                    if (weakRef.TryGetTarget(out var resource))
                    {
                        try { resource.Dispose(); } catch { }
                    }
                }
                cleanupList.Clear();

                independentContext?.Dispose();
                independentDevice?.Dispose();
                independentContext = null;
                independentDevice = null;
            }
        }
    }
}
