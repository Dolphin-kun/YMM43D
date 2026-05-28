using System.Numerics;
using Vortice;
using Vortice.Direct2D1;
using Vortice.Direct3D11;

namespace YMM43D.Commons
{
    public static class D3D11Helper
    {
        public static ID3D11Buffer CreateConstantBuffer<T>(ID3D11Device device) where T : unmanaged
        {
            return device.CreateBuffer(new BufferDescription
            {
                Usage = ResourceUsage.Default,
                BindFlags = BindFlags.ConstantBuffer,
                ByteWidth = (System.Runtime.InteropServices.Marshal.SizeOf<T>() + 15) / 16 * 16, // 16byte alignment
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

        /// <summary>
        /// ID2D1Image (Bitmap1) から ID3D11ShaderResourceView を作成します。
        /// </summary>
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
                        if (texture != null)
                        {
                            return device.CreateShaderResourceView(texture);
                        }
                    }
                }
                else if (Rendering.SharedGraphics.Devices != null)
                {
                    // CommandList などのビットマップでない画像をレンダリングしてテクスチャ化
                    try
                    {
                        var devices = Rendering.SharedGraphics.Devices;
                        var d2dContext = devices?.DeviceContext;
                        if (d2dContext == null || d2dContext.NativePointer == IntPtr.Zero) return null;
                        if (image == null || image.NativePointer == IntPtr.Zero) return null;

                        RawRectF bounds;
                        if (image is ID2D1Bitmap bitmap)
                        {
                            var size = bitmap.Size;
                            bounds = new RawRectF(0, 0, size.Width, size.Height);
                        }
                        else
                        {
                            try
                            {
                                bounds = d2dContext.GetImageLocalBounds(image);
                            }
                            catch
                            {
                                bounds = new RawRectF(0, 0, 1, 1);
                            }
                        }

                        int width = (int)System.Math.Max(1, System.Math.Ceiling(bounds.Right - bounds.Left));
                        int height = (int)System.Math.Max(1, System.Math.Ceiling(bounds.Bottom - bounds.Top));

                        // 1. 描画用デバイス（IndependentDevice）側で共有テクスチャを作成
                        var desc = new Texture2DDescription
                        {
                            Width = width,
                            Height = height,
                            MipLevels = 1,
                            ArraySize = 1,
                            Format = Vortice.DXGI.Format.B8G8R8A8_UNorm,
                            SampleDescription = new Vortice.DXGI.SampleDescription(1, 0),
                            Usage = ResourceUsage.Default,
                            BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource,
                            MiscFlags = ResourceOptionFlags.Shared
                        };

                        // SRV がテクスチャを参照し続けるためusing を使わない
                        var renderTexture = device.CreateTexture2D(desc);

                        // 共有ハンドルを介して YMM4側のデバイスでテクスチャを開く
                        using var dxgiResource = renderTexture.QueryInterface<Vortice.DXGI.IDXGIResource>();
                        nint sharedHandle = dxgiResource.SharedHandle;

                        ID3D11Device ymmDevice = devices!.D3D.Device;
                        using var sharedTexture = ymmDevice.OpenSharedResource<ID3D11Texture2D>(sharedHandle);
                        using var surface = sharedTexture.QueryInterface<Vortice.DXGI.IDXGISurface>();

                        // YMM4側の D2D コンテキストで共有テクスチャに書き込む
                        using var tempBitmap = d2dContext.CreateBitmapFromDxgiSurface(surface);

                        lock (d2dContext)
                        {
                            var oldTarget = d2dContext.Target;
                            var oldTransform = d2dContext.Transform;
                            d2dContext.Target = tempBitmap;
                            d2dContext.BeginDraw();
                            d2dContext.Clear(null);
                            d2dContext.Transform = Matrix3x2.CreateTranslation(-bounds.Left, -bounds.Top);
                            d2dContext.DrawImage(image);
                            d2dContext.EndDraw();
                            d2dContext.Transform = oldTransform;
                            d2dContext.Target = oldTarget;

                            // 書き込み完了を保証するため Flush する
                            ymmDevice.ImmediateContext.Flush();
                        }

                        // 4. 描画用デバイス側のテクスチャから SRV を作成して返す
                        // renderTexture 自体は SRV 作成後に Release してもよいが、安全のためそのまま渡す
                        var srv = device.CreateShaderResourceView(renderTexture);
                        renderTexture.Dispose();
                        return srv;
                    }
                    catch (Exception)
                    {
                        return null;
                    }
                }
            }
            catch (Exception)
            {
                return null;
            }

            return null;
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
