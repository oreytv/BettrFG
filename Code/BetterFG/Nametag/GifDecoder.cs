using System;
using System.IO;
using UnityEngine;

namespace BetterFG.Nametag
{
    // Minimal GIF87a/GIF89a decoder. Produces fully-composited RGBA frames
    // (honouring frame disposal + transparency) plus per-frame delays in seconds.
    public static class GifDecoder
    {
        public sealed class Result
        {
            public Texture2D[] Frames;   // bottom-up not used; pixels are top-left origin, flipped for Unity
            public float[] Delays;       // seconds per frame
            public int Width;
            public int Height;
            public bool IsAnimated => Frames != null && Frames.Length > 1;
        }

        public static bool IsGif(byte[] data)
        {
            return data != null && data.Length >= 6
                && data[0] == 'G' && data[1] == 'I' && data[2] == 'F'
                && data[3] == '8' && (data[4] == '7' || data[4] == '9') && data[5] == 'a';
        }

        public static bool IsGifPath(string path)
            => !string.IsNullOrEmpty(path) && path.EndsWith(".gif", StringComparison.OrdinalIgnoreCase);

        public static Result Decode(byte[] data)
        {
            if (!IsGif(data)) return null;
            try
            {
                using var ms = new MemoryStream(data);
                using var r = new BinaryReader(ms);

                r.ReadBytes(6); // header

                int width = r.ReadUInt16();
                int height = r.ReadUInt16();
                byte packed = r.ReadByte();
                r.ReadByte(); // background color index
                r.ReadByte(); // pixel aspect ratio

                bool hasGct = (packed & 0x80) != 0;
                int gctSize = 2 << (packed & 0x07);
                Color32[] gct = hasGct ? ReadColorTable(r, gctSize) : null;

                var frames = new System.Collections.Generic.List<Texture2D>();
                var delays = new System.Collections.Generic.List<float>();

                // Persistent canvas across frames for disposal handling.
                var canvas = new Color32[width * height];
                var prevCanvas = new Color32[width * height];

                int transparentIndex = -1;
                int delayCs = 10; // default 0.1s
                int disposalMethod = 0;

                while (ms.Position < ms.Length)
                {
                    int block = r.ReadByte();
                    if (block == 0x3B) break; // trailer

                    if (block == 0x21) // extension
                    {
                        int label = r.ReadByte();
                        if (label == 0xF9) // graphic control extension
                        {
                            r.ReadByte(); // block size (4)
                            byte gcePacked = r.ReadByte();
                            disposalMethod = (gcePacked >> 2) & 0x07;
                            bool transparency = (gcePacked & 0x01) != 0;
                            delayCs = r.ReadUInt16();
                            int tIdx = r.ReadByte();
                            transparentIndex = transparency ? tIdx : -1;
                            r.ReadByte(); // block terminator
                        }
                        else
                        {
                            SkipSubBlocks(r);
                        }
                        continue;
                    }

                    if (block != 0x2C) break; // not image descriptor → bail

                    int ix = r.ReadUInt16();
                    int iy = r.ReadUInt16();
                    int iw = r.ReadUInt16();
                    int ih = r.ReadUInt16();
                    byte imgPacked = r.ReadByte();

                    bool hasLct = (imgPacked & 0x80) != 0;
                    bool interlaced = (imgPacked & 0x40) != 0;
                    int lctSize = 2 << (imgPacked & 0x07);
                    Color32[] colorTable = hasLct ? ReadColorTable(r, lctSize) : gct;

                    int lzwMinCode = r.ReadByte();
                    byte[] lzwData = ReadSubBlocks(r);
                    byte[] indices = LzwDecode(lzwData, lzwMinCode, iw * ih);

                    // Save canvas for "restore to previous" disposal.
                    Array.Copy(canvas, prevCanvas, canvas.Length);

                    // Composite this frame's pixels onto the canvas.
                    if (colorTable != null && indices != null)
                    {
                        for (int row = 0; row < ih; row++)
                        {
                            int srcRow = interlaced ? DeinterlaceRow(row, ih) : row;
                            int cy = iy + srcRow;
                            if (cy < 0 || cy >= height) continue;
                            for (int col = 0; col < iw; col++)
                            {
                                int cx = ix + col;
                                if (cx < 0 || cx >= width) continue;
                                int idx = indices[row * iw + col];
                                if (idx == transparentIndex) continue;
                                if (idx < 0 || idx >= colorTable.Length) continue;
                                canvas[cy * width + cx] = colorTable[idx];
                            }
                        }
                    }

                    frames.Add(BuildTexture(canvas, width, height));
                    delays.Add(Mathf.Max(0.02f, delayCs / 100f));

                    // Apply disposal for next frame.
                    if (disposalMethod == 2) // restore to background (transparent)
                    {
                        for (int row = 0; row < ih; row++)
                        {
                            int cy = iy + row;
                            if (cy < 0 || cy >= height) continue;
                            for (int col = 0; col < iw; col++)
                            {
                                int cx = ix + col;
                                if (cx < 0 || cx >= width) continue;
                                canvas[cy * width + cx] = new Color32(0, 0, 0, 0);
                            }
                        }
                    }
                    else if (disposalMethod == 3) // restore to previous
                    {
                        Array.Copy(prevCanvas, canvas, canvas.Length);
                    }

                    transparentIndex = -1; // reset per-frame GCE state
                }

                if (frames.Count == 0) return null;
                return new Result
                {
                    Frames = frames.ToArray(),
                    Delays = delays.ToArray(),
                    Width = width,
                    Height = height,
                };
            }
            catch (Exception ex)
            {
                Debug.LogError("[GifDecoder] decode failed: " + ex.Message);
                return null;
            }
        }

        private static int DeinterlaceRow(int pass, int height)
        {
            // GIF interlacing: 4 passes. Map sequential output rows back to image rows.
            int i = 0;
            // pass 1: rows 0,8,16...
            for (int y = 0; y < height; y += 8) { if (i++ == pass) return y; }
            // pass 2: rows 4,12,20...
            for (int y = 4; y < height; y += 8) { if (i++ == pass) return y; }
            // pass 3: rows 2,6,10...
            for (int y = 2; y < height; y += 4) { if (i++ == pass) return y; }
            // pass 4: rows 1,3,5...
            for (int y = 1; y < height; y += 2) { if (i++ == pass) return y; }
            return pass;
        }

        private static Texture2D BuildTexture(Color32[] canvas, int width, int height)
        {
            // GIF origin is top-left; Unity textures are bottom-left, so flip vertically.
            var flipped = new Color32[width * height];
            for (int y = 0; y < height; y++)
            {
                int srcOff = y * width;
                int dstOff = (height - 1 - y) * width;
                Array.Copy(canvas, srcOff, flipped, dstOff, width);
            }
            var tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.SetPixels32(flipped);
            tex.Apply();
            return tex;
        }

        private static Color32[] ReadColorTable(BinaryReader r, int size)
        {
            var table = new Color32[size];
            for (int i = 0; i < size; i++)
            {
                byte cr = r.ReadByte();
                byte cg = r.ReadByte();
                byte cb = r.ReadByte();
                table[i] = new Color32(cr, cg, cb, 255);
            }
            return table;
        }

        private static void SkipSubBlocks(BinaryReader r)
        {
            int len;
            while ((len = r.ReadByte()) != 0) r.ReadBytes(len);
        }

        private static byte[] ReadSubBlocks(BinaryReader r)
        {
            using var ms = new MemoryStream();
            int len;
            while ((len = r.ReadByte()) != 0)
                ms.Write(r.ReadBytes(len), 0, len);
            return ms.ToArray();
        }

        // ── LZW decode ────────────────────────────────────────────────────────
        private static byte[] LzwDecode(byte[] data, int minCodeSize, int pixelCount)
        {
            int clearCode = 1 << minCodeSize;
            int endCode = clearCode + 1;
            int codeSize = minCodeSize + 1;
            int nextCode = endCode + 1;

            var output = new byte[pixelCount];
            int outPos = 0;

            // Dictionary of byte sequences.
            var prefix = new short[4096];
            var suffix = new byte[4096];
            var stack = new byte[4096];
            for (int i = 0; i < clearCode; i++) { prefix[i] = -1; suffix[i] = (byte)i; }

            int bitBuffer = 0, bitCount = 0, dataPos = 0;
            int prevCode = -1;
            int stackTop = 0;

            while (outPos < pixelCount)
            {
                // Read next code.
                while (bitCount < codeSize)
                {
                    if (dataPos >= data.Length) goto done;
                    bitBuffer |= data[dataPos++] << bitCount;
                    bitCount += 8;
                }
                int code = bitBuffer & ((1 << codeSize) - 1);
                bitBuffer >>= codeSize;
                bitCount -= codeSize;

                if (code == clearCode)
                {
                    codeSize = minCodeSize + 1;
                    nextCode = endCode + 1;
                    prevCode = -1;
                    continue;
                }
                if (code == endCode) break;

                int curCode = code;
                if (code >= nextCode)
                {
                    // Special case: code not yet in table.
                    if (prevCode < 0) break;
                    stack[stackTop++] = FirstByte(prefix, suffix, prevCode);
                    curCode = prevCode;
                }

                while (curCode >= clearCode)
                {
                    stack[stackTop++] = suffix[curCode];
                    curCode = prefix[curCode];
                }
                stack[stackTop++] = suffix[curCode];

                while (stackTop > 0 && outPos < pixelCount)
                    output[outPos++] = stack[--stackTop];

                if (prevCode >= 0 && nextCode < 4096)
                {
                    prefix[nextCode] = (short)prevCode;
                    suffix[nextCode] = suffix[curCode];
                    nextCode++;
                    if (nextCode == (1 << codeSize) && codeSize < 12)
                        codeSize++;
                }
                prevCode = code;
            }
            done:
            return output;
        }

        private static byte FirstByte(short[] prefix, byte[] suffix, int code)
        {
            while (prefix[code] >= 0) code = prefix[code];
            return suffix[code];
        }
    }
}
