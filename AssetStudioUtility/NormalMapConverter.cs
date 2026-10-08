using System;
using System.Text.RegularExpressions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace AssetStudio
{
    public enum NormalMapPacking
    {
        None = 0,
        Auto,
        DXT5nm,     // AG packing (X in Alpha, Y in Green, R is 1)
        BC5,        // RG packing (X in Red, Y in Green, B is 0)
        Universal   // Unity UnpackNormalmapRGorAG (X = R * A, Y = G)
    }

    public static class NormalMapConverter
    {
        // 256 x 256 lookup table for fast Z reconstruction:
        // ZTable[(x << 8) | y] = (byte)round((sqrt(max(0, 1 - nx^2 - ny^2)) * 0.5 + 0.5) * 255)
        private static readonly byte[] ZTable = new byte[256 * 256];

        private static readonly Regex NormalNameRegex = new Regex(
            @"(?:^|[_\-\s])(normal|bump|norm|nrm|nm|n)(?:[_\-\s\d]|\.[^.]+$|$)|(?:normal|bump|nrm)(?:map)?(?:s)?(?:[_\-\s\d]|\.[^.]+$|$)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static byte ClampByte(float val)
        {
            var round = (int)Math.Round(val);
            return (byte)(round < 0 ? 0 : round > 255 ? 255 : round);
        }

        static NormalMapConverter()
        {
            for (var x = 0; x < 256; x++)
            {
                var nx = (x / 255f) * 2f - 1f;
                var nx2 = nx * nx;
                for (var y = 0; y < 256; y++)
                {
                    var ny = (y / 255f) * 2f - 1f;
                    var nz2 = 1f - nx2 - (ny * ny);
                    var nz = nz2 > 0f ? (float)Math.Sqrt(nz2) : 0f;
                    var zByte = ClampByte((nz * 0.5f + 0.5f) * 255f);
                    ZTable[(x << 8) | y] = zByte;
                }
            }
        }

        public static byte GetReconstructedZ(byte x, byte y)
        {
            return ZTable[(x << 8) | y];
        }

        public static bool IsNormalMapName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return false;

            if (name.IndexOf("normal", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (name.IndexOf("bump", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (name.IndexOf("nrm", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            return NormalNameRegex.IsMatch(name);
        }

        public static bool IsNormalMapFormat(TextureFormat format)
        {
            return format == TextureFormat.BC5
                || format == TextureFormat.EAC_RG
                || format == TextureFormat.EAC_RG_SIGNED
                || format == TextureFormat.RG16
                || format == TextureFormat.RG32
                || format == TextureFormat.RGHalf
                || format == TextureFormat.RGFloat;
        }

        public static bool IsNormalMapMaterialProperty(string propName)
        {
            if (string.IsNullOrEmpty(propName))
                return false;

            return propName.Equals("_BumpMap", StringComparison.OrdinalIgnoreCase)
                || propName.Equals("_NormalMap", StringComparison.OrdinalIgnoreCase)
                || propName.Equals("_DetailNormalMap", StringComparison.OrdinalIgnoreCase)
                || propName.IndexOf("normal", StringComparison.OrdinalIgnoreCase) >= 0
                || propName.IndexOf("bump", StringComparison.OrdinalIgnoreCase) >= 0
                || propName.IndexOf("norm", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static bool IsLikelyDxt5nm(ReadOnlySpan<byte> bgraBytes)
        {
            if (bgraBytes.IsEmpty || bgraBytes.Length < 64)
                return false;

            var totalPixels = bgraBytes.Length / 4;
            var sampleCount = Math.Min(totalPixels, 64);
            var step = Math.Max(1, totalPixels / sampleCount);

            var highRCount = 0;
            var validNormCount = 0;
            long greenSum = 0;
            long alphaSum = 0;

            for (var s = 0; s < sampleCount; s++)
            {
                var offset = (s * step) * 4;
                if (offset + 3 >= bgraBytes.Length)
                    break;

                var g = bgraBytes[offset + 1];
                var r = bgraBytes[offset + 2];
                var a = bgraBytes[offset + 3];

                if (r >= 230)
                {
                    highRCount++;
                }

                greenSum += g;
                alphaSum += a;

                var nx = (a / 127.5f) - 1.0f;
                var ny = (g / 127.5f) - 1.0f;
                if ((nx * nx + ny * ny) <= 1.12f)
                {
                    validNormCount++;
                }
            }

            if (highRCount < sampleCount * 0.80f)
                return false;

            if (validNormCount < sampleCount * 0.80f)
                return false;

            var avgG = greenSum / (float)sampleCount;
            var avgA = alphaSum / (float)sampleCount;

            if (avgG < 50f || avgG > 205f || avgA < 50f || avgA > 205f)
                return false;

            var alphaVarianceSum = 0f;
            for (var s = 0; s < sampleCount; s++)
            {
                var offset = (s * step) * 4;
                if (offset + 3 >= bgraBytes.Length)
                    break;
                var a = bgraBytes[offset + 3];
                var diff = a - avgA;
                alphaVarianceSum += diff * diff;
            }
            var alphaStdDev = (float)Math.Sqrt(alphaVarianceSum / sampleCount);

            return alphaStdDev >= 2.0f;
        }

        public static NormalMapPacking DetectPacking(TextureFormat format, ReadOnlySpan<byte> bgraBytes = default)
        {
            if (format == TextureFormat.BC5
                || format == TextureFormat.EAC_RG
                || format == TextureFormat.EAC_RG_SIGNED
                || format == TextureFormat.RG16
                || format == TextureFormat.RG32
                || format == TextureFormat.RGHalf
                || format == TextureFormat.RGFloat)
            {
                return NormalMapPacking.BC5;
            }

            if (format == TextureFormat.DXT5 || format == TextureFormat.DXT5Crunched)
            {
                return NormalMapPacking.DXT5nm;
            }

            if (!bgraBytes.IsEmpty && bgraBytes.Length >= 64)
            {
                // Heuristic inspection: sample up to 32 pixels
                var pixelCount = Math.Min(bgraBytes.Length / 4, 32);
                var highRCount = 0;
                var lowBCount = 0;
                var alphaVariance = 0;
                var firstA = bgraBytes[3];

                for (var i = 0; i < pixelCount; i++)
                {
                    var offset = i * 4;
                    var b = bgraBytes[offset];
                    var r = bgraBytes[offset + 2];
                    var a = bgraBytes[offset + 3];

                    if (r >= 240) highRCount++;
                    if (b <= 15) lowBCount++;
                    if (Math.Abs(a - firstA) > 10) alphaVariance++;
                }

                if (highRCount > pixelCount / 2 && alphaVariance > 2)
                    return NormalMapPacking.DXT5nm;
                if (lowBCount > pixelCount / 2)
                    return NormalMapPacking.BC5;
            }

            return NormalMapPacking.Universal;
        }

        public static void UnpackNormalMap(Span<byte> bgraBytes, NormalMapPacking packing, bool invertY = false)
        {
            if (packing == NormalMapPacking.None || bgraBytes.IsEmpty)
                return;

            if (packing == NormalMapPacking.Auto)
            {
                packing = DetectPacking(TextureFormat.Alpha8, bgraBytes);
            }

            var pixelCount = bgraBytes.Length / 4;

            switch (packing)
            {
                case NormalMapPacking.DXT5nm:
                    for (var i = 0; i < pixelCount; i++)
                    {
                        var offset = i * 4;
                        var g = bgraBytes[offset + 1];
                        var x = bgraBytes[offset + 3]; // Alpha contains X
                        var y = invertY ? (byte)(255 - g) : g;
                        var z = ZTable[(x << 8) | y];

                        bgraBytes[offset] = z;         // B
                        bgraBytes[offset + 1] = y;     // G
                        bgraBytes[offset + 2] = x;     // R
                        bgraBytes[offset + 3] = 255;   // A
                    }
                    break;

                case NormalMapPacking.BC5:
                    for (var i = 0; i < pixelCount; i++)
                    {
                        var offset = i * 4;
                        var g = bgraBytes[offset + 1];
                        var x = bgraBytes[offset + 2]; // Red contains X
                        var y = invertY ? (byte)(255 - g) : g;
                        var z = ZTable[(x << 8) | y];

                        bgraBytes[offset] = z;         // B
                        bgraBytes[offset + 1] = y;     // G
                        bgraBytes[offset + 2] = x;     // R
                        bgraBytes[offset + 3] = 255;   // A
                    }
                    break;

                case NormalMapPacking.Universal:
                default:
                    for (var i = 0; i < pixelCount; i++)
                    {
                        var offset = i * 4;
                        var g = bgraBytes[offset + 1];
                        var r = bgraBytes[offset + 2];
                        var a = bgraBytes[offset + 3];

                        // Unity UnpackNormalmapRGorAG: X = R * A (in 0..1)
                        byte x;
                        if (a >= 250)
                        {
                            x = r; // RG format
                        }
                        else if (r >= 250)
                        {
                            x = a; // AG / DXT5nm format
                        }
                        else
                        {
                            var nx = ((r / 255f) * (a / 255f)) * 2f - 1f;
                            x = ClampByte((nx * 0.5f + 0.5f) * 255f);
                        }

                        var y = invertY ? (byte)(255 - g) : g;
                        var z = ZTable[(x << 8) | y];

                        bgraBytes[offset] = z;         // B
                        bgraBytes[offset + 1] = y;     // G
                        bgraBytes[offset + 2] = x;     // R
                        bgraBytes[offset + 3] = 255;   // A
                    }
                    break;
            }
        }

        public static void UnpackNormalMap(this Image<Bgra32> image, NormalMapPacking packing, bool invertY = false)
        {
            if (image == null) return;
            image.ProcessPixelRows(accessor =>
            {
                for (var y = 0; y < accessor.Height; y++)
                {
                    var rowSpan = accessor.GetRowSpan(y);
                    var byteSpan = System.Runtime.InteropServices.MemoryMarshal.AsBytes(rowSpan);
                    UnpackNormalMap(byteSpan, packing, invertY);
                }
            });
        }
    }
}
