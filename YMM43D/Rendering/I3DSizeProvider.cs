using System.Numerics;

namespace YMM43D.Rendering
{
    /// <summary>
    /// 3Dプロバイダーがトリミング済みのテクスチャのサイズと原点からのオフセットをPropertyMapperに伝達するためのインターフェース。
    /// </summary>
    public interface I3DSizeProvider
    {
        bool TryGetSize(out float width, out float height, out Vector2 offset);
    }
}
