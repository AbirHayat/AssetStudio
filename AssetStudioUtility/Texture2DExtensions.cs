using System;
using System.IO;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace AssetStudio
{
    public static class Texture2DExtensions
    {
        public static Image<Bgra32> ConvertToImage(this Texture2D m_Texture2D, bool flip)
        {
            return ConvertToImage(m_Texture2D, flip, unpackNormal: false);
        }

        public static Image<Bgra32> ConvertToImage(this Texture2D m_Texture2D, bool flip, bool unpackNormal, bool invertY = false)
        {
            var converter = new Texture2DConverter(m_Texture2D);
            var buff = BigArrayPool<byte>.Shared.Rent(converter.OutputDataSize);
            var spanBuff = buff.AsSpan(0, converter.OutputDataSize);
            try
            {
                if (!converter.DecodeTexture2D(buff)) 
                    return null;

                if (unpackNormal)
                {
                    var packing = NormalMapConverter.DetectPacking(m_Texture2D.m_TextureFormat, spanBuff);
                    NormalMapConverter.UnpackNormalMap(spanBuff, packing, invertY);
                }

                Image<Bgra32> image;
                if (converter.UsesSwitchSwizzle)
                {
                    var uncroppedSize = converter.GetUncroppedSize();
                    image = Image.LoadPixelData<Bgra32>(spanBuff, uncroppedSize.Width, uncroppedSize.Height);
                    image.Mutate(x => x.Crop(m_Texture2D.m_Width, m_Texture2D.m_Height));
                }
                else
                {
                    image = Image.LoadPixelData<Bgra32>(spanBuff, m_Texture2D.m_Width, m_Texture2D.m_Height);
                }

                if (flip)
                {
                    image.Mutate(x => x.Flip(FlipMode.Vertical));
                }
                return image;
            }
            finally
            {
                BigArrayPool<byte>.Shared.Return(buff, clearArray: true);
            }
        }

        public static Image<Bgra32> ConvertToNormalMapImage(this Texture2D m_Texture2D, bool flip, bool invertY = false, NormalMapPacking packing = NormalMapPacking.Auto)
        {
            var converter = new Texture2DConverter(m_Texture2D);
            var buff = BigArrayPool<byte>.Shared.Rent(converter.OutputDataSize);
            var spanBuff = buff.AsSpan(0, converter.OutputDataSize);
            try
            {
                if (!converter.DecodeTexture2D(buff))
                    return null;

                if (packing == NormalMapPacking.Auto)
                {
                    packing = NormalMapConverter.DetectPacking(m_Texture2D.m_TextureFormat, spanBuff);
                }
                NormalMapConverter.UnpackNormalMap(spanBuff, packing, invertY);

                Image<Bgra32> image;
                if (converter.UsesSwitchSwizzle)
                {
                    var uncroppedSize = converter.GetUncroppedSize();
                    image = Image.LoadPixelData<Bgra32>(spanBuff, uncroppedSize.Width, uncroppedSize.Height);
                    image.Mutate(x => x.Crop(m_Texture2D.m_Width, m_Texture2D.m_Height));
                }
                else
                {
                    image = Image.LoadPixelData<Bgra32>(spanBuff, m_Texture2D.m_Width, m_Texture2D.m_Height);
                }

                if (flip)
                {
                    image.Mutate(x => x.Flip(FlipMode.Vertical));
                }
                return image;
            }
            finally
            {
                BigArrayPool<byte>.Shared.Return(buff, clearArray: true);
            }
        }

        public static MemoryStream ConvertToStream(this Texture2D m_Texture2D, ImageFormat imageFormat, bool flip)
        {
            return ConvertToStream(m_Texture2D, imageFormat, flip, unpackNormal: false);
        }

        public static MemoryStream ConvertToStream(this Texture2D m_Texture2D, ImageFormat imageFormat, bool flip, bool unpackNormal, bool invertY = false)
        {
            var image = ConvertToImage(m_Texture2D, flip, unpackNormal, invertY);
            if (image != null)
            {
                using (image)
                {
                    return image.ConvertToStream(imageFormat);
                }
            }
            return null;
        }
    }
}
