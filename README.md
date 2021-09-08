# StbTrueType

This is a C# port of the [stb_truetype.h font rendering library](https://github.com/nothings/stb/blob/master/stb_truetype.h). It aims to recreate the original code as closely as possible, without using any unsafe code. This involved replacing the original types and substituting any use of pointers with C# Memory/Span structures.

The port is functional, though the semantics and function naming still match the original C code. It was investigated as a replacement for SharpFont (a C# wrapper over FreeType), in order to replace the native freetype.dll dependency with 100% managed code. However, I stopped development when initial tests showed that the font rendering quality was significantly worse than FreeType, at least for my use cases.