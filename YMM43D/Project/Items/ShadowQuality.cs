using System.ComponentModel.DataAnnotations;

namespace YMM43D.Project.Items
{
    public enum ShadowQuality
    {
        [Display(Name = "粗い", Description = "512 × 512 で、軽いがふちがギザギザになりやすい")]
        Low,

        [Display(Name = "普通", Description = "1024 × 1024")]
        Normal,

        [Display(Name = "細かい", Description = "2048 × 2048")]
        High,

        [Display(Name = "とても細かい", Description = "4096 × 4096 で、広い場面でもくっきりするがメモリを多く使う")]
        Highest,
    }

    internal static class ShadowQualityExtensions
    {
        public static int Resolution(this ShadowQuality quality) => quality switch
        {
            ShadowQuality.Low => 512,
            ShadowQuality.High => 2048,
            ShadowQuality.Highest => 4096,
            _ => 1024,
        };
    }
}
