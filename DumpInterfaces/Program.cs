using System;
using System.Reflection;
using System.Linq;

class Program
{
    static void Main()
    {
        var asm = Assembly.LoadFrom(@""c:\ƒhƒLƒ…ƒƒ“ƒg\VStudio\YMM4Plugins\YukkuriMovieMaker_v4_Lite_Plugin\YukkuriMovieMaker.Plugin.dll"");
        var types = asm.GetTypes().Where(t => t.Name.Contains(""Camera"")).Select(t => t.FullName).ToArray();
        Console.WriteLine(string.Join(""\n"", types));
    }
}
