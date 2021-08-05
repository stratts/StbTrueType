using System;
using System.IO;

namespace StbTrueType
{
    public class Program
    {
        public static void Main()
        {
            var font = new STBTT.stbtt_fontinfo();
            int w, h, xoff, yoff;
            int c = (int)'ĝ', s = 20;

            using (var f = File.OpenRead("arialbd.ttf"))
            {
                var buf = new byte[f.Length];
                f.Read(buf);

                STBTT.InitFont(font, buf, STBTT.GetFontOffsetForIndex(buf, 0));
            }

            var bitmap = STBTT.stbtt_GetCodepointBitmap(font, 0, STBTT.stbtt_ScaleForPixelHeight(font, s), c, out w, out h, out xoff, out yoff);

            var span = bitmap.Span;
            for (int j = 0; j < h; ++j)
            {
                for (int i = 0; i < w; ++i)
                {
                    Console.Write(" .:ioVM@"[span[j * w + i] >> 5]);
                    Console.Write(" ");
                }
                Console.Write('\n');
            }
        }
    }
}