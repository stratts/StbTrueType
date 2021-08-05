using System;
using System.IO;

namespace StbTrueType
{
    public class Program
    {
        public static void Main()
        {
            Memory<byte> screen = new byte[20 * 79];

            var font = new Stbtt.FontInfo();
            int i, j, ascent, baseline, ch = 0;
            float scale, xpos = 2;       // leave a little padding in case the character extends left
            string text = "Hello World!"; // intentionally misspelled to show 'lj' brokenness

            using (var f = File.OpenRead("arialbd.ttf"))
            {
                var buf = new byte[f.Length];
                f.Read(buf);

                Stbtt.InitFont(font, buf, Stbtt.GetFontOffsetForIndex(buf, 0));
            }

            scale = Stbtt.ScaleForPixelHeight(font, 15);
            Stbtt.GetFontVMetrics(font, out ascent, out var _, out var _);
            baseline = (int)(ascent * scale);

            while (ch < text.Length)
            {
                int advance, lsb, x0, y0, x1, y1;
                float x_shift = xpos - (float)Math.Floor(xpos);
                Stbtt.GetCodepointHMetrics(font, text[ch], out advance, out lsb);
                Stbtt.GetCodepointBitmapBoxSubpixel(font, text[ch], scale, scale, x_shift, 0, out x0, out y0, out x1, out y1);
                var idx = ((int)xpos + x0) + (baseline + y0) * 79;
                Stbtt.MakeCodepointBitmapSubpixel(font, screen.Slice(idx), x1 - x0, y1 - y0, 79, scale, scale, x_shift, 0, text[ch]);
                // note that this stomps the old data, so where character boxes overlap (e.g. 'lj') it's wrong
                // because this API is really for baking character bitmaps into textures. if you want to render
                // a sequence of characters, you really need to render each bitmap to a temp buffer, then
                // "alpha blend" that into the working buffer
                xpos += (advance * scale);

                if (ch < text.Length - 1)
                    xpos += scale * Stbtt.GetCodepointKernAdvance(font, text[ch], text[ch + 1]);
                ++ch;
            }

            for (j = 0; j < 20; ++j)
            {
                for (i = 0; i < 78; ++i)
                    Console.Write(" .:ioVM@"[screen.Span[i + j * 79] >> 5]);
                Console.Write('\n');
            }
        }
    }
}