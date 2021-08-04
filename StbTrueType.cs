using System;

namespace StbTrueType
{
    public static class STBTT
    {
        public class stbtt__buf
        {
            public Memory<byte> data;
            public int cursor;
            public int size;

            public stbtt__buf(Memory<byte> data, int size)
            {
                this.data = data;
                this.size = size;
                cursor = 0;
            }
        }

        public class stbtt_fontinfo
        {
            public Memory<byte> data;              // pointer to .ttf file
            public int fontstart;         // offset of start of font

            public int numGlyphs;                     // number of glyphs, needed for range checking

            public int loca, head, glyf, hhea, hmtx, kern, gpos, svg; // table locations as offset from start of .ttf
            public int index_map;                     // a cmap mapping for our chosen character encoding
            public int indexToLocFormat;              // format needed to map from glyph index to glyph

            public stbtt__buf cff;                    // cff font data
            public stbtt__buf charstrings;            // the charstring index
            public stbtt__buf gsubrs;                 // global charstring subroutines index
            public stbtt__buf subrs;                  // private charstring subroutines index
            public stbtt__buf fontdicts;              // array of font dicts
            public stbtt__buf fdselect;               // map from glyph to fontdict
        };

        // Enums

        const ushort
            STBTT_PLATFORM_ID_UNICODE = 0,
            STBTT_PLATFORM_ID_MAC = 1,
            STBTT_PLATFORM_ID_ISO = 2,
            STBTT_PLATFORM_ID_MICROSOFT = 3;

        const ushort
            STBTT_MS_EID_SYMBOL = 0,
            STBTT_MS_EID_UNICODE_BMP = 1,
            STBTT_MS_EID_SHIFTJIS = 2,
            STBTT_MS_EID_UNICODE_FULL = 10;

        public static int InitFont(stbtt_fontinfo info, byte[] data, int offset)
        {
            return InitFont_internal(info, data, offset);
        }

        static bool stbtt_tag4(Memory<byte> m, char c0, char c1, char c2, char c3)
        {
            var p = m.Span;
            return ((p)[0] == (c0) && (p)[1] == (c1) && (p)[2] == (c2) && (p)[3] == (c3));
        }
        static bool stbtt_tag(Memory<byte> p, string str) => stbtt_tag4(p, str[0], str[1], str[2], str[3]);

        static bool stbtt__isfont(Memory<byte> font)
        {
            // check the version number
            if (stbtt_tag4(font, '1', (char)0, (char)0, (char)0))
                return true; // TrueType 1
            if (stbtt_tag(font, "typ1"))
                return true; // TrueType with type 1 font -- we don't support this!
            if (stbtt_tag(font, "OTTO"))
                return true; // OpenType with CFF
            if (stbtt_tag4(font, (char)0, (char)1, (char)0, (char)0))
                return true; // OpenType 1.0
            if (stbtt_tag(font, "true"))
                return true; // Apple specification for TrueType fonts
            return false;
        }

        static void STBTT_assert(bool cond) { }

        static void stbtt__buf_seek(stbtt__buf b, int o)
        {
            STBTT_assert(!(o > b.size || o < 0));
            b.cursor = (o > b.size || o < 0) ? b.size : o;
        }

        // Buffer functions

        static void stbtt__buf_skip(stbtt__buf b, int o)
        {
            stbtt__buf_seek(b, b.cursor + o);
        }

        static uint stbtt__buf_get(stbtt__buf b, int n)
        {
            uint v = 0;
            int i;
            STBTT_assert(n >= 1 && n <= 4);
            for (i = 0; i < n; i++)
                v = (v << 8) | stbtt__buf_get8(b);
            return v;
        }

        static uint stbtt__buf_get16(stbtt__buf b) => stbtt__buf_get((b), 2);

        static uint stbtt__buf_get32(stbtt__buf b) => stbtt__buf_get((b), 4);

        static byte stbtt__buf_get8(stbtt__buf b)
        {
            if (b.cursor >= b.size)
                return 0;
            return b.data.Span[b.cursor++];
        }

        static stbtt__buf stbtt__buf_range(stbtt__buf b, int o, int s)
        {
            stbtt__buf r = new(null, 0);
            if (o < 0 || s < 0 || o > b.size || s > b.size - o)
                return r;
            r.data = b.data.Slice(o);
            r.size = s;
            return r;
        }

        static byte stbtt__buf_peek8(stbtt__buf b)
        {
            if (b.cursor >= b.size)
                return 0;
            return b.data.Span[b.cursor];
        }

        static void stbtt__cff_skip_operand(stbtt__buf b)
        {
            int v, b0 = stbtt__buf_peek8(b);
            STBTT_assert(b0 >= 28);
            if (b0 == 30)
            {
                stbtt__buf_skip(b, 1);
                while (b.cursor < b.size)
                {
                    v = stbtt__buf_get8(b);
                    if ((v & 0xF) == 0xF || (v >> 4) == 0xF)
                        break;
                }
            }
            else
            {
                stbtt__cff_int(b);
            }
        }

        static int stbtt__cff_int(stbtt__buf b)
        {
            int b0 = stbtt__buf_get8(b);
            if (b0 >= 32 && b0 <= 246)
                return b0 - 139;
            else if (b0 >= 247 && b0 <= 250)
                return (b0 - 247) * 256 + stbtt__buf_get8(b) + 108;
            else if (b0 >= 251 && b0 <= 254)
                return -(b0 - 251) * 256 - stbtt__buf_get8(b) - 108;
            else if (b0 == 28)
                return (int)stbtt__buf_get16(b);
            else if (b0 == 29)
                return (int)stbtt__buf_get32(b);
            STBTT_assert(false);
            return 0;
        }

        static stbtt__buf stbtt__cff_get_index(stbtt__buf b)
        {
            int count, start, offsize;
            start = b.cursor;
            count = (int)stbtt__buf_get16(b);
            if (count != 0)
            {
                offsize = stbtt__buf_get8(b);
                STBTT_assert(offsize >= 1 && offsize <= 4);
                stbtt__buf_skip(b, offsize * count);
                stbtt__buf_skip(b, (int)stbtt__buf_get(b, offsize) - 1);
            }
            return stbtt__buf_range(b, start, b.cursor - start);
        }

        static stbtt__buf stbtt__cff_index_get(stbtt__buf b, int i)
        {
            int count, offsize, start, end;
            stbtt__buf_seek(b, 0);
            count = (int)stbtt__buf_get16(b);
            offsize = stbtt__buf_get8(b);
            STBTT_assert(i >= 0 && i < count);
            STBTT_assert(offsize >= 1 && offsize <= 4);
            stbtt__buf_skip(b, i * offsize);
            start = (int)stbtt__buf_get(b, offsize);
            end = (int)stbtt__buf_get(b, offsize);
            return stbtt__buf_range(b, 2 + (count + 1) * offsize + start, end - start);
        }

        static void stbtt__dict_get_int(stbtt__buf b, int key, out uint outvar)
        {
            stbtt__buf operands = stbtt__dict_get(b, key);
            outvar = (uint)stbtt__cff_int(operands);
        }

        static void stbtt__dict_get_ints(stbtt__buf b, int key, int outcount, uint[] outarr)
        {
            int i;
            stbtt__buf operands = stbtt__dict_get(b, key);
            for (i = 0; i < outcount && operands.cursor < operands.size; i++)
                outarr[i] = (uint)stbtt__cff_int(operands);
        }

        static stbtt__buf stbtt__dict_get(stbtt__buf b, int key)
        {
            stbtt__buf_seek(b, 0);
            while (b.cursor < b.size)
            {
                int start = b.cursor, end, op;
                while (stbtt__buf_peek8(b) >= 28)
                    stbtt__cff_skip_operand(b);
                end = b.cursor;
                op = stbtt__buf_get8(b);
                if (op == 12)
                    op = stbtt__buf_get8(b) | 0x100;
                if (op == key)
                    return stbtt__buf_range(b, start, end - start);
            }
            return stbtt__buf_range(b, 0, 0);
        }

        static stbtt__buf stbtt__get_subrs(stbtt__buf cff, stbtt__buf fontdict)
        {
            uint subrsoff = 0;
            uint[] private_loc = new uint[2] { 0, 0 };
            stbtt__buf pdict;
            stbtt__dict_get_ints(fontdict, 18, 2, private_loc);
            if (private_loc[1] == 0 || private_loc[0] == 0)
                return new(null, 0);
            pdict = stbtt__buf_range(cff, (int)private_loc[1], (int)private_loc[0]);
            stbtt__dict_get_int(pdict, 19, out subrsoff);
            if (subrsoff == 0)
                return new(null, 0);
            stbtt__buf_seek(cff, (int)(private_loc[1] + subrsoff));
            return stbtt__cff_get_index(cff);
        }

        static ushort ttUSHORT(Memory<byte> p)
        {
            return (ushort)(p.Span[0] * 256 + p.Span[1]);
        }

        static uint ttULONG(Memory<byte> p) { return (uint)((p.Span[0] << 24) + (p.Span[1] << 16) + (p.Span[2] << 8) + p.Span[3]); }

        static int ttLONG(Memory<byte> m)
        {
            var p = m.Span;
            return (p[0] << 24) + (p[1] << 16) + (p[2] << 8) + p[3];
        }

        public static uint stbtt__find_table(Memory<byte> data, int fontstart, string tag)
        {
            int num_tables = ttUSHORT(data.Slice(fontstart + 4));
            uint tabledir = (uint)fontstart + 12;
            uint i;
            for (i = 0; i < num_tables; ++i)
            {
                uint loc = tabledir + 16 * i;
                if (stbtt_tag(data.Slice((int)loc + 0), tag))
                    return ttULONG(data.Slice(((int)loc + 8)));
            }
            return 0;
        }

        public static int GetFontOffsetForIndex(Memory<byte> font_collection, int index) =>
            stbtt_GetFontOffsetForIndex_internal(font_collection, index);

        static int stbtt_GetFontOffsetForIndex_internal(Memory<byte> font_collection, int index)
        {
            // if it's just a font, there's only one valid index
            if (stbtt__isfont(font_collection))
                return index == 0 ? 0 : -1;

            // check if it's a TTC
            if (stbtt_tag(font_collection, "ttcf"))
            {
                // version 1?
                if (ttULONG(font_collection.Slice(4)) == 0x00010000 || ttULONG(font_collection.Slice(4)) == 0x00020000)
                {
                    int n = (int)ttLONG(font_collection.Slice(8));
                    if (index >= n)
                        return -1;
                    return (int)ttULONG(font_collection.Slice(12 + index * 4));
                }
            }
            return -1;
        }

        static int InitFont_internal(stbtt_fontinfo info, Memory<byte> data, int fontstart)
        {
            uint cmap, t;
            int i, numTables;

            info.data = data;
            info.fontstart = fontstart;
            info.cff = new(null, 0);

            cmap = stbtt__find_table(data, fontstart, "cmap");       // required
            info.loca = (int)stbtt__find_table(data, fontstart, "loca"); // required
            info.head = (int)stbtt__find_table(data, fontstart, "head"); // required
            info.glyf = (int)stbtt__find_table(data, fontstart, "glyf"); // required
            info.hhea = (int)stbtt__find_table(data, fontstart, "hhea"); // required
            info.hmtx = (int)stbtt__find_table(data, fontstart, "hmtx"); // required
            info.kern = (int)stbtt__find_table(data, fontstart, "kern"); // not required
            info.gpos = (int)stbtt__find_table(data, fontstart, "GPOS"); // not required

            if (cmap == 0 || info.head == 0 || info.hhea == 0 || info.hmtx == 0)
                return 0;
            if (info.glyf != 0)
            {
                // required for truetype
                if (info.loca == 0) return 0;
            }
            else
            {
                // initialization for CFF / Type2 fonts (OTF)
                stbtt__buf b, topdict, topdictidx;
                uint cstype = 2, charstrings = 0, fdarrayoff = 0, fdselectoff = 0;
                uint cff;

                cff = stbtt__find_table(data, fontstart, "CFF ");
                if (cff == 0) return 0;

                info.fontdicts = new(null, 0);
                info.fdselect = new(null, 0);

                // @TODO this should use size from table (not 512MB)
                info.cff = new(data.Slice((int)cff), 512 * 1024 * 1024);
                b = info.cff;

                // read the header
                stbtt__buf_skip(b, 2);
                stbtt__buf_seek(b, stbtt__buf_get8(b)); // hdrsize

                // @TODO the name INDEX could list multiple fonts,
                // but we just use the first one.
                stbtt__cff_get_index(b);  // name INDEX
                topdictidx = stbtt__cff_get_index(b);
                topdict = stbtt__cff_index_get(topdictidx, 0);
                stbtt__cff_get_index(b);  // string INDEX
                info.gsubrs = stbtt__cff_get_index(b);

                stbtt__dict_get_int(topdict, 17, out charstrings);
                stbtt__dict_get_int(topdict, 0x100 | 6, out cstype);
                stbtt__dict_get_int(topdict, 0x100 | 36, out fdarrayoff);
                stbtt__dict_get_int(topdict, 0x100 | 37, out fdselectoff);
                info.subrs = stbtt__get_subrs(b, topdict);

                // we only support Type 2 charstrings
                if (cstype != 2) return 0;
                if (charstrings == 0) return 0;

                if (fdarrayoff != 0)
                {
                    // looks like a CID font
                    if (fdselectoff == 0) return 0;
                    stbtt__buf_seek(b, (int)fdarrayoff);
                    info.fontdicts = stbtt__cff_get_index(b);
                    info.fdselect = stbtt__buf_range(b, (int)fdselectoff, (int)(b.size - fdselectoff));
                }

                stbtt__buf_seek(b, (int)charstrings);
                info.charstrings = stbtt__cff_get_index(b);
            }

            t = stbtt__find_table(data, fontstart, "maxp");
            if (t != 0)
                info.numGlyphs = ttUSHORT(data.Slice((int)t + 4));
            else
                info.numGlyphs = 0xffff;

            info.svg = -1;

            // find a cmap encoding table we understand *now* to avoid searching
            // later. (todo: could make this installable)
            // the same regardless of glyph.
            numTables = ttUSHORT(data.Slice((int)cmap + 2));
            info.index_map = 0;
            for (i = 0; i < numTables; ++i)
            {
                uint encoding_record = (uint)(cmap + 4 + 8 * i);
                // find an encoding we understand:
                switch (ttUSHORT(data.Slice((int)encoding_record)))
                {
                    case STBTT_PLATFORM_ID_MICROSOFT:
                        switch (ttUSHORT(data.Slice((int)encoding_record + 2)))
                        {
                            case STBTT_MS_EID_UNICODE_BMP:
                            case STBTT_MS_EID_UNICODE_FULL:
                                // MS/Unicode
                                info.index_map = (int)(cmap + ttULONG(data.Slice((int)encoding_record + 4)));
                                break;
                        }
                        break;
                    case STBTT_PLATFORM_ID_UNICODE:
                        // Mac/iOS has these
                        // all the encodingIDs are unicode, so we don't bother to check it
                        info.index_map = (int)(cmap + ttULONG(data.Slice((int)encoding_record + 4)));
                        break;
                }
            }
            if (info.index_map == 0)
                return 0;

            info.indexToLocFormat = ttUSHORT(data.Slice(info.head + 50));
            return 1;
        }
    }
}
