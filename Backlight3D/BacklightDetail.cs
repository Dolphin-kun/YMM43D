using System.ComponentModel.DataAnnotations;

namespace Backlight3D
{
    public enum BacklightDetail
    {
        [Display(Name = "等倍", Description = "画面と同じ大きさで描きます")]
        Full,

        [Display(Name = "1/2", Description = "縦横 1/2 で描いてから広げます")]
        Half,

        [Display(Name = "1/4", Description = "縦横 1/4 で描いてから広げます")]
        Quarter,
    }

    internal static class BacklightDetailExtensions
    {
        public static int Divisor(this BacklightDetail detail) => detail switch
        {
            BacklightDetail.Half => 2,
            BacklightDetail.Quarter => 4,
            _ => 1,
        };
    }
}
