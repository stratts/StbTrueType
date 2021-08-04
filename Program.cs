using System;
using System.IO;

namespace StbTrueType
{
    public class Program
    {
        public static void Main()
        {
            var font = new STBTT.stbtt_fontinfo();

            using (var f = File.OpenRead("arialbd.ttf"))
            {
                var buf = new byte[f.Length];
                f.Read(buf);

                STBTT.InitFont(font, buf, STBTT.GetFontOffsetForIndex(buf, 0));
            }

            Console.WriteLine(font.numGlyphs);
            Console.WriteLine(font.loca);
            Console.WriteLine(font.head);
            Console.WriteLine(font.glyf);
            Console.WriteLine(font.hhea);
            Console.WriteLine(font.hmtx);
            Console.WriteLine(font.kern);
            Console.WriteLine(font.gpos);
            Console.WriteLine(font.svg);
        }
    }
}