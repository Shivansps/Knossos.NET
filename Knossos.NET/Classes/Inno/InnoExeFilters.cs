// InnoExeFilters.cs
//
// Managed port of innoextract's stream/exefilter.hpp. Inno Setup rewrites the
// addresses of x86 CALL/JMP instructions in stored executables to make them
// more compressible; these decoders reverse that transform. They operate on a
// fully-buffered payload and always return a buffer of the SAME length as the
// input (the transform is byte-for-byte).
//
// .vp data files use no filter, so for FreeSpace 2 these are rarely exercised,
// but they are provided for completeness and correctness on any installer.

using System;
using System.IO;

namespace Knossos.NET.Classes.Inno
{
    internal static class InnoExeFilters
    {
        /// <summary>Decoder for executables stored by Inno Setup &lt; 5.2.0.</summary>
        public static byte[] Decode4108(byte[] input)
        {
            var output = new byte[input.Length];
            uint addr = 0;
            int addrBytesLeft = 0;
            uint addrOffset = 5; // matches the filter's initial state

            for (int i = 0; i < input.Length; i++, addrOffset++)
            {
                int b = input[i];
                if (addrBytesLeft == 0)
                {
                    if (b == 0xe8 || b == 0xe9)
                    {
                        unchecked { addr = ~addrOffset + 1; } // == -addrOffset
                        addrBytesLeft = 4;
                    }
                }
                else
                {
                    unchecked { addr += (byte)b; }
                    b = (byte)addr;
                    addr >>= 8;
                    addrBytesLeft--;
                }
                output[i] = (byte)b;
            }
            return output;
        }

        /// <summary>
        /// Decoder for executables stored by Inno Setup &gt;= 5.2.0.
        /// <paramref name="flipHighByte"/> selects the 5.3.9+ variant.
        /// </summary>
        public static byte[] Decode5200(byte[] input, bool flipHighByte)
        {
            const int blockSize = 0x10000;
            using var ms = new MemoryStream(input.Length);
            uint offset = 0; // total bytes consumed from input
            int i = 0;
            int n = input.Length;

            while (i < n)
            {
                int b = input[i++];
                ms.WriteByte((byte)b);
                offset++;

                if (b != 0xe8 && b != 0xe9) continue;

                int blockSizeLeft = (int)(blockSize - ((offset - 1) % blockSize));
                if (blockSizeLeft < 5) continue; // instruction spans a block boundary

                if (i + 4 > n)
                {
                    // EOF before the full address: emit the remaining bytes unchanged.
                    while (i < n) ms.WriteByte(input[i++]);
                    break;
                }

                byte b0 = input[i], b1 = input[i + 1], b2 = input[i + 2], b3 = input[i + 3];
                i += 4;
                offset += 4;

                if (b3 == 0x00 || b3 == 0xff)
                {
                    uint addr = offset & 0xffffff;
                    uint rel = (uint)(b0 | (b1 << 8) | (b2 << 16));
                    unchecked { rel -= addr; }
                    byte n0 = (byte)rel;
                    byte n1 = (byte)(rel >> 8);
                    byte n2 = (byte)(rel >> 16);
                    byte n3 = b3;
                    if (flipHighByte && (rel & 0x800000) != 0) n3 = (byte)~b3;
                    ms.WriteByte(n0); ms.WriteByte(n1); ms.WriteByte(n2); ms.WriteByte(n3);
                }
                else
                {
                    // Most likely not a real CALL/JMP: leave the operand untouched.
                    ms.WriteByte(b0); ms.WriteByte(b1); ms.WriteByte(b2); ms.WriteByte(b3);
                }
            }

            return ms.ToArray();
        }
    }
}
