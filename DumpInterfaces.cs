using System;
using System.Reflection;
using System.Linq;

class Program
{
    static void Main()
    {
        var asm = Assembly.LoadFrom(@"c:\ƒhƒLƒ…ƒƒ“ƒg\VStudio\YMM4Plugins\YMM43D\..\..\..\YukkuriMovieMaker_v4_Lite_Plugin\YukkuriMovieMaker.Plugin.dll");
        foreach (var type in asm.GetTypes().Where(t => t.IsInterface))
        {
            Console.WriteLine(type.FullName);
        }
    }
}
