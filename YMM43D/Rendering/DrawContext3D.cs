using System.Numerics;
using Vortice.Direct3D11;

namespace YMM43D.Rendering
{
    public class DrawContext3D
    {
        public Matrix4x4 World { get; set; }
        public float Opacity { get; set; }
        public YukkuriMovieMaker.Project.Blend Blend { get; set; } = YukkuriMovieMaker.Project.Blend.Normal;
        public bool IsAlwaysOnTop { get; set; }
        public int Frame { get; set; }
        public int Length { get; set; }
        public int FPS { get; set; }
        public ID3D11ShaderResourceView? Texture { get; set; }
        public bool OwnsTexture { get; set; }

        /// <summary>
        /// YMM4のDrawDescription.Cameraから取得したアイテムのカメラ変換行列（Matrix4x4）。
        /// RotateEffect(Is3D)等によってアイテムに適用された3D変換を保持します。
        /// Identity の場合はカメラ変換なし。
        /// </summary>
        public Matrix4x4 ItemCameraMatrix { get; set; } = Matrix4x4.Identity;
    }
}
