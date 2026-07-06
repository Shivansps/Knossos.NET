// InnoSharpCompat.cs
//
// SharpCompress changed its API: newer versions made the public LzmaStream /
// BZip2Stream constructors private and replaced them with static Create(...)
// factory methods. Older versions only have the constructors.
//
// This shim resolves, once, whichever form is available (factory preferred,
// constructor as fallback) so the wrapper compiles and runs against any
// SharpCompress version. ZlibStream kept its public constructor across versions,
// so it is used directly in InnoArchive and not handled here.

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using SharpCompress.Compressors; // CompressionMode (stable public enum)

namespace Knossos.NET.Classes.Inno
{
    internal static class InnoSharpCompat
    {
        private static readonly Func<byte[], Stream, long, long, Stream> LzmaFactory     = BuildLzma(isLzma2: false);
        private static readonly Func<byte[], Stream, long, long, Stream> Lzma2Factory    = BuildLzma(isLzma2: true);
        private static readonly Func<Stream, Stream>                     BZip2Factory    = BuildBZip2();

        /// <summary>Raw LZMA1: 5-byte properties + data. outputSize may be -1 (unknown).</summary>
        public static Stream Lzma1(byte[] props, Stream input, long inputSize, long outputSize)
            => LzmaFactory(props, input, inputSize, outputSize);

        /// <summary>Raw LZMA2: single dict-size property byte + data.</summary>
        public static Stream Lzma2(byte[] props, Stream input, long inputSize, long outputSize)
            => Lzma2Factory(props, input, inputSize, outputSize);

        /// <summary>BZip2 decompressor (decompressConcatenated = false).</summary>
        public static Stream BZip2(Stream input) => BZip2Factory(input);

        // ------------------------------------------------------------------ //
        private static Type Resolve(string fullName)
            => Type.GetType($"{fullName}, SharpCompress")
               ?? AppDomain.CurrentDomain.GetAssemblies()
                    .Select(a => a.GetType(fullName))
                    .FirstOrDefault(t => t != null)
               ?? throw new InnoExtractException($"SharpCompress type not found: {fullName}");

        // LzmaStream: prefer Create(byte[],Stream,long,long[,Stream,bool,...]),
        // fall back to the matching public constructor.
        private static Func<byte[], Stream, long, long, Stream> BuildLzma(bool isLzma2)
        {
            Type t = Resolve("SharpCompress.Compressors.LZMA.LzmaStream");

            if (!isLzma2)
            {
                // 4-arg form: (properties, inputStream, inputSize, outputSize[, optional...])
                var fourTypes = new[] { typeof(byte[]), typeof(Stream), typeof(long), typeof(long) };

                var create = t.GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .FirstOrDefault(m => m.Name == "Create" && ParamsStartWith(m, fourTypes));
                if (create != null)
                {
                    var pars = create.GetParameters();
                    return (p, s, isz, osz) =>
                    {
                        var args = new object?[pars.Length];
                        args[0] = p; args[1] = s; args[2] = isz; args[3] = osz;
                        for (int i = 4; i < pars.Length; i++) args[i] = pars[i].HasDefaultValue ? pars[i].DefaultValue : false;
                        return (Stream)create.Invoke(null, args)!;
                    };
                }

                var ctor = t.GetConstructor(fourTypes)
                    ?? throw new InnoExtractException("No usable LzmaStream (4-arg) API found in this SharpCompress version.");
                return (p, s, isz, osz) => (Stream)ctor.Invoke(new object[] { p, s, isz, osz });
            }
            else
            {
                // 6-arg form: (properties, inputStream, inputSize, outputSize, presetDictionary, isLzma2)
                var sixTypes = new[] { typeof(byte[]), typeof(Stream), typeof(long), typeof(long), typeof(Stream), typeof(bool) };

                // Newer Create has an extra optional leaveOpen; match by prefix.
                var create = t.GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .FirstOrDefault(m => m.Name == "Create" && ParamsStartWith(m, sixTypes));
                if (create != null)
                {
                    var pars = create.GetParameters();
                    return (p, s, isz, osz) =>
                    {
                        var args = new object?[pars.Length];
                        args[0] = p; args[1] = s; args[2] = isz; args[3] = osz; args[4] = null; args[5] = true;
                        for (int i = 6; i < pars.Length; i++) args[i] = pars[i].HasDefaultValue ? pars[i].DefaultValue : false;
                        return (Stream)create.Invoke(null, args)!;
                    };
                }

                var ctor = t.GetConstructor(sixTypes)
                    ?? throw new InnoExtractException("No usable LzmaStream (LZMA2) API found in this SharpCompress version.");
                return (p, s, isz, osz) => (Stream)ctor.Invoke(new object?[] { p, s, isz, osz, null, true });
            }
        }

        // BZip2Stream: prefer Create(Stream,CompressionMode,bool[,...]), else the ctor.
        private static Func<Stream, Stream> BuildBZip2()
        {
            Type t = Resolve("SharpCompress.Compressors.BZip2.BZip2Stream");
            object decompress = Enum.Parse(typeof(CompressionMode), "Decompress");
            var baseTypes = new[] { typeof(Stream), typeof(CompressionMode), typeof(bool) };

            var create = t.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(m => m.Name == "Create" && ParamsStartWith(m, baseTypes));
            if (create != null)
            {
                var pars = create.GetParameters();
                return s =>
                {
                    var args = new object?[pars.Length];
                    args[0] = s; args[1] = decompress; args[2] = false;
                    for (int i = 3; i < pars.Length; i++) args[i] = pars[i].HasDefaultValue ? pars[i].DefaultValue : false;
                    return (Stream)create.Invoke(null, args)!;
                };
            }

            var ctor = t.GetConstructor(baseTypes)
                ?? throw new InnoExtractException("No usable BZip2Stream API found in this SharpCompress version.");
            return s => (Stream)ctor.Invoke(new object?[] { s, decompress, false });
        }

        private static bool ParamsStartWith(MethodInfo m, Type[] types)
        {
            var p = m.GetParameters();
            if (p.Length < types.Length) return false;
            for (int i = 0; i < types.Length; i++) if (p[i].ParameterType != types[i]) return false;
            // remaining parameters (if any) must be optional
            for (int i = types.Length; i < p.Length; i++) if (!p[i].HasDefaultValue) return false;
            return true;
        }
    }
}
