using System;
using System.Collections.Generic;
using System.Numerics;
using Vortice;
using Vortice.Direct2D1;
using Vortice.Direct3D11;
using Vortice.DXGI;
using YukkuriMovieMaker.Project.Items;
using YukkuriMovieMaker.Commons;

namespace YMM43D.Commons
{
    public static class D3D11Helper
    {
        internal static ID3D11ShaderResourceView? GetOrCreateSrvFromD2DImage(
            ID3D11Device device, 
            ID2D1Image? image, 
            IVideoItem item,
            IGraphicsDevicesAndContext? imageDevices,
            Dictionary<IVideoItem, CachedItemTexture> textureCache,
            out float widthInDips,
            out float heightInDips,
            out bool ownsTexture)
        {
            widthInDips = 100f;
            heightInDips = 100f;
            ownsTexture = false;

            if (image == null)
            {
                return null;
            }

            try
            {
                var activeDevices = imageDevices ?? Rendering.SharedGraphics.Devices;
                if (activeDevices == null) return null;

                var d2dContext = activeDevices.DeviceContext;
                RawRectF bounds = GetImageBounds(d2dContext, image, out widthInDips, out heightInDips);

                if (image is ID2D1Bitmap1 b)
                {
                    using var surface = b.Surface;
                    if (surface != null)
                    {
                        using var texture = surface.QueryInterface<ID3D11Texture2D>();
                        if (texture != null && texture.Device.NativePointer == device.NativePointer)
                        {
                            ownsTexture = true;
                            return device.CreateShaderResourceView(texture);
                        }
                    }
                }

                if (d2dContext == null || d2dContext.NativePointer == IntPtr.Zero) return null;

                int width = (int)Math.Max(1, Math.Ceiling(bounds.Right - bounds.Left));
                int height = (int)Math.Max(1, Math.Ceiling(bounds.Bottom - bounds.Top));

                lock (textureCache)
                {
                    if (textureCache.TryGetValue(item, out var cached))
                    {
                        if (cached.Width != width || cached.Height != height || cached.SharedTexture.Device.NativePointer != activeDevices.D3D.Device.NativePointer)
                        {
                            cached.Dispose();
                            textureCache.Remove(item);
                            cached = null;
                        }
                    }

                    if (cached == null)
                    {
                        cached = new CachedItemTexture(device, activeDevices.D3D.Device, d2dContext, width, height);
                        textureCache[item] = cached;
                    }

                    cached.UpdateTexture(d2dContext, image, bounds, activeDevices.D3D.Device);
                    return cached.Srv;
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static ID3D11ShaderResourceView? CreateSrvFromD2DImage(ID3D11Device device, ID2D1Image? image)
        {
            if (image == null) return null;

            try
            {
                if (image is ID2D1Bitmap1 b)
                {
                    using var surface = b.Surface;
                    if (surface != null)
                    {
                        using var texture = surface.QueryInterface<ID3D11Texture2D>();
                        if (texture != null && texture.Device.NativePointer == device.NativePointer)
                        {
                            return device.CreateShaderResourceView(texture);
                        }
                    }
                }

                if (Rendering.SharedGraphics.Devices != null)
                {
                    var devices = Rendering.SharedGraphics.Devices;
                    var d2dContext = devices.DeviceContext;
                    if (d2dContext == null || d2dContext.NativePointer == IntPtr.Zero) return null;

                    RawRectF bounds = GetImageBounds(d2dContext, image, out _, out _);
                    int width = (int)Math.Max(1, Math.Ceiling(bounds.Right - bounds.Left));
                    int height = (int)Math.Max(1, Math.Ceiling(bounds.Bottom - bounds.Top));

                    var desc = new Texture2DDescription
                    {
                        Width = width,
                        Height = height,
                        MipLevels = 1,
                        ArraySize = 1,
                        Format = Format.B8G8R8A8_UNorm,
                        SampleDescription = new SampleDescription(1, 0),
                        Usage = ResourceUsage.Default,
                        BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource,
                        MiscFlags = ResourceOptionFlags.Shared
                    };

                    var renderTexture = device.CreateTexture2D(desc);
                    var srv = device.CreateShaderResourceView(renderTexture);

                    using var dxgiResource = renderTexture.QueryInterface<IDXGIResource>();
                    nint sharedHandle = dxgiResource.SharedHandle;

                    using var sharedTexture = devices.D3D.Device.OpenSharedResource<ID3D11Texture2D>(sharedHandle);
                    using var surface = sharedTexture.QueryInterface<IDXGISurface>();
                    using var tempBitmap = d2dContext.CreateBitmapFromDxgiSurface(surface);

                    UpdateSharedTexture(d2dContext, image, tempBitmap, bounds, devices.D3D.Device);

                    renderTexture.Dispose();
                    return srv;
                }
            }
            catch (Exception)
            {
                return null;
            }

            return null;
        }

        public static RawRectF GetImageBounds(ID2D1DeviceContext? d2dContext, ID2D1Image image, out float widthInDips, out float heightInDips)
        {
            if (image is ID2D1Bitmap bitmap)
            {
                var size = bitmap.Size;
                widthInDips = size.Width;
                heightInDips = size.Height;
                return new RawRectF(0, 0, widthInDips, heightInDips);
            }

            if (d2dContext != null)
            {
                try
                {
                    var bounds = d2dContext.GetImageLocalBounds(image);
                    widthInDips = bounds.Right - bounds.Left;
                    heightInDips = bounds.Bottom - bounds.Top;
                    return bounds;
                }
                catch
                {
                }
            }

            widthInDips = 1f;
            heightInDips = 1f;
            return new RawRectF(0, 0, 1, 1);
        }

        public static void UpdateSharedTexture(ID2D1DeviceContext d2dContext, ID2D1Image image, ID2D1Bitmap1 targetBitmap, RawRectF bounds, ID3D11Device ymmDevice)
        {
            lock (d2dContext)
            {
                var oldTarget = d2dContext.Target;
                var oldTransform = d2dContext.Transform;
                d2dContext.Target = targetBitmap;
                d2dContext.BeginDraw();
                d2dContext.Clear(null);
                d2dContext.Transform = Matrix3x2.CreateTranslation(-bounds.Left, -bounds.Top);
                d2dContext.DrawImage(image);
                d2dContext.EndDraw();
                d2dContext.Transform = oldTransform;
                d2dContext.Target = oldTarget;

                ymmDevice.ImmediateContext.Flush();
            }
        }

        public static ID3D11Buffer CreateConstantBuffer<T>(ID3D11Device device) where T : unmanaged
        {
            return device.CreateBuffer(new BufferDescription
            {
                Usage = ResourceUsage.Default,
                BindFlags = BindFlags.ConstantBuffer,
                ByteWidth = (System.Runtime.InteropServices.Marshal.SizeOf<T>() + 15) / 16 * 16,
            });
        }

        public static ID3D11Buffer CreateBuffer<T>(ID3D11Device device, T[]? data, BindFlags bindFlags) where T : unmanaged
        {
            if (data == null || data.Length == 0)
            {
                return device.CreateBuffer(new BufferDescription
                {
                    Usage = ResourceUsage.Default,
                    BindFlags = bindFlags,
                    ByteWidth = System.Runtime.InteropServices.Marshal.SizeOf<T>()
                });
            }
            return device.CreateBuffer(data, new BufferDescription
            {
                Usage = ResourceUsage.Default,
                BindFlags = bindFlags,
                ByteWidth = System.Runtime.InteropServices.Marshal.SizeOf<T>() * data.Length,
            });
        }

        public static byte[] CompileShader(string source, string entryPoint, string profile)
        {
            var result = Vortice.D3DCompiler.Compiler.Compile(source, entryPoint, "", profile, out var blob, out var errorBlob);
            if (result.Failure)
            {
                string error = errorBlob != null ? System.Text.Encoding.UTF8.GetString(errorBlob.AsBytes()) : "(no error info)";
                errorBlob?.Dispose();
                throw new Exception($"Shader compile failed [{profile} {entryPoint}]: {error}");
            }
            errorBlob?.Dispose();
            return blob!.AsBytes();
        }
    }
}
