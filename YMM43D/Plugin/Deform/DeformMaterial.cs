using System.Reflection;
using Vortice.Direct3D11;
using YMM43D.Graphics.Materials;

namespace YMM43D.Plugin
{
    public sealed class DeformMaterial(ID3D11Device device, Assembly assembly, string shader)
        : ShaderMaterial(device, assembly, shader)
    {
    }
}
