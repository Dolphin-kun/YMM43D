using System;
using System.Reflection;
using System.Linq;

class Program
{
    static void Main()
    {
        var asm = Assembly.LoadFrom(@""C:\Program Files\YukkuriMovieMaker4\YukkuriMovieMaker.Plugin.dll""); // guess
        foreach (var p in typeof(YukkuriMovieMaker.Plugin.Effects.VideoEffectAttribute).GetProperties())
        {
            Console.WriteLine(p.Name);
        }
    }
}
