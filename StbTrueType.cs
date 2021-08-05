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

        const byte
            STBTT_vmove = 1,
            STBTT_vline = 2,
            STBTT_vcurve = 3,
            STBTT_vcubic = 4;

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

        static void STBTT_assert(bool cond)
        {
            if (!cond) throw new Exception();
        }

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

        public static float stbtt_ScaleForPixelHeight(stbtt_fontinfo info, float height)
        {
            int fheight = ttSHORT(info.data.Slice(info.hhea + 4)) - ttSHORT(info.data.Slice(info.hhea + 6));
            return (float)height / fheight;
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

        static stbtt__buf stbtt__get_subr(stbtt__buf idx, int n)
        {
            int count = stbtt__cff_index_count(idx);
            int bias = 107;
            if (count >= 33900)
                bias = 32768;
            else if (count >= 1240)
                bias = 1131;
            n += bias;
            if (n < 0 || n >= count)
                return new(null, 0);
            return stbtt__cff_index_get(idx, n);
        }

        static int stbtt__cff_index_count(stbtt__buf b)
        {
            stbtt__buf_seek(b, 0);
            return (int)stbtt__buf_get16(b);
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

        static byte ttBYTE(Memory<byte> p) => p.Span[0];

        static sbyte ttCHAR(Memory<byte> p) => (sbyte)p.Span[0];

        static ushort ttUSHORT(Memory<byte> p)
        {
            return (ushort)(p.Span[0] * 256 + p.Span[1]);
        }

        static short ttSHORT(Memory<byte> m)
        {
            var p = m.Span;
            return (short)(p[0] * 256 + p[1]);
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

        static int stbtt_FindGlyphIndex(stbtt_fontinfo info, int unicode_codepoint)
        {
            Memory<byte> data = info.data;
            uint index_map = (uint)info.index_map;

            ushort format = ttUSHORT(data.Slice((int)index_map + 0));
            if (format == 0)
            { // apple byte encoding
                int bytes = ttUSHORT(data.Slice((int)index_map + 2));
                if (unicode_codepoint < bytes - 6)
                    return ttBYTE(data.Slice((int)index_map + 6 + unicode_codepoint));
                return 0;
            }
            else if (format == 6)
            {
                uint first = ttUSHORT(data.Slice((int)index_map + 6));
                uint count = ttUSHORT(data.Slice((int)index_map + 8));
                if ((uint)unicode_codepoint >= first && (uint)unicode_codepoint < first + count)
                    return ttUSHORT(data.Slice((int)(index_map + 10 + (unicode_codepoint - first) * 2)));
                return 0;
            }
            else if (format == 2)
            {
                STBTT_assert(false); // @TODO: high-byte mapping for japanese/chinese/korean
                return 0;
            }
            else if (format == 4)
            { // standard mapping for windows fonts: binary search collection of ranges
                ushort segcount = (ushort)(ttUSHORT(data.Slice((int)index_map + 6)) >> 1);
                ushort searchRange = (ushort)(ttUSHORT(data.Slice((int)index_map + 8)) >> 1);
                ushort entrySelector = (ushort)(ttUSHORT(data.Slice((int)index_map + 10)));
                ushort rangeShift = (ushort)(ttUSHORT(data.Slice((int)index_map + 12)) >> 1);

                // do a binary search of the segments
                uint endCount = index_map + 14;
                uint search = endCount;

                if (unicode_codepoint > 0xffff)
                    return 0;

                // they lie from endCount .. endCount + segCount
                // but searchRange is the nearest power of two, so...
                if (unicode_codepoint >= ttUSHORT(data.Slice((int)search + rangeShift * 2)))
                    search += (uint)(rangeShift * 2);

                // now decrement to bias correctly to find smallest
                search -= 2;
                while (entrySelector != 0)
                {
                    ushort end;
                    searchRange >>= 1;
                    end = ttUSHORT(data.Slice((int)search + searchRange * 2));
                    if (unicode_codepoint > end)
                        search += (uint)searchRange * 2;
                    --entrySelector;
                }
                search += 2;

                {
                    ushort offset, start, last;
                    ushort item = (ushort)((search - endCount) >> 1);

                    start = ttUSHORT(data.Slice((int)index_map + 14 + segcount * 2 + 2 + 2 * item));
                    last = ttUSHORT(data.Slice((int)endCount + 2 * item));
                    if (unicode_codepoint < start || unicode_codepoint > last)
                        return 0;

                    offset = ttUSHORT(data.Slice((int)index_map + 14 + segcount * 6 + 2 + 2 * item));
                    if (offset == 0)
                        return (ushort)(unicode_codepoint + ttSHORT(data.Slice((int)index_map + 14 + segcount * 4 + 2 + 2 * item)));

                    return ttUSHORT(data.Slice((int)(offset + (unicode_codepoint - start) * 2 + index_map + 14 + segcount * 6 + 2 + 2 * item)));
                }
            }
            else if (format == 12 || format == 13)
            {
                uint ngroups = ttULONG(data.Slice((int)index_map + 12));
                int low, high;
                low = 0;
                high = (int)ngroups;
                // Binary search the right group.
                while (low < high)
                {
                    int mid = low + ((high - low) >> 1); // rounds down, so low <= mid < high
                    uint start_char = ttULONG(data.Slice((int)index_map + 16 + mid * 12));
                    uint end_char = ttULONG(data.Slice((int)index_map + 16 + mid * 12 + 4));
                    if ((uint)unicode_codepoint < start_char)
                        high = mid;
                    else if ((uint)unicode_codepoint > end_char)
                        low = mid + 1;
                    else
                    {
                        uint start_glyph = ttULONG(data.Slice((int)index_map + 16 + mid * 12 + 8));
                        if (format == 12)
                            return (int)(start_glyph + unicode_codepoint - start_char);
                        else // format == 13
                            return (int)start_glyph;
                    }
                }
                return 0; // not found
            }
            // @TODO
            STBTT_assert(false);
            return 0;
        }

        // Glyph

        struct stbtt_vertex
        {
            public short x, y, cx, cy, cx1, cy1;
            public byte type, padding;
        }

        struct stbtt__point
        {
            public float x, y;
        }

        static int stbtt_GetGlyphShape(stbtt_fontinfo info, int glyph_index, out stbtt_vertex[] pvertices)
        {
            if (info.cff.size == 0)
                return stbtt__GetGlyphShapeTT(info, glyph_index, out pvertices);
            else
                throw new NotImplementedException();
            //return stbtt__GetGlyphShapeT2(info, glyph_index, pvertices);
        }

        static int stbtt__GetGlyphShapeTT(stbtt_fontinfo info, int glyph_index, out stbtt_vertex[] pvertices)
        {
            short numberOfContours;
            Memory<byte> endPtsOfContours;
            Memory<byte> data = info.data;
            stbtt_vertex[] vertices = null;
            int num_vertices = 0;
            int g = stbtt__GetGlyfOffset(info, glyph_index);

            pvertices = null;

            if (g < 0)
                return 0;

            numberOfContours = ttSHORT(data.Slice(g));

            if (numberOfContours > 0)
            {
                byte flags = 0, flagcount;

                int ins, i, j = 0, m, n, next_move, off;
                int x, y, cx, cy, sx, sy, scx, scy;
                bool start_off = false, was_off = false;
                Span<byte> points;
                endPtsOfContours = data.Slice(g + 10);
                ins = ttUSHORT(data.Slice(g + 10 + numberOfContours * 2));
                points = data.Slice(g + 10 + numberOfContours * 2 + 2 + ins).Span;

                n = 1 + ttUSHORT(endPtsOfContours.Slice(numberOfContours * 2 - 2));

                m = n + 2 * numberOfContours; // a loose bound on how many vertices we might need
                vertices = new stbtt_vertex[m];

                next_move = 0;
                flagcount = 0;

                // in first pass, we load uninterpreted data into the allocated array
                // above, shifted to the end of the array so we won't overwrite it when
                // we create our final data starting from the front

                off = m - n; // starting offset for uninterpreted data, regardless of how m ends up being calculated

                // first load flags

                for (i = 0; i < n; ++i)
                {
                    if (flagcount == 0)
                    {
                        flags = points[0];
                        points = points.Slice(1);
                        if ((flags & 8) != 0)
                        {
                            flagcount = points[0];
                            points = points.Slice(1);
                        }
                    }
                    else
                        --flagcount;
                    vertices[off + i].type = flags;
                }

                // now load x coordinates
                x = 0;
                for (i = 0; i < n; ++i)
                {
                    flags = vertices[off + i].type;
                    if ((flags & 2) != 0)
                    {
                        short dx = points[0];
                        points = points.Slice(1);
                        x += (flags & 16) != 0 ? dx : -dx; // ???
                    }
                    else
                    {
                        if ((flags & 16) == 0)
                        {
                            x = x + (short)(points[0] * 256 + points[1]);
                            points = points.Slice(2);
                        }
                    }
                    vertices[off + i].x = (short)x;
                }

                // now load y coordinates
                y = 0;
                for (i = 0; i < n; ++i)
                {
                    flags = vertices[off + i].type;
                    if ((flags & 4) != 0)
                    {
                        short dy = points[0];
                        points = points.Slice(1);
                        y += (flags & 32) != 0 ? dy : -dy; // ???
                    }
                    else
                    {
                        if ((flags & 32) == 0)
                        {
                            y = y + (short)(points[0] * 256 + points[1]);
                            points = points.Slice(2);
                        }
                    }
                    vertices[off + i].y = (short)y;
                }

                // now convert them to our format
                num_vertices = 0;
                sx = sy = cx = cy = scx = scy = 0;
                for (i = 0; i < n; ++i)
                {
                    flags = vertices[off + i].type;
                    x = (short)vertices[off + i].x;
                    y = (short)vertices[off + i].y;

                    if (next_move == i)
                    {
                        if (i != 0)
                            num_vertices = stbtt__close_shape(vertices, num_vertices, was_off, start_off, sx, sy, scx, scy, cx, cy);

                        // now start the new one
                        start_off = (flags & 1) == 0;
                        if (start_off)
                        {
                            // if we start off with an off-curve point, then when we need to find a point on the curve
                            // where we can start, and we need to save some state for when we wraparound.
                            scx = x;
                            scy = y;
                            if ((vertices[off + i + 1].type & 1) == 0)
                            {
                                // next point is also a curve point, so interpolate an on-point curve
                                sx = (x + (int)vertices[off + i + 1].x) >> 1;
                                sy = (y + (int)vertices[off + i + 1].y) >> 1;
                            }
                            else
                            {
                                // otherwise just use the next point as our start point
                                sx = (int)vertices[off + i + 1].x;
                                sy = (int)vertices[off + i + 1].y;
                                ++i; // we're using point i+1 as the starting point, so skip it
                            }
                        }
                        else
                        {
                            sx = x;
                            sy = y;
                        }
                        stbtt_setvertex(ref vertices[num_vertices++], STBTT_vmove, sx, sy, 0, 0);
                        was_off = false;
                        next_move = 1 + ttUSHORT(endPtsOfContours.Slice(j * 2));
                        ++j;
                    }
                    else
                    {
                        if ((flags & 1) == 0)
                        {                // if it's a curve
                            if (was_off) // two off-curve control points in a row means interpolate an on-curve midpoint
                                stbtt_setvertex(ref vertices[num_vertices++], STBTT_vcurve, (cx + x) >> 1, (cy + y) >> 1, cx, cy);
                            cx = x;
                            cy = y;
                            was_off = true;
                        }
                        else
                        {
                            if (was_off)
                                stbtt_setvertex(ref vertices[num_vertices++], STBTT_vcurve, x, y, cx, cy);
                            else
                                stbtt_setvertex(ref vertices[num_vertices++], STBTT_vline, x, y, 0, 0);
                            was_off = false;
                        }
                    }
                }
                num_vertices = stbtt__close_shape(vertices, num_vertices, was_off, start_off, sx, sy, scx, scy, cx, cy);
            }
            else if (numberOfContours < 0)
            {
                // Compound shapes.
                bool more = true;
                Memory<byte> comp = data.Slice(g + 10);
                num_vertices = 0;
                vertices = null;
                while (more)
                {
                    ushort flags, gidx;
                    int comp_num_verts = 0, i;
                    stbtt_vertex[] comp_verts = null;
                    stbtt_vertex[] tmp;
                    var mtx = new float[] { 1, 0, 0, 1, 0, 0 };
                    float m, n;

                    flags = (ushort)ttSHORT(comp);
                    comp = comp.Slice(2);
                    gidx = (ushort)ttSHORT(comp);
                    comp = comp.Slice(2);

                    if ((flags & 2) != 0)
                    { // XY values
                        if ((flags & 1) != 0)
                        { // shorts
                            mtx[4] = ttSHORT(comp);
                            comp = comp.Slice(2);
                            mtx[5] = ttSHORT(comp);
                            comp = comp.Slice(2);
                        }
                        else
                        {
                            mtx[4] = ttCHAR(comp);
                            comp = comp.Slice(1);
                            mtx[5] = ttCHAR(comp);
                            comp = comp.Slice(1);
                        }
                    }
                    else
                    {
                        // @TODO handle matching point
                        STBTT_assert(false);
                    }
                    if ((flags & (1 << 3)) != 0)
                    { // WE_HAVE_A_SCALE
                        mtx[0] = mtx[3] = (float)ttSHORT(comp) / 16384.0f;
                        comp = comp.Slice(2);
                        mtx[1] = mtx[2] = 0;
                    }
                    else if ((flags & (1 << 6)) != 0)
                    { // WE_HAVE_AN_X_AND_YSCALE
                        mtx[0] = (float)ttSHORT(comp) / 16384.0f;
                        comp = comp.Slice(2);
                        mtx[1] = mtx[2] = 0;
                        mtx[3] = (float)ttSHORT(comp) / 16384.0f;
                        comp = comp.Slice(2);
                    }
                    else if ((flags & (1 << 7)) != 0)
                    { // WE_HAVE_A_TWO_BY_TWO
                        mtx[0] = (float)ttSHORT(comp) / 16384.0f;
                        comp = comp.Slice(2);
                        mtx[1] = (float)ttSHORT(comp) / 16384.0f;
                        comp = comp.Slice(2);
                        mtx[2] = (float)ttSHORT(comp) / 16384.0f;
                        comp = comp.Slice(2);
                        mtx[3] = (float)ttSHORT(comp) / 16384.0f;
                        comp = comp.Slice(2);
                    }

                    // Find transformation scales.
                    m = (float)Math.Sqrt(mtx[0] * mtx[0] + mtx[1] * mtx[1]);
                    n = (float)Math.Sqrt(mtx[2] * mtx[2] + mtx[3] * mtx[3]);

                    // Get indexed glyph.
                    comp_num_verts = stbtt_GetGlyphShape(info, gidx, out comp_verts);
                    if (comp_num_verts > 0)
                    {
                        // Transform vertices.
                        for (i = 0; i < comp_num_verts; ++i)
                        {
                            ref stbtt_vertex v = ref comp_verts[i];
                            short x, y;
                            x = v.x;
                            y = v.y;
                            v.x = (short)(m * (mtx[0] * x + mtx[2] * y + mtx[4]));
                            v.y = (short)(n * (mtx[1] * x + mtx[3] * y + mtx[5]));
                            x = v.cx;
                            y = v.cy;
                            v.cx = (short)(m * (mtx[0] * x + mtx[2] * y + mtx[4]));
                            v.cy = (short)(n * (mtx[1] * x + mtx[3] * y + mtx[5]));
                        }
                        // Append vertices.
                        tmp = new stbtt_vertex[num_vertices + comp_num_verts];

                        if (num_vertices > 0 && vertices != null)
                            vertices.AsSpan(0, num_vertices).CopyTo(tmp);
                        comp_verts.AsSpan(0, comp_num_verts).CopyTo(tmp.AsSpan(num_vertices));
                        vertices = tmp;
                        num_vertices += comp_num_verts;
                    }
                    // More components ?
                    more = (flags & (1 << 5)) != 0;
                }
            }
            else
            {
                // numberOfCounters == 0, do nothing
            }

            pvertices = vertices;
            return num_vertices;
        }

        static int stbtt__close_shape(stbtt_vertex[] vertices, int num_vertices, bool was_off, bool start_off,
                              int sx, int sy, int scx, int scy, int cx, int cy)
        {
            if (start_off)
            {
                if (was_off)
                    stbtt_setvertex(ref vertices[num_vertices++], STBTT_vcurve, (cx + scx) >> 1, (cy + scy) >> 1, cx, cy);
                stbtt_setvertex(ref vertices[num_vertices++], STBTT_vcurve, sx, sy, scx, scy);
            }
            else
            {
                if (was_off)
                    stbtt_setvertex(ref vertices[num_vertices++], STBTT_vcurve, sx, sy, cx, cy);
                else
                    stbtt_setvertex(ref vertices[num_vertices++], STBTT_vline, sx, sy, 0, 0);
            }
            return num_vertices;
        }

        // Bitmaps

        struct stbtt__bitmap
        {
            public int w, h, stride;
            public Memory<byte> pixels;
        }

        struct stbtt__edge
        {
            public float x0, y0, x1, y1;
            public bool invert;
        }


        public static Memory<byte> stbtt_GetCodepointBitmap(stbtt_fontinfo info, float scale_x, float scale_y, int codepoint, out int width, out int height, out int xoff, out int yoff)
        {
            return stbtt_GetCodepointBitmapSubpixel(info, scale_x, scale_y, 0.0f, 0.0f, codepoint, out width, out height, out xoff, out yoff);
        }

        static Memory<byte> stbtt_GetCodepointBitmapSubpixel(stbtt_fontinfo info, float scale_x, float scale_y, float shift_x, float shift_y, int codepoint, out int width, out int height, out int xoff, out int yoff)
        {
            return stbtt_GetGlyphBitmapSubpixel(info, scale_x, scale_y, shift_x, shift_y, stbtt_FindGlyphIndex(info, codepoint), out width, out height, out xoff, out yoff);
        }

        public static void stbtt_GetCodepointBitmapBoxSubpixel(stbtt_fontinfo font, int codepoint, float scale_x, float scale_y, float shift_x, float shift_y, out int ix0, out int iy0, out int ix1, out int iy1)
        {
            stbtt_GetGlyphBitmapBoxSubpixel(font, stbtt_FindGlyphIndex(font, codepoint), scale_x, scale_y, shift_x, shift_y, out ix0, out iy0, out ix1, out iy1);
        }

        public static void stbtt_MakeCodepointBitmapSubpixel(stbtt_fontinfo info, Memory<byte> output, int out_w, int out_h, int out_stride, float scale_x, float scale_y, float shift_x, float shift_y, int codepoint)
        {
            stbtt_MakeGlyphBitmapSubpixel(info, output, out_w, out_h, out_stride, scale_x, scale_y, shift_x, shift_y, stbtt_FindGlyphIndex(info, codepoint));
        }

        static void stbtt_MakeGlyphBitmapSubpixel(stbtt_fontinfo info, Memory<byte> output, int out_w, int out_h, int out_stride, float scale_x, float scale_y, float shift_x, float shift_y, int glyph)
        {
            int ix0, iy0;
            stbtt_vertex[] vertices;
            int num_verts = stbtt_GetGlyphShape(info, glyph, out vertices);
            stbtt__bitmap gbm;

            stbtt_GetGlyphBitmapBoxSubpixel(info, glyph, scale_x, scale_y, shift_x, shift_y, out ix0, out iy0, out _, out _);
            gbm.pixels = output;
            gbm.w = out_w;
            gbm.h = out_h;
            gbm.stride = out_stride;

            if (gbm.w != 0 && gbm.h != 0)
                stbtt_Rasterize(ref gbm, 0.35f, vertices, num_verts, scale_x, scale_y, shift_x, shift_y, ix0, iy0, true);
        }

        static Memory<byte> stbtt_GetGlyphBitmapSubpixel(stbtt_fontinfo info, float scale_x, float scale_y, float shift_x, float shift_y, int glyph, out int width, out int height, out int xoff, out int yoff)
        {
            int ix0, iy0, ix1, iy1;
            stbtt__bitmap gbm;
            stbtt_vertex[] vertices;
            int num_verts = stbtt_GetGlyphShape(info, glyph, out vertices);

            if (scale_x == 0)
                scale_x = scale_y;
            if (scale_y == 0)
            {
                if (scale_x == 0)
                {
                    (width, height, xoff, yoff) = (0, 0, 0, 0);
                    return null;
                }
                scale_y = scale_x;
            }

            stbtt_GetGlyphBitmapBoxSubpixel(info, glyph, scale_x, scale_y, shift_x, shift_y, out ix0, out iy0, out ix1, out iy1);

            // now we get the size
            gbm.w = (ix1 - ix0);
            gbm.h = (iy1 - iy0);
            gbm.pixels = null; // in case we error

            width = gbm.w;
            height = gbm.h;
            xoff = ix0;
            yoff = iy0;

            if (gbm.w != 0 && gbm.h != 0)
            {
                gbm.pixels = new byte[gbm.w * gbm.h];
                gbm.stride = gbm.w;
                stbtt_Rasterize(ref gbm, 0.35f, vertices, num_verts, scale_x, scale_y, shift_x, shift_y, ix0, iy0, true);
            }

            return gbm.pixels;
        }

        static void stbtt_GetGlyphBitmapBoxSubpixel(stbtt_fontinfo font, int glyph, float scale_x, float scale_y, float shift_x, float shift_y, out int ix0, out int iy0, out int ix1, out int iy1)
        {
            int x0 = 0, y0 = 0, x1, y1; // =0 suppresses compiler warning
            if (stbtt_GetGlyphBox(font, glyph, out x0, out y0, out x1, out y1) == 0)
            {
                // e.g. space character
                ix0 = 0;
                iy0 = 0;
                ix1 = 0;
                iy1 = 0;
            }
            else
            {

                // move to integral bboxes (treating pixels as little squares, what pixels get touched)?
                ix0 = (int)Math.Floor((float)x0 * scale_x + shift_x);
                iy0 = (int)Math.Floor(-(float)y1 * scale_y + shift_y);
                ix1 = (int)Math.Ceiling((float)x1 * scale_x + shift_x);
                iy1 = (int)Math.Ceiling(-(float)y0 * scale_y + shift_y);
            }
        }

        struct stbtt__csctx
        {
            public int bounds;
            public bool started;
            public float first_x, first_y;
            public float x, y;
            public int min_x, max_x, min_y, max_y;
            public stbtt_vertex[] pvertices;
            public int num_vertices;
        }


        static void stbtt__track_vertex(ref stbtt__csctx c, int x, int y)
        {
            if (x > c.max_x || !c.started)
                c.max_x = x;
            if (y > c.max_y || !c.started)
                c.max_y = y;
            if (x < c.min_x || !c.started)
                c.min_x = x;
            if (y < c.min_y || !c.started)
                c.min_y = y;
            c.started = true;
        }

        static void stbtt_setvertex(ref stbtt_vertex v, byte type, int x, int y, int cx, int cy)
        {
            v.type = type;
            v.x = (short)x;
            v.y = (short)y;
            v.cx = (short)cx;
            v.cy = (short)cy;
        }

        static void stbtt__csctx_v(ref stbtt__csctx c, uint type, int x, int y, int cx, int cy, int cx1, int cy1)
        {
            if (c.bounds != 0)
            {
                stbtt__track_vertex(ref c, x, y);
                if (type == STBTT_vcubic)
                {
                    stbtt__track_vertex(ref c, cx, cy);
                    stbtt__track_vertex(ref c, cx1, cy1);
                }
            }
            else
            {
                stbtt_setvertex(ref c.pvertices[c.num_vertices], (byte)type, x, y, cx, cy);
                c.pvertices[c.num_vertices].cx1 = (short)cx1;
                c.pvertices[c.num_vertices].cy1 = (short)cy1;
            }
            c.num_vertices++;
        }

        static void stbtt__csctx_close_shape(ref stbtt__csctx ctx)
        {
            if (ctx.first_x != ctx.x || ctx.first_y != ctx.y)
                stbtt__csctx_v(ref ctx, STBTT_vline, (int)ctx.first_x, (int)ctx.first_y, 0, 0, 0, 0);
        }

        static void stbtt__csctx_rmove_to(ref stbtt__csctx ctx, float dx, float dy)
        {
            stbtt__csctx_close_shape(ref ctx);
            ctx.first_x = ctx.x = ctx.x + dx;
            ctx.first_y = ctx.y = ctx.y + dy;
            stbtt__csctx_v(ref ctx, STBTT_vmove, (int)ctx.x, (int)ctx.y, 0, 0, 0, 0);
        }

        static void stbtt__csctx_rline_to(ref stbtt__csctx ctx, float dx, float dy)
        {
            ctx.x += dx;
            ctx.y += dy;
            stbtt__csctx_v(ref ctx, STBTT_vline, (int)ctx.x, (int)ctx.y, 0, 0, 0, 0);
        }

        static void stbtt__csctx_rccurve_to(ref stbtt__csctx ctx, float dx1, float dy1, float dx2, float dy2, float dx3, float dy3)
        {
            float cx1 = ctx.x + dx1;
            float cy1 = ctx.y + dy1;
            float cx2 = cx1 + dx2;
            float cy2 = cy1 + dy2;
            ctx.x = cx2 + dx3;
            ctx.y = cy2 + dy3;
            stbtt__csctx_v(ref ctx, STBTT_vcubic, (int)ctx.x, (int)ctx.y, (int)cx1, (int)cy1, (int)cx2, (int)cy2);
        }

        static int stbtt__GetGlyfOffset(stbtt_fontinfo info, int glyph_index)
        {
            int g1, g2;

            STBTT_assert(info.cff.size == 0);

            if (glyph_index >= info.numGlyphs)
                return -1; // glyph index out of range
            if (info.indexToLocFormat >= 2)
                return -1; // unknown index.glyph map format

            if (info.indexToLocFormat == 0)
            {
                g1 = info.glyf + ttUSHORT(info.data.Slice(info.loca + glyph_index * 2)) * 2;
                g2 = info.glyf + ttUSHORT(info.data.Slice(info.loca + glyph_index * 2 + 2)) * 2;
            }
            else
            {
                g1 = info.glyf + (int)ttULONG(info.data.Slice((int)(info.loca + glyph_index * 4)));
                g2 = info.glyf + (int)ttULONG(info.data.Slice((int)(info.loca + glyph_index * 4 + 4)));
            }

            return g1 == g2 ? -1 : g1; // if length is 0, return -1
        }

        static int stbtt__GetGlyphInfoT2(stbtt_fontinfo info, int glyph_index, out int x0, out int y0, out int x1, out int y1)
        {
            stbtt__csctx c = new();
            int r = stbtt__run_charstring(info, glyph_index, ref c);
            x0 = r != 0 ? c.min_x : 0;
            y0 = r != 0 ? c.min_y : 0;
            x1 = r != 0 ? c.max_x : 0;
            y1 = r != 0 ? c.max_y : 0;
            return r != 0 ? c.num_vertices : 0;
        }

        static int stbtt_GetGlyphBox(stbtt_fontinfo info, int glyph_index, out int x0, out int y0, out int x1, out int y1)
        {
            x0 = 0; y0 = 0; x1 = 0; y1 = 0;
            if (info.cff.size != 0)
            {
                stbtt__GetGlyphInfoT2(info, glyph_index, out x0, out y0, out x1, out y1);
            }
            else
            {
                int g = stbtt__GetGlyfOffset(info, glyph_index);
                if (g < 0)
                    return 0;

                x0 = ttSHORT(info.data.Slice(g + 2));
                y0 = ttSHORT(info.data.Slice(g + 4));
                x1 = ttSHORT(info.data.Slice(g + 6));
                y1 = ttSHORT(info.data.Slice(g + 8));
            }
            return 1;
        }

        static int STBTT__CSERR(string s) => 0;

        static int stbtt__run_charstring(stbtt_fontinfo info, int glyph_index, ref stbtt__csctx c)
        {
            bool in_header = true;
            bool has_subrs = false;
            int maskbits = 0, subr_stack_height = 0, sp = 0, v, i, b0;
            bool clear_stack;
            float[] s = new float[48];
            stbtt__buf[] subr_stack = new stbtt__buf[10];
            var subrs = info.subrs;
            stbtt__buf b;

            float f;

            bool goto_vlineto, goto_hcurveto;

            // this currently ignores the initial width value, which isn't needed if we have hmtx
            b = stbtt__cff_index_get(info.charstrings, glyph_index);
            while (b.cursor < b.size)
            {
                i = 0;
                clear_stack = true;
                b0 = stbtt__buf_get8(b);

                goto_vlineto = false;
                goto_hcurveto = false;

                switch (b0)
                {
                    // @TODO implement hinting
                    case 0x13: // hintmask
                    case 0x14: // cntrmask
                        if (in_header)
                            maskbits += (sp / 2); // implicit "vstem"
                        in_header = false;
                        stbtt__buf_skip(b, (maskbits + 7) / 8);
                        break;

                    case 0x01: // hstem
                    case 0x03: // vstem
                    case 0x12: // hstemhm
                    case 0x17: // vstemhm
                        maskbits += (sp / 2);
                        break;

                    case 0x15: // rmoveto
                        in_header = false;
                        if (sp < 2)
                            return STBTT__CSERR("rmoveto stack");
                        stbtt__csctx_rmove_to(ref c, s[sp - 2], s[sp - 1]);
                        break;
                    case 0x04: // vmoveto
                        in_header = false;
                        if (sp < 1)
                            return STBTT__CSERR("vmoveto stack");
                        stbtt__csctx_rmove_to(ref c, 0, s[sp - 1]);
                        break;
                    case 0x16: // hmoveto
                        in_header = false;
                        if (sp < 1)
                            return STBTT__CSERR("hmoveto stack");
                        stbtt__csctx_rmove_to(ref c, s[sp - 1], 0);
                        break;

                    case 0x05: // rlineto
                        if (sp < 2)
                            return STBTT__CSERR("rlineto stack");
                        for (; i + 1 < sp; i += 2)
                            stbtt__csctx_rline_to(ref c, s[i], s[i + 1]);
                        break;

                    // hlineto/vlineto and vhcurveto/hvcurveto alternate horizontal and vertical
                    // starting from a different place.

                    case 0x07: // vlineto
                        if (sp < 1)
                            return STBTT__CSERR("vlineto stack");
                        goto_vlineto = true;
                        goto case 0x06;
                    case 0x06: // hlineto
                        if (!goto_vlineto && sp < 1)
                            return STBTT__CSERR("hlineto stack");
                        for (; ; )
                        {
                            if (!goto_vlineto)
                            {
                                if (i >= sp)
                                    break;
                                stbtt__csctx_rline_to(ref c, s[i], 0);
                                i++;
                            }

                            goto_vlineto = false;

                            if (i >= sp)
                                break;
                            stbtt__csctx_rline_to(ref c, 0, s[i]);
                            i++;
                        }
                        break;

                    case 0x1F: // hvcurveto
                        if (sp < 4)
                            return STBTT__CSERR("hvcurveto stack");
                        goto_hcurveto = true;
                        goto case 0x1E;

                    case 0x1E: // vhcurveto
                        if (!goto_hcurveto && sp < 4)
                            return STBTT__CSERR("vhcurveto stack");
                        for (; ; )
                        {
                            if (!goto_hcurveto)
                            {
                                if (i + 3 >= sp)
                                    break;
                                stbtt__csctx_rccurve_to(ref c, 0, s[i], s[i + 1], s[i + 2], s[i + 3], (sp - i == 5) ? s[i + 4] : 0.0f);
                                i += 4;
                            }
                            goto_hcurveto = false;

                            if (i + 3 >= sp)
                                break;
                            stbtt__csctx_rccurve_to(ref c, s[i], 0, s[i + 1], s[i + 2], (sp - i == 5) ? s[i + 4] : 0.0f, s[i + 3]);
                            i += 4;
                        }
                        break;

                    case 0x08: // rrcurveto
                        if (sp < 6)
                            return STBTT__CSERR("rcurveline stack");
                        for (; i + 5 < sp; i += 6)
                            stbtt__csctx_rccurve_to(ref c, s[i], s[i + 1], s[i + 2], s[i + 3], s[i + 4], s[i + 5]);
                        break;

                    case 0x18: // rcurveline
                        if (sp < 8)
                            return STBTT__CSERR("rcurveline stack");
                        for (; i + 5 < sp - 2; i += 6)
                            stbtt__csctx_rccurve_to(ref c, s[i], s[i + 1], s[i + 2], s[i + 3], s[i + 4], s[i + 5]);
                        if (i + 1 >= sp)
                            return STBTT__CSERR("rcurveline stack");
                        stbtt__csctx_rline_to(ref c, s[i], s[i + 1]);
                        break;

                    case 0x19: // rlinecurve
                        if (sp < 8)
                            return STBTT__CSERR("rlinecurve stack");
                        for (; i + 1 < sp - 6; i += 2)
                            stbtt__csctx_rline_to(ref c, s[i], s[i + 1]);
                        if (i + 5 >= sp)
                            return STBTT__CSERR("rlinecurve stack");
                        stbtt__csctx_rccurve_to(ref c, s[i], s[i + 1], s[i + 2], s[i + 3], s[i + 4], s[i + 5]);
                        break;

                    case 0x1A: // vvcurveto
                    case 0x1B: // hhcurveto
                        if (sp < 4)
                            return STBTT__CSERR("(vv|hh)curveto stack");
                        f = 0.0f;
                        if ((sp & 1) != 0)
                        {
                            f = s[i];
                            i++;
                        }
                        for (; i + 3 < sp; i += 4)
                        {
                            if (b0 == 0x1B)
                                stbtt__csctx_rccurve_to(ref c, s[i], f, s[i + 1], s[i + 2], s[i + 3], 0.0f);
                            else
                                stbtt__csctx_rccurve_to(ref c, f, s[i], s[i + 1], s[i + 2], 0.0f, s[i + 3]);
                            f = 0.0f;
                        }
                        break;

                    case 0x0A: // callsubr
                        if (!has_subrs)
                        {
                            if (info.fdselect.size != 0)
                                subrs = stbtt__cid_get_glyph_subrs(info, glyph_index);
                            has_subrs = true;
                        }
                        goto case 0x1D;
                    // FALLTHROUGH
                    case 0x1D: // callgsubr
                        if (sp < 1)
                            return STBTT__CSERR("call(g|)subr stack");
                        v = (int)s[--sp];
                        if (subr_stack_height >= 10)
                            return STBTT__CSERR("recursion limit");
                        subr_stack[subr_stack_height++] = b;
                        b = stbtt__get_subr(b0 == 0x0A ? subrs : info.gsubrs, v);
                        if (b.size == 0)
                            return STBTT__CSERR("subr not found");
                        b.cursor = 0;
                        clear_stack = false;
                        break;

                    case 0x0B: // return
                        if (subr_stack_height <= 0)
                            return STBTT__CSERR("return outside subr");
                        b = subr_stack[--subr_stack_height];
                        clear_stack = false;
                        break;

                    case 0x0E: // endchar
                        stbtt__csctx_close_shape(ref c);
                        return 1;

                    case 0x0C:
                        { // two-byte escape
                            float dx1, dx2, dx3, dx4, dx5, dx6, dy1, dy2, dy3, dy4, dy5, dy6;
                            float dx, dy;
                            int b1 = stbtt__buf_get8(b);
                            switch (b1)
                            {
                                // @TODO These "flex" implementations ignore the flex-depth and resolution,
                                // and always draw beziers.
                                case 0x22: // hflex
                                    if (sp < 7)
                                        return STBTT__CSERR("hflex stack");
                                    dx1 = s[0];
                                    dx2 = s[1];
                                    dy2 = s[2];
                                    dx3 = s[3];
                                    dx4 = s[4];
                                    dx5 = s[5];
                                    dx6 = s[6];
                                    stbtt__csctx_rccurve_to(ref c, dx1, 0, dx2, dy2, dx3, 0);
                                    stbtt__csctx_rccurve_to(ref c, dx4, 0, dx5, -dy2, dx6, 0);
                                    break;

                                case 0x23: // flex
                                    if (sp < 13)
                                        return STBTT__CSERR("flex stack");
                                    dx1 = s[0];
                                    dy1 = s[1];
                                    dx2 = s[2];
                                    dy2 = s[3];
                                    dx3 = s[4];
                                    dy3 = s[5];
                                    dx4 = s[6];
                                    dy4 = s[7];
                                    dx5 = s[8];
                                    dy5 = s[9];
                                    dx6 = s[10];
                                    dy6 = s[11];
                                    //fd is s[12]
                                    stbtt__csctx_rccurve_to(ref c, dx1, dy1, dx2, dy2, dx3, dy3);
                                    stbtt__csctx_rccurve_to(ref c, dx4, dy4, dx5, dy5, dx6, dy6);
                                    break;

                                case 0x24: // hflex1
                                    if (sp < 9)
                                        return STBTT__CSERR("hflex1 stack");
                                    dx1 = s[0];
                                    dy1 = s[1];
                                    dx2 = s[2];
                                    dy2 = s[3];
                                    dx3 = s[4];
                                    dx4 = s[5];
                                    dx5 = s[6];
                                    dy5 = s[7];
                                    dx6 = s[8];
                                    stbtt__csctx_rccurve_to(ref c, dx1, dy1, dx2, dy2, dx3, 0);
                                    stbtt__csctx_rccurve_to(ref c, dx4, 0, dx5, dy5, dx6, -(dy1 + dy2 + dy5));
                                    break;

                                case 0x25: // flex1
                                    if (sp < 11)
                                        return STBTT__CSERR("flex1 stack");
                                    dx1 = s[0];
                                    dy1 = s[1];
                                    dx2 = s[2];
                                    dy2 = s[3];
                                    dx3 = s[4];
                                    dy3 = s[5];
                                    dx4 = s[6];
                                    dy4 = s[7];
                                    dx5 = s[8];
                                    dy5 = s[9];
                                    dx6 = dy6 = s[10];
                                    dx = dx1 + dx2 + dx3 + dx4 + dx5;
                                    dy = dy1 + dy2 + dy3 + dy4 + dy5;
                                    if (Math.Abs(dx) > Math.Abs(dy))
                                        dy6 = -dy;
                                    else
                                        dx6 = -dx;
                                    stbtt__csctx_rccurve_to(ref c, dx1, dy1, dx2, dy2, dx3, dy3);
                                    stbtt__csctx_rccurve_to(ref c, dx4, dy4, dx5, dy5, dx6, dy6);
                                    break;

                                default:
                                    return STBTT__CSERR("unimplemented");
                            }
                        }
                        break;

                    default:
                        if (b0 != 255 && b0 != 28 && b0 < 32)
                            return STBTT__CSERR("reserved operator");

                        // push immediate
                        if (b0 == 255)
                        {
                            f = (float)(int)stbtt__buf_get32(b) / 0x10000;
                        }
                        else
                        {
                            stbtt__buf_skip(b, -1);
                            f = (float)(short)stbtt__cff_int(b);
                        }
                        if (sp >= 48)
                            return STBTT__CSERR("push stack overflow");
                        s[sp++] = f;
                        clear_stack = false;
                        break;
                }
                if (clear_stack)
                    sp = 0;
            }
            return STBTT__CSERR("no endchar");
        }

        static void stbtt_Rasterize(ref stbtt__bitmap result, float flatness_in_pixels, stbtt_vertex[] vertices, int num_verts, float scale_x, float scale_y, float shift_x, float shift_y, int x_off, int y_off, bool invert)
        {
            float scale = scale_x > scale_y ? scale_y : scale_x;
            int winding_count = 0;
            int[] winding_lengths;
            stbtt__point[] windings = stbtt_FlattenCurves(vertices, num_verts, flatness_in_pixels / scale, out winding_lengths, out winding_count);
            if (windings != null)
            {
                stbtt__rasterize(ref result, windings, winding_lengths, winding_count, scale_x, scale_y, shift_x, shift_y, x_off, y_off, invert);
            }
        }

        public static int stbtt_GetCodepointKernAdvance(stbtt_fontinfo info, int ch1, int ch2)
        {
            if (info.kern == 0 && info.gpos == 0) // if no kerning table, don't waste time looking up both codepoint.glyphs
                return 0;
            return stbtt_GetGlyphKernAdvance(info, stbtt_FindGlyphIndex(info, ch1), stbtt_FindGlyphIndex(info, ch2));
        }

        static int stbtt_GetGlyphKernAdvance(stbtt_fontinfo info, int g1, int g2)
        {
            int xAdvance = 0;

            if (info.gpos != 0)
                xAdvance += stbtt__GetGlyphGPOSInfoAdvance(info, g1, g2);
            else if (info.kern != 0)
                xAdvance += stbtt__GetGlyphKernInfoAdvance(info, g1, g2);

            return xAdvance;
        }

        static int stbtt__GetGlyphKernInfoAdvance(stbtt_fontinfo info, int glyph1, int glyph2)
        {
            Memory<byte> data = info.data.Slice(info.kern);
            uint needle, straw;
            int l, r, m;

            // we only look at the first table. it must be 'horizontal' and format 0.
            if (info.kern == 0)
                return 0;
            if (ttUSHORT(data.Slice(2)) < 1) // number of tables, need at least 1
                return 0;
            if (ttUSHORT(data.Slice(8)) != 1) // horizontal flag must be set in format
                return 0;

            l = 0;
            r = ttUSHORT(data.Slice(10)) - 1;
            needle = (uint)(glyph1 << 16 | glyph2);
            while (l <= r)
            {
                m = (l + r) >> 1;
                straw = ttULONG(data.Slice(18 + (m * 6))); // note: unaligned read
                if (needle < straw)
                    r = m - 1;
                else if (needle > straw)
                    l = m + 1;
                else
                    return ttSHORT(data.Slice(22 + (m * 6)));
            }
            return 0;
        }

        static int stbtt__GetGlyphGPOSInfoAdvance(stbtt_fontinfo info, int glyph1, int glyph2)
        {
            ushort lookupListOffset;
            Memory<byte> lookupList;
            ushort lookupCount;
            Memory<byte> data;
            int i, sti;

            if (info.gpos == 0)
                return 0;

            data = info.data.Slice(info.gpos);

            if (ttUSHORT(data.Slice(0)) != 1)
                return 0; // Major version 1
            if (ttUSHORT(data.Slice(2)) != 0)
                return 0; // Minor version 0

            lookupListOffset = ttUSHORT(data.Slice(8));
            lookupList = data.Slice(lookupListOffset);
            lookupCount = ttUSHORT(lookupList);

            for (i = 0; i < lookupCount; ++i)
            {
                ushort lookupOffset = ttUSHORT(lookupList.Slice(2 + 2 * i));
                Memory<byte> lookupTable = lookupList.Slice(lookupOffset);

                ushort lookupType = ttUSHORT(lookupTable);
                ushort subTableCount = ttUSHORT(lookupTable.Slice(4));
                Memory<byte> subTableOffsets = lookupTable.Slice(6);
                if (lookupType != 2) // Pair Adjustment Positioning Subtable
                    continue;

                for (sti = 0; sti < subTableCount; sti++)
                {
                    ushort subtableOffset = ttUSHORT(subTableOffsets.Slice(2 * sti));
                    Memory<byte> table = lookupTable.Slice(subtableOffset);
                    ushort posFormat = ttUSHORT(table);
                    ushort coverageOffset = ttUSHORT(table.Slice(2));
                    int coverageIndex = stbtt__GetCoverageIndex(table.Slice(coverageOffset), glyph1);
                    if (coverageIndex == -1)
                        continue;

                    switch (posFormat)
                    {
                        case 1:
                            {
                                int l, r, m;
                                int straw, needle;
                                ushort valueFormat1 = ttUSHORT(table.Slice(4));
                                ushort valueFormat2 = ttUSHORT(table.Slice(6));
                                if (valueFormat1 == 4 && valueFormat2 == 0)
                                { // Support more formats?
                                    int valueRecordPairSizeInBytes = 2;
                                    ushort pairSetCount = ttUSHORT(table.Slice(8));
                                    ushort pairPosOffset = ttUSHORT(table.Slice(10 + 2 * coverageIndex));
                                    Memory<byte> pairValueTable = table.Slice(pairPosOffset);
                                    ushort pairValueCount = ttUSHORT(pairValueTable);
                                    Memory<byte> pairValueArray = pairValueTable.Slice(2);

                                    if (coverageIndex >= pairSetCount)
                                        return 0;

                                    needle = glyph2;
                                    r = pairValueCount - 1;
                                    l = 0;

                                    // Binary search.
                                    while (l <= r)
                                    {
                                        ushort secondGlyph;
                                        Memory<byte> pairValue;
                                        m = (l + r) >> 1;
                                        pairValue = pairValueArray.Slice((2 + valueRecordPairSizeInBytes) * m);
                                        secondGlyph = ttUSHORT(pairValue);
                                        straw = secondGlyph;
                                        if (needle < straw)
                                            r = m - 1;
                                        else if (needle > straw)
                                            l = m + 1;
                                        else
                                        {
                                            short xAdvance = ttSHORT(pairValue.Slice(2));
                                            return xAdvance;
                                        }
                                    }
                                }
                                else
                                    return 0;
                                break;
                            }

                        case 2:
                            {
                                ushort valueFormat1 = ttUSHORT(table.Slice(4));
                                ushort valueFormat2 = ttUSHORT(table.Slice(6));
                                if (valueFormat1 == 4 && valueFormat2 == 0)
                                { // Support more formats?
                                    ushort classDef1Offset = ttUSHORT(table.Slice(8));
                                    ushort classDef2Offset = ttUSHORT(table.Slice(10));
                                    int glyph1class = stbtt__GetGlyphClass(table.Slice(classDef1Offset), glyph1);
                                    int glyph2class = stbtt__GetGlyphClass(table.Slice(classDef2Offset), glyph2);

                                    ushort class1Count = ttUSHORT(table.Slice(12));
                                    ushort class2Count = ttUSHORT(table.Slice(14));
                                    Memory<byte> class1Records, class2Records;
                                    short xAdvance;

                                    if (glyph1class < 0 || glyph1class >= class1Count)
                                        return 0; // malformed
                                    if (glyph2class < 0 || glyph2class >= class2Count)
                                        return 0; // malformed

                                    class1Records = table.Slice(16);
                                    class2Records = class1Records.Slice(2 * (glyph1class * class2Count));
                                    xAdvance = ttSHORT(class2Records.Slice(2 * glyph2class));
                                    return xAdvance;
                                }
                                else
                                    return 0;
                                //break;
                            }

                        default:
                            return 0; // Unsupported position format
                    }
                }
            }

            return 0;
        }

        public static int stbtt__GetCoverageIndex(Memory<byte> coverageTable, int glyph)
        {
            ushort coverageFormat = ttUSHORT(coverageTable);
            switch (coverageFormat)
            {
                case 1:
                    {
                        ushort glyphCount = ttUSHORT(coverageTable.Slice(2));

                        // Binary search.
                        int l = 0, r = glyphCount - 1, m;
                        int straw, needle = glyph;
                        while (l <= r)
                        {
                            Memory<byte> glyphArray = coverageTable.Slice(4);
                            ushort glyphID;
                            m = (l + r) >> 1;
                            glyphID = ttUSHORT(glyphArray.Slice(2 * m));
                            straw = glyphID;
                            if (needle < straw)
                                r = m - 1;
                            else if (needle > straw)
                                l = m + 1;
                            else
                            {
                                return m;
                            }
                        }
                        break;
                    }

                case 2:
                    {
                        ushort rangeCount = ttUSHORT(coverageTable.Slice(2));
                        Memory<byte> rangeArray = coverageTable.Slice(4);

                        // Binary search.
                        int l = 0, r = rangeCount - 1, m;
                        int strawStart, strawEnd, needle = glyph;
                        while (l <= r)
                        {
                            Memory<byte> rangeRecord;
                            m = (l + r) >> 1;
                            rangeRecord = rangeArray.Slice(6 * m);
                            strawStart = ttUSHORT(rangeRecord);
                            strawEnd = ttUSHORT(rangeRecord.Slice(2));
                            if (needle < strawStart)
                                r = m - 1;
                            else if (needle > strawEnd)
                                l = m + 1;
                            else
                            {
                                ushort startCoverageIndex = ttUSHORT(rangeRecord.Slice(4));
                                return startCoverageIndex + glyph - strawStart;
                            }
                        }
                        break;
                    }

                default:
                    return -1; // unsupported
            }

            return -1;
        }

        static int stbtt__GetGlyphClass(Memory<byte> classDefTable, int glyph)
        {
            ushort classDefFormat = ttUSHORT(classDefTable);
            switch (classDefFormat)
            {
                case 1:
                    {
                        ushort startGlyphID = ttUSHORT(classDefTable.Slice(2));
                        ushort glyphCount = ttUSHORT(classDefTable.Slice(4));
                        Memory<byte> classDef1ValueArray = classDefTable.Slice(6);

                        if (glyph >= startGlyphID && glyph < startGlyphID + glyphCount)
                            return (int)ttUSHORT(classDef1ValueArray.Slice(2 * (glyph - startGlyphID)));
                        break;
                    }

                case 2:
                    {
                        ushort classRangeCount = ttUSHORT(classDefTable.Slice(2));
                        Memory<byte> classRangeRecords = classDefTable.Slice(4);

                        // Binary search.
                        int l = 0, r = classRangeCount - 1, m;
                        int strawStart, strawEnd, needle = glyph;
                        while (l <= r)
                        {
                            Memory<byte> classRangeRecord;
                            m = (l + r) >> 1;
                            classRangeRecord = classRangeRecords.Slice(6 * m);
                            strawStart = ttUSHORT(classRangeRecord);
                            strawEnd = ttUSHORT(classRangeRecord.Slice(2));
                            if (needle < strawStart)
                                r = m - 1;
                            else if (needle > strawEnd)
                                l = m + 1;
                            else
                                return (int)ttUSHORT(classRangeRecord.Slice(4));
                        }
                        break;
                    }

                default:
                    return -1; // Unsupported definition type, return an error.
            }

            // "All glyphs not assigned to a class fall into class 0". (OpenType spec)
            return 0;
        }


        public static void stbtt_GetFontVMetrics(stbtt_fontinfo info, out int ascent, out int descent, out int lineGap)
        {
            ascent = ttSHORT(info.data.Slice(info.hhea + 4));
            descent = ttSHORT(info.data.Slice(info.hhea + 6));
            lineGap = ttSHORT(info.data.Slice(info.hhea + 8));
        }

        public static void stbtt_GetCodepointHMetrics(stbtt_fontinfo info, int codepoint, out int advanceWidth, out int leftSideBearing)
        {
            stbtt_GetGlyphHMetrics(info, stbtt_FindGlyphIndex(info, codepoint), out advanceWidth, out leftSideBearing);
        }

        public static void stbtt_GetGlyphHMetrics(stbtt_fontinfo info, int glyph_index, out int advanceWidth, out int leftSideBearing)
        {
            ushort numOfLongHorMetrics = ttUSHORT(info.data.Slice(info.hhea + 34));
            if (glyph_index < numOfLongHorMetrics)
            {
                advanceWidth = ttSHORT(info.data.Slice(info.hmtx + 4 * glyph_index));
                leftSideBearing = ttSHORT(info.data.Slice(info.hmtx + 4 * glyph_index + 2));
            }
            else
            {
                advanceWidth = ttSHORT(info.data.Slice(info.hmtx + 4 * (numOfLongHorMetrics - 1)));
                leftSideBearing = ttSHORT(info.data.Slice(info.hmtx + 4 * numOfLongHorMetrics + 2 * (glyph_index - numOfLongHorMetrics)));
            }
        }

        static void stbtt__add_point(stbtt__point[] points, int n, float x, float y)
        {
            if (points == null)
                return; // during first pass, it's unallocated
            points[n].x = x;
            points[n].y = y;
        }

        static stbtt__point[] stbtt_FlattenCurves(stbtt_vertex[] vertices, int num_verts, float objspace_flatness, out int[] contour_lengths, out int num_contours)
        {
            stbtt__point[] points = null;
            int num_points = 0;

            float objspace_flatness_squared = objspace_flatness * objspace_flatness;
            int i, n = 0, start = 0, pass;

            // count how many "moves" there are to get the contour count
            for (i = 0; i < num_verts; ++i)
                if (vertices[i].type == STBTT_vmove)
                    ++n;

            num_contours = n;
            if (n == 0)
            {
                contour_lengths = null;
                return null;
            }

            contour_lengths = new int[n];

            // make two passes through the points so we don't need to realloc
            for (pass = 0; pass < 2; ++pass)
            {
                float x = 0, y = 0;
                if (pass == 1)
                {
                    points = new stbtt__point[num_points];
                }
                num_points = 0;
                n = -1;
                for (i = 0; i < num_verts; ++i)
                {
                    switch (vertices[i].type)
                    {
                        case STBTT_vmove:
                            // start the next contour
                            if (n >= 0)
                                contour_lengths[n] = num_points - start;
                            ++n;
                            start = num_points;

                            x = vertices[i].x; y = vertices[i].y;
                            stbtt__add_point(points, num_points++, x, y);
                            break;
                        case STBTT_vline:
                            x = vertices[i].x; y = vertices[i].y;
                            stbtt__add_point(points, num_points++, x, y);
                            break;
                        case STBTT_vcurve:
                            stbtt__tesselate_curve(points, ref num_points, x, y,
                                                   vertices[i].cx, vertices[i].cy,
                                                   vertices[i].x, vertices[i].y,
                                                   objspace_flatness_squared, 0);
                            x = vertices[i].x; y = vertices[i].y;
                            break;
                        case STBTT_vcubic:
                            stbtt__tesselate_cubic(points, ref num_points, x, y,
                                                   vertices[i].cx, vertices[i].cy,
                                                   vertices[i].cx1, vertices[i].cy1,
                                                   vertices[i].x, vertices[i].y,
                                                   objspace_flatness_squared, 0);
                            x = vertices[i].x; y = vertices[i].y;
                            break;
                    }
                }
                contour_lengths[n] = num_points - start;
            }

            return points;
        }

        // tessellate until threshold p is happy... @TODO warped to compensate for non-linear stretching
        static int stbtt__tesselate_curve(stbtt__point[] points, ref int num_points, float x0, float y0, float x1, float y1, float x2, float y2, float objspace_flatness_squared, int n)
        {
            // midpoint
            float mx = (x0 + 2 * x1 + x2) / 4;
            float my = (y0 + 2 * y1 + y2) / 4;
            // versus directly drawn line
            float dx = (x0 + x2) / 2 - mx;
            float dy = (y0 + y2) / 2 - my;
            if (n > 16) // 65536 segments on one curve better be enough!
                return 1;
            if (dx * dx + dy * dy > objspace_flatness_squared)
            { // half-pixel error allowed... need to be smaller if AA
                stbtt__tesselate_curve(points, ref num_points, x0, y0, (x0 + x1) / 2.0f, (y0 + y1) / 2.0f, mx, my, objspace_flatness_squared, n + 1);
                stbtt__tesselate_curve(points, ref num_points, mx, my, (x1 + x2) / 2.0f, (y1 + y2) / 2.0f, x2, y2, objspace_flatness_squared, n + 1);
            }
            else
            {
                stbtt__add_point(points, num_points, x2, y2);
                num_points = num_points + 1;
            }
            return 1;
        }

        static void stbtt__tesselate_cubic(stbtt__point[] points, ref int num_points, float x0, float y0, float x1, float y1, float x2, float y2, float x3, float y3, float objspace_flatness_squared, int n)
        {
            // @TODO this "flatness" calculation is just made-up nonsense that seems to work well enough
            float dx0 = x1 - x0;
            float dy0 = y1 - y0;
            float dx1 = x2 - x1;
            float dy1 = y2 - y1;
            float dx2 = x3 - x2;
            float dy2 = y3 - y2;
            float dx = x3 - x0;
            float dy = y3 - y0;
            float longlen = (float)(Math.Sqrt(dx0 * dx0 + dy0 * dy0) + Math.Sqrt(dx1 * dx1 + dy1 * dy1) + Math.Sqrt(dx2 * dx2 + dy2 * dy2));
            float shortlen = (float)Math.Sqrt(dx * dx + dy * dy);
            float flatness_squared = longlen * longlen - shortlen * shortlen;

            if (n > 16) // 65536 segments on one curve better be enough!
                return;

            if (flatness_squared > objspace_flatness_squared)
            {
                float x01 = (x0 + x1) / 2;
                float y01 = (y0 + y1) / 2;
                float x12 = (x1 + x2) / 2;
                float y12 = (y1 + y2) / 2;
                float x23 = (x2 + x3) / 2;
                float y23 = (y2 + y3) / 2;

                float xa = (x01 + x12) / 2;
                float ya = (y01 + y12) / 2;
                float xb = (x12 + x23) / 2;
                float yb = (y12 + y23) / 2;

                float mx = (xa + xb) / 2;
                float my = (ya + yb) / 2;

                stbtt__tesselate_cubic(points, ref num_points, x0, y0, x01, y01, xa, ya, mx, my, objspace_flatness_squared, n + 1);
                stbtt__tesselate_cubic(points, ref num_points, mx, my, xb, yb, x23, y23, x3, y3, objspace_flatness_squared, n + 1);
            }
            else
            {
                stbtt__add_point(points, num_points, x3, y3);
                num_points = num_points + 1;
            }
        }

        static stbtt__buf stbtt__cid_get_glyph_subrs(stbtt_fontinfo info, int glyph_index)
        {
            stbtt__buf fdselect = info.fdselect;
            int nranges, start, end, v, fmt, fdselector = -1, i;

            stbtt__buf_seek(fdselect, 0);
            fmt = stbtt__buf_get8(fdselect);
            if (fmt == 0)
            {
                // untested
                stbtt__buf_skip(fdselect, glyph_index);
                fdselector = stbtt__buf_get8(fdselect);
            }
            else if (fmt == 3)
            {
                nranges = (int)stbtt__buf_get16(fdselect);
                start = (int)stbtt__buf_get16(fdselect);
                for (i = 0; i < nranges; i++)
                {
                    v = stbtt__buf_get8(fdselect);
                    end = (int)stbtt__buf_get16(fdselect);
                    if (glyph_index >= start && glyph_index < end)
                    {
                        fdselector = v;
                        break;
                    }
                    start = end;
                }
            }
            //if (fdselector == -1)
            //    stbtt__new_buf(NULL, 0);
            return stbtt__get_subrs(info.cff, stbtt__cff_index_get(info.fontdicts, fdselector));
        }


        static void stbtt__rasterize(ref stbtt__bitmap result, stbtt__point[] pts, int[] wcount, int windings, float scale_x, float scale_y, float shift_x, float shift_y, int off_x, int off_y, bool invert)
        {
            float y_scale_inv = invert ? -scale_y : scale_y;
            stbtt__edge[] e;
            int n, i, j, k, m;
            int vsubsample = 1;
            // vsubsample should divide 255 evenly; otherwise we won't reach full opacity

            // now we have to blow out the windings into explicit edge lists
            n = 0;
            for (i = 0; i < windings; ++i)
                n += wcount[i];

            e = new stbtt__edge[n + 1]; // add an extra one as a sentinel
            n = 0;

            m = 0;
            for (i = 0; i < windings; ++i)
            {
                Span<stbtt__point> p = pts.AsSpan().Slice(m);
                m += wcount[i];
                j = wcount[i] - 1;
                for (k = 0; k < wcount[i]; j = k++)
                {
                    int a = k, b = j;
                    // skip the edge if horizontal
                    if (p[j].y == p[k].y)
                        continue;
                    // add edge from j to k to the list
                    e[n].invert = false;
                    if (invert ? p[j].y > p[k].y : p[j].y < p[k].y)
                    {
                        e[n].invert = true;
                        a = j; b = k;
                    }
                    e[n].x0 = p[a].x * scale_x + shift_x;
                    e[n].y0 = (p[a].y * y_scale_inv + shift_y) * vsubsample;
                    e[n].x1 = p[b].x * scale_x + shift_x;
                    e[n].y1 = (p[b].y * y_scale_inv + shift_y) * vsubsample;
                    ++n;
                }
            }

            // now sort the edges by their highest point (should snap to integer, and then by x)
            //STBTT_sort(e, n, sizeof(e[0]), stbtt__edge_compare);
            stbtt__sort_edges(e, n);

            // now, traverse the scanlines and find the intersections on each scanline, use xor winding rule
            stbtt__rasterize_sorted_edges(ref result, e, n, vsubsample, off_x, off_y);
        }

        class stbtt__active_edge
        {
            public stbtt__active_edge next;
            public float fx, fdx, fdy;
            public float direction;
            public float sy;
            public float ey;

            public stbtt__active_edge(stbtt__edge e, int off_x, float start_point)
            {
                float dxdy = (e.x1 - e.x0) / (e.y1 - e.y0);
                fdx = dxdy;
                fdy = dxdy != 0.0f ? (1.0f / dxdy) : 0.0f;
                fx = e.x0 + dxdy * (start_point - e.y0);
                fx -= off_x;
                direction = e.invert ? 1.0f : -1.0f;
                sy = e.y0;
                ey = e.y1;
                next = null;
            }
        }

        static void stbtt__rasterize_sorted_edges(ref stbtt__bitmap result, Span<stbtt__edge> e, int n, int vsubsample, int off_x, int off_y)
        {
            stbtt__active_edge active = null;
            int y, j = 0, i;
            Span<float> scanline, scanline2;

            scanline = new float[result.w * 2 + 1];
            scanline2 = scanline.Slice(result.w);

            y = off_y;
            e[n].y0 = (float)(off_y + result.h) + 1;

            int q = 0;
            stbtt__edge e2 = e[0];

            while (j < result.h)
            {
                // find center of pixel for this scanline
                float scan_y_top = y + 0.0f;
                float scan_y_bottom = y + 1.0f;
                ref stbtt__active_edge step = ref active;

                scanline.Slice(0, result.w).Fill(0);
                scanline2.Slice(0, result.w + 1).Fill(0);

                // update all active edges;
                // remove all active edges that terminate before the top of this scanline
                while (step != null)
                {
                    stbtt__active_edge z = step;
                    if (z.ey <= scan_y_top)
                    {
                        step = z.next; // delete from list
                        STBTT_assert(z.direction != 0);
                        z.direction = 0;
                    }
                    else
                    {
                        step = ref step.next; // advance through list
                    }
                }

                // insert all edges that start before the bottom of this scanline
                while (e2.y0 <= scan_y_bottom)
                {
                    if (e2.y0 != e2.y1)
                    {
                        stbtt__active_edge z = new(e2, off_x, scan_y_top);
                        if (z != null)
                        {
                            if (j == 0 && off_y != 0)
                            {
                                if (z.ey < scan_y_top)
                                {
                                    // this can happen due to subpixel positioning and some kind of fp rounding error i think
                                    z.ey = scan_y_top;
                                }
                            }
                            STBTT_assert(z.ey >= scan_y_top); // if we get really unlucky a tiny bit of an edge can be out of bounds
                                                              // insert at front
                            z.next = active;
                            active = z;
                        }
                    }

                    q++;
                    e2 = e[q];
                }

                // now process all active edges
                if (active != null)
                {
                    stbtt__fill_active_edges_new(scanline, scanline2, result.w, active, scan_y_top);
                }


                float sum = 0;
                for (i = 0; i < result.w; ++i)
                {
                    float k;
                    int m;
                    sum += scanline2[i];
                    k = scanline[i] + sum;
                    k = (float)Math.Abs(k) * 255 + 0.5f;
                    m = (int)k;
                    if (m > 255)
                        m = 255;
                    result.pixels.Span[j * result.stride + i] = (byte)m;
                }

                // advance all the edges
                step = ref active;
                while (step != null)
                {
                    stbtt__active_edge z = step;
                    z.fx += z.fdx;         // advance to position for current scanline
                    step = ref step.next; // advance through list
                }

                ++y;
                ++j;
            }
        }

        static void stbtt__fill_active_edges_new(Span<float> scanline, Span<float> scanline_fill, int len, stbtt__active_edge e, float y_top)
        {
            float y_bottom = y_top + 1;

            while (e != null)
            {
                // brute force every pixel

                // compute intersection points with top & bottom
                STBTT_assert(e.ey >= y_top);

                if (e.fdx == 0)
                {
                    float x0 = e.fx;
                    if (x0 < len)
                    {
                        if (x0 >= 0)
                        {
                            stbtt__handle_clipped_edge(scanline, (int)x0, e, x0, y_top, x0, y_bottom);
                            stbtt__handle_clipped_edge(scanline_fill, (int)x0 + 1, e, x0, y_top, x0, y_bottom);
                        }
                        else
                        {
                            stbtt__handle_clipped_edge(scanline_fill, 0, e, x0, y_top, x0, y_bottom);
                        }
                    }
                }
                else
                {
                    float x0 = e.fx;
                    float dx = e.fdx;
                    float xb = x0 + dx;
                    float x_top, x_bottom;
                    float sy0, sy1;
                    float dy = e.fdy;
                    STBTT_assert(e.sy <= y_bottom && e.ey >= y_top);

                    // compute endpoints of line segment clipped to this scanline (if the
                    // line segment starts on this scanline. x0 is the intersection of the
                    // line with y_top, but that may be off the line segment.
                    if (e.sy > y_top)
                    {
                        x_top = x0 + dx * (e.sy - y_top);
                        sy0 = e.sy;
                    }
                    else
                    {
                        x_top = x0;
                        sy0 = y_top;
                    }
                    if (e.ey < y_bottom)
                    {
                        x_bottom = x0 + dx * (e.ey - y_top);
                        sy1 = e.ey;
                    }
                    else
                    {
                        x_bottom = xb;
                        sy1 = y_bottom;
                    }

                    if (x_top >= 0 && x_bottom >= 0 && x_top < len && x_bottom < len)
                    {
                        // from here on, we don't have to range check x values

                        if ((int)x_top == (int)x_bottom)
                        {
                            float height;
                            // simple case, only spans one pixel
                            int x = (int)x_top;
                            height = sy1 - sy0;
                            STBTT_assert(x >= 0 && x < len);
                            scanline[x] += e.direction * (1 - ((x_top - x) + (x_bottom - x)) / 2) * height;
                            scanline_fill[x + 1] += e.direction * height; // everything right of this pixel is filled
                        }
                        else
                        {
                            int x, x1, x2;
                            float y_crossing, y_final, step, sign, area;
                            // covers 2+ pixels
                            if (x_top > x_bottom)
                            {
                                // flip scanline vertically; signed area is the same
                                float t;
                                sy0 = y_bottom - (sy0 - y_top);
                                sy1 = y_bottom - (sy1 - y_top);
                                t = sy0;
                                sy0 = sy1;
                                sy1 = t;
                                t = x_bottom;
                                x_bottom = x_top;
                                x_top = t;
                                dx = -dx;
                                dy = -dy;
                                t = x0;
                                x0 = xb;
                                xb = t;
                            }
                            STBTT_assert(dy >= 0);
                            STBTT_assert(dx >= 0);

                            x1 = (int)x_top;
                            x2 = (int)x_bottom;
                            // compute intersection with y axis at x1+1
                            y_crossing = (x1 + 1 - x0) * dy + y_top;
                            // if x2 is right at the right edge of x1, y_crossing can blow up, github #1057
                            if (y_crossing > y_bottom)
                                y_crossing = y_bottom;

                            sign = e.direction;
                            // area of the rectangle covered from y0..y_crossing
                            area = sign * (y_crossing - sy0);
                            // area of the triangle (x_top,y0), (x+1,y0), (x+1,y_crossing)
                            scanline[x1] += area * (x1 + 1 - x_top) / 2;

                            // check if final y_crossing is blown up; no test case for this
                            y_final = y_crossing + dy * (x2 - (x1 + 1)); // advance y by number of steps taken below
                            if (y_final > y_bottom)
                            {
                                y_final = y_bottom;
                                dy = (y_final - y_crossing) / (x2 - (x1 + 1)); // if denom=0, y_final = y_crossing, so y_final <= y_bottom
                            }

                            step = sign * dy * 1; // dy is dy/dx, change in y for every 1 change in x, which is also how much pixel area changes for each step in x
                            for (x = x1 + 1; x < x2; ++x)
                            {
                                scanline[x] += area + step / 2; // area of parallelogram is step/2
                                area += step;
                            }
                            STBTT_assert(Math.Abs(area) <= 1.01f); // accumulated error from area += step unless we round step down

                            // area of the triangle (x2,y_crossing), (x_bottom,y1), (x2,y1)
                            scanline[x2] += area + sign * (x_bottom - x2) / 2 * (sy1 - y_crossing);

                            scanline_fill[x2 + 1] += sign * (sy1 - sy0);
                        }
                    }
                    else
                    {
                        // if edge goes outside of box we're drawing, we require
                        // clipping logic. since this does not match the intended use
                        // of this library, we use a different, very slow brute
                        // force implementation
                        int x;
                        for (x = 0; x < len; ++x)
                        {
                            // cases:
                            //
                            // there can be up to two intersections with the pixel. any intersection
                            // with left or right edges can be handled by splitting into two (or three)
                            // regions. intersections with top & bottom do not necessitate case-wise logic.
                            //
                            // the old way of doing this found the intersections with the left & right edges,
                            // then used some simple logic to produce up to three segments in sorted order
                            // from top-to-bottom. however, this had a problem: if an x edge was epsilon
                            // across the x border, then the corresponding y position might not be distinct
                            // from the other y segment, and it might ignored as an empty segment. to avoid
                            // that, we need to explicitly produce segments based on x positions.

                            // rename variables to clearly-defined pairs
                            float y0 = y_top;
                            float x1 = (float)(x);
                            float x2 = (float)(x + 1);
                            float x3 = xb;
                            float y3 = y_bottom;

                            // x = e.x + e.dx * (y-y_top)
                            // (y-y_top) = (x - e.x) / e.dx
                            // y = (x - e.x) / e.dx + y_top
                            float y1 = (x - x0) / dx + y_top;
                            float y2 = (x + 1 - x0) / dx + y_top;

                            if (x0 < x1 && x3 > x2)
                            { // three segments descending down-right
                                stbtt__handle_clipped_edge(scanline, x, e, x0, y0, x1, y1);
                                stbtt__handle_clipped_edge(scanline, x, e, x1, y1, x2, y2);
                                stbtt__handle_clipped_edge(scanline, x, e, x2, y2, x3, y3);
                            }
                            else if (x3 < x1 && x0 > x2)
                            { // three segments descending down-left
                                stbtt__handle_clipped_edge(scanline, x, e, x0, y0, x2, y2);
                                stbtt__handle_clipped_edge(scanline, x, e, x2, y2, x1, y1);
                                stbtt__handle_clipped_edge(scanline, x, e, x1, y1, x3, y3);
                            }
                            else if (x0 < x1 && x3 > x1)
                            { // two segments across x, down-right
                                stbtt__handle_clipped_edge(scanline, x, e, x0, y0, x1, y1);
                                stbtt__handle_clipped_edge(scanline, x, e, x1, y1, x3, y3);
                            }
                            else if (x3 < x1 && x0 > x1)
                            { // two segments across x, down-left
                                stbtt__handle_clipped_edge(scanline, x, e, x0, y0, x1, y1);
                                stbtt__handle_clipped_edge(scanline, x, e, x1, y1, x3, y3);
                            }
                            else if (x0 < x2 && x3 > x2)
                            { // two segments across x+1, down-right
                                stbtt__handle_clipped_edge(scanline, x, e, x0, y0, x2, y2);
                                stbtt__handle_clipped_edge(scanline, x, e, x2, y2, x3, y3);
                            }
                            else if (x3 < x2 && x0 > x2)
                            { // two segments across x+1, down-left
                                stbtt__handle_clipped_edge(scanline, x, e, x0, y0, x2, y2);
                                stbtt__handle_clipped_edge(scanline, x, e, x2, y2, x3, y3);
                            }
                            else
                            { // one segment
                                stbtt__handle_clipped_edge(scanline, x, e, x0, y0, x3, y3);
                            }
                        }
                    }
                }
                e = e.next;
            }
        }

        static void stbtt__handle_clipped_edge(Span<float> scanline, int x, stbtt__active_edge e, float x0, float y0, float x1, float y1)
        {
            if (y0 == y1)
                return;
            STBTT_assert(y0 < y1);
            STBTT_assert(e.sy <= e.ey);
            if (y0 > e.ey)
                return;
            if (y1 < e.sy)
                return;
            if (y0 < e.sy)
            {
                x0 += (x1 - x0) * (e.sy - y0) / (y1 - y0);
                y0 = e.sy;
            }
            if (y1 > e.ey)
            {
                x1 += (x1 - x0) * (e.ey - y1) / (y1 - y0);
                y1 = e.ey;
            }

            if (x0 == x)
                STBTT_assert(x1 <= x + 1);
            else if (x0 == x + 1)
                STBTT_assert(x1 >= x);
            else if (x0 <= x)
                STBTT_assert(x1 <= x);
            else if (x0 >= x + 1)
                STBTT_assert(x1 >= x + 1);
            else
                STBTT_assert(x1 >= x && x1 <= x + 1);

            if (x0 <= x && x1 <= x)
                scanline[x] += e.direction * (y1 - y0);
            else if (x0 >= x + 1 && x1 >= x + 1) { }
            else
            {
                STBTT_assert(x0 >= x && x0 <= x + 1 && x1 >= x && x1 <= x + 1);
                scanline[x] += e.direction * (y1 - y0) * (1 - ((x0 - x) + (x1 - x)) / 2); // coverage = 1 - average x position
            }
        }


        static void stbtt__sort_edges(stbtt__edge[] p, int n)
        {
            stbtt__sort_edges_quicksort(p, n);
            stbtt__sort_edges_ins_sort(p, n);
        }

        static bool STBTT__COMPARE(stbtt__edge a, stbtt__edge b) => ((a).y0 < (b).y0);

        static void stbtt__sort_edges_quicksort(Span<stbtt__edge> p, int n)
        {
            /* threshold for transitioning to insertion sort */
            while (n > 12)
            {
                stbtt__edge t;
                bool c01, c12, c;
                int m, i, j;

                /* compute median of three */
                m = n >> 1;
                c01 = STBTT__COMPARE(p[0], p[m]);
                c12 = STBTT__COMPARE(p[m], p[n - 1]);
                /* if 0 >= mid >= end, or 0 < mid < end, then use mid */
                if (c01 != c12)
                {
                    /* otherwise, we'll need to swap something else to middle */
                    int z;
                    c = STBTT__COMPARE(p[0], p[n - 1]);
                    /* 0>mid && mid<n:  0>n => n; 0<n => 0 */
                    /* 0<mid && mid>n:  0>n => 0; 0<n => n */
                    z = (c == c12) ? 0 : n - 1;
                    t = p[z];
                    p[z] = p[m];
                    p[m] = t;
                }
                /* now p[m] is the median-of-three */
                /* swap it to the beginning so it won't move around */
                t = p[0];
                p[0] = p[m];
                p[m] = t;

                /* partition loop */
                i = 1;
                j = n - 1;
                for (; ; )
                {
                    /* handling of equality is crucial here */
                    /* for sentinels & efficiency with duplicates */
                    for (; ; ++i)
                    {
                        if (!STBTT__COMPARE(p[i], p[0]))
                            break;
                    }
                    for (; ; --j)
                    {
                        if (!STBTT__COMPARE(p[0], p[j]))
                            break;
                    }
                    /* make sure we haven't crossed */
                    if (i >= j)
                        break;
                    t = p[i];
                    p[i] = p[j];
                    p[j] = t;

                    ++i;
                    --j;
                }
                /* recurse on smaller side, iterate on larger */
                if (j < (n - i))
                {
                    stbtt__sort_edges_quicksort(p, j);
                    p = p.Slice(i);
                    n = n - i;
                }
                else
                {
                    stbtt__sort_edges_quicksort(p.Slice(i), n - i);
                    n = j;
                }
            }
        }

        static void stbtt__sort_edges_ins_sort(Span<stbtt__edge> p, int n)
        {
            int i, j;
            for (i = 1; i < n; ++i)
            {
                stbtt__edge t = p[i];
                ref stbtt__edge a = ref t;
                j = i;
                while (j > 0)
                {
                    stbtt__edge b = p[j - 1];
                    bool c = STBTT__COMPARE(a, b);
                    if (!c)
                        break;
                    p[j] = p[j - 1];
                    --j;
                }
                if (i != j)
                    p[j] = t;
            }
        }
    }
}

