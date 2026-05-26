// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Formats.Heif.Av1.OpenBitstreamUnit;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Formats.Heif.Av1;

internal class Av1YuvConverter
{
    private static readonly YuvMatrix Bt709 = new(0.2126, 0.0722);

    // Used by Bt601/Bt470BG/Fcc/Smpte240/Unspecified.
    private static readonly YuvMatrix Bt601 = new(0.299, 0.114);

    private static readonly YuvMatrix Bt2020 = new(0.2627, 0.0593);

    public static void ConvertToRgb<TPixel>(Configuration configuration, Av1FrameBuffer<byte> frameBuffer, ImageFrame<TPixel> image)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using Image<Rgb24> rgbImage = new(image.Width, image.Height);
        ImageFrame<Rgb24> rgbFrame = rgbImage.Frames.RootFrame;

        ConvertYuvToRgb(frameBuffer, rgbFrame);
        image.ProcessPixelRows(rgbFrame, (resultAcc, rgbAcc) =>
        {
            for (int y = 0; y < rgbImage.Height; y++)
            {
                Span<Rgb24> rgbRow = rgbAcc.GetRowSpan(y);
                Span<TPixel> resultRow = resultAcc.GetRowSpan(y);
                PixelOperations<TPixel>.Instance.FromRgb24(configuration, rgbRow, resultRow);
            }
        });
    }

    public static void ConvertFromRgb<TPixel>(Configuration configuration, ImageFrame<TPixel> image, Av1FrameBuffer<byte> frameBuffer)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using Image<Rgb24> rgbImage = new(image.Width, image.Height);
        ImageFrame<Rgb24> rgbFrame = rgbImage.Frames.RootFrame;

        image.ProcessPixelRows(rgbFrame, (sourceAcc, rgbAcc) =>
        {
            for (int y = 0; y < rgbImage.Height; y++)
            {
                Span<Rgb24> rgbRow = rgbAcc.GetRowSpan(y);
                Span<TPixel> sourceRow = sourceAcc.GetRowSpan(y);
                PixelOperations<TPixel>.Instance.ToRgb24(configuration, sourceRow, rgbRow);
            }
        });

        ConvertRgbToYuv(rgbFrame, frameBuffer);
    }

    private static (int SubX, int SubY) GetSubsampling(Av1ColorFormat format) => format switch
    {
        Av1ColorFormat.Yuv420 => (1, 1),
        Av1ColorFormat.Yuv422 => (1, 0),
        Av1ColorFormat.Yuv444 => (0, 0),
        Av1ColorFormat.Yuv400 => (0, 0),
        _ => throw new NotSupportedException($"Unsupported color format: {format}.")
    };

    // AVIF/MIAF (ISO 23000-22 §7.4.2.2.2) treats matrix_coefficients = Unspecified as BT.601.
    // Identity (matrix_coefficients = 0) means GBR with no transform, but historically this
    // converter has produced BT.709 output for default-constructed sequence headers; preserve
    // that to avoid breaking callers that rely on the legacy contract.
    private static YuvMatrix GetMatrix(ObuMatrixCoefficients matrix) => matrix switch
    {
        ObuMatrixCoefficients.Bt407 => Bt709,
        ObuMatrixCoefficients.Bt2020NonConstantLuminance => Bt2020,
        ObuMatrixCoefficients.Bt2020ConstantLuminance => Bt2020,
        ObuMatrixCoefficients.Smpte240 => new YuvMatrix(0.212, 0.087),
        ObuMatrixCoefficients.Bt601 => Bt601,
        ObuMatrixCoefficients.Bt470BG => Bt601,
        ObuMatrixCoefficients.Fcc => new YuvMatrix(0.30, 0.11),
        ObuMatrixCoefficients.Identity => Bt709,
        ObuMatrixCoefficients.Unspecified => Bt601,
        _ => Bt601,
    };

    private static void ConvertYuvToRgb(Av1FrameBuffer<byte> buffer, ImageFrame<Rgb24> image)
    {
        Guard.NotNull(buffer.BufferY);
        (int subX, int subY) = GetSubsampling(buffer.ColorFormat);
        bool monochrome = buffer.ColorFormat == Av1ColorFormat.Yuv400;
        if (!monochrome)
        {
            Guard.NotNull(buffer.BufferCb);
            Guard.NotNull(buffer.BufferCr);
        }

        YuvMatrix matrix = GetMatrix(buffer.MatrixCoefficients);
        double wr = matrix.Wr;
        double wb = matrix.Wb;
        double wg = matrix.Wg;
        double krFactor = 2.0 * (1.0 - wr);
        double kbFactor = 2.0 * (1.0 - wb);
        double krgFactor = krFactor * wr / wg;
        double kbgFactor = kbFactor * wb / wg;
        (double yScale, double yBias, double chromaScale, double chromaBias) = GetDecodeScale(buffer.IsFullRange);

        Span<byte> yBuffer = buffer.DeriveBlockPointer(Av1Plane.Y, default, 0, 0, out int yStride);
        Span<byte> uBuffer = default;
        Span<byte> vBuffer = default;
        int chromaStride = 0;
        if (!monochrome)
        {
            uBuffer = buffer.DeriveBlockPointer(Av1Plane.U, default, subX, subY, out _);
            vBuffer = buffer.DeriveBlockPointer(Av1Plane.V, default, subX, subY, out chromaStride);
        }

        int yOffset = yStride;
        int chromaOffset = chromaStride;

        for (int y = 0; y < image.Height; y++)
        {
            Span<Rgb24> rgbRow = image.PixelBuffer.DangerousGetRowSpan(y);
            for (int x = 0; x < image.Width; x++)
            {
                double yNorm = (yBuffer[yOffset + x] * yScale) + yBias;
                double cb, cr;
                if (monochrome)
                {
                    cb = 0;
                    cr = 0;
                }
                else
                {
                    int chromaX = x >> subX;
                    cb = (uBuffer[chromaOffset + chromaX] * chromaScale) + chromaBias;
                    cr = (vBuffer[chromaOffset + chromaX] * chromaScale) + chromaBias;
                }

                double r = yNorm + (krFactor * cr);
                double g = yNorm - (krgFactor * cr) - (kbgFactor * cb);
                double b = yNorm + (kbFactor * cb);

                rgbRow[x] = new Rgb24(
                    ClampToByte(r * 255.0),
                    ClampToByte(g * 255.0),
                    ClampToByte(b * 255.0));
            }

            yOffset += yStride;
            if (!monochrome && (subY == 0 || ((y + 1) & 1) == 0))
            {
                chromaOffset += chromaStride;
            }
        }
    }

    private static void ConvertRgbToYuv(ImageFrame<Rgb24> image, Av1FrameBuffer<byte> buffer)
    {
        Guard.NotNull(buffer.BufferY);
        (int subX, int subY) = GetSubsampling(buffer.ColorFormat);
        bool monochrome = buffer.ColorFormat == Av1ColorFormat.Yuv400;
        if (!monochrome)
        {
            Guard.NotNull(buffer.BufferCb);
            Guard.NotNull(buffer.BufferCr);
        }

        YuvMatrix matrix = GetMatrix(buffer.MatrixCoefficients);
        double wr = matrix.Wr;
        double wb = matrix.Wb;
        double wg = matrix.Wg;
        double cbNormScale = 0.5 / (1.0 - wb);
        double crNormScale = 0.5 / (1.0 - wr);
        (double yLumaScale, double yLumaBias, double chromaScale, double chromaBias) = GetEncodeScale(buffer.IsFullRange);
        byte chromaNeutral = ClampToByte(chromaBias);

        Span<byte> yBuffer = buffer.DeriveBlockPointer(Av1Plane.Y, default, 0, 0, out int yStride);
        Span<byte> uBuffer = default;
        Span<byte> vBuffer = default;
        int chromaStride = 0;
        int chromaWidth = 0;
        if (!monochrome)
        {
            uBuffer = buffer.DeriveBlockPointer(Av1Plane.U, default, subX, subY, out _);
            vBuffer = buffer.DeriveBlockPointer(Av1Plane.V, default, subX, subY, out chromaStride);
            chromaWidth = (image.Width + subX) >> subX;
        }

        int yOffset = yStride;
        int chromaOffset = chromaStride;

        // Accumulators for chroma subsampling (4:2:0 averages 2x2 blocks of signed cb/cr in [-0.5, 0.5]).
        double[] uAcc = monochrome ? [] : new double[chromaWidth];
        double[] vAcc = monochrome ? [] : new double[chromaWidth];
        int[] chromaCount = monochrome ? [] : new int[chromaWidth];

        for (int y = 0; y < image.Height; y++)
        {
            Span<Rgb24> rgbRow = image.PixelBuffer.DangerousGetRowSpan(y);
            for (int x = 0; x < image.Width; x++)
            {
                Rgb24 pixel = rgbRow[x];
                double r = pixel.R / 255.0;
                double g = pixel.G / 255.0;
                double b = pixel.B / 255.0;

                double yLuma = (wr * r) + (wg * g) + (wb * b);
                yBuffer[yOffset + x] = ClampToByte((yLuma * yLumaScale) + yLumaBias);

                if (monochrome)
                {
                    continue;
                }

                double cb = (b - yLuma) * cbNormScale;
                double cr = (r - yLuma) * crNormScale;

                int chromaX = x >> subX;
                uAcc[chromaX] += cb;
                vAcc[chromaX] += cr;
                chromaCount[chromaX]++;
            }

            yOffset += yStride;

            if (!monochrome && (subY == 0 || ((y + 1) & 1) == 0))
            {
                FlushChromaRow(uBuffer, vBuffer, chromaOffset, chromaWidth, uAcc, vAcc, chromaCount, chromaScale, chromaBias, chromaNeutral);
                chromaOffset += chromaStride;
            }
        }

        if (!monochrome && subY == 1 && (image.Height & 1) == 1)
        {
            // Final partial chroma row at odd image height.
            FlushChromaRow(uBuffer, vBuffer, chromaOffset, chromaWidth, uAcc, vAcc, chromaCount, chromaScale, chromaBias, chromaNeutral);
        }
    }

    private static void FlushChromaRow(Span<byte> uBuffer, Span<byte> vBuffer, int chromaOffset, int chromaWidth, double[] uAcc, double[] vAcc, int[] chromaCount, double chromaScale, double chromaBias, byte chromaNeutral)
    {
        for (int cx = 0; cx < chromaWidth; cx++)
        {
            int count = chromaCount[cx];
            if (count > 0)
            {
                uBuffer[chromaOffset + cx] = ClampToByte((uAcc[cx] / count * chromaScale) + chromaBias);
                vBuffer[chromaOffset + cx] = ClampToByte((vAcc[cx] / count * chromaScale) + chromaBias);
            }
            else
            {
                uBuffer[chromaOffset + cx] = chromaNeutral;
                vBuffer[chromaOffset + cx] = chromaNeutral;
            }

            uAcc[cx] = 0;
            vAcc[cx] = 0;
            chromaCount[cx] = 0;
        }
    }

    // Per ITU-T H.273 §8.3 (referenced by AV1): full-range chroma uses Round(c × 256) + 128 with
    // c in [-0.5, 0.5), so the inverse is (C - 128) / 256. Studio swing uses Y in [16,235] (×219)
    // and C in [16,240] (×224) anchored at 128. Luma full-range stays at [0,255] (×255).
    private static (double YScale, double YBias, double ChromaScale, double ChromaBias) GetDecodeScale(bool isFullRange)
        => isFullRange
            ? (1.0 / 255.0, 0.0, 1.0 / 256.0, -0.5)
            : (1.0 / 219.0, -16.0 / 219.0, 1.0 / 224.0, -128.0 / 224.0);

    private static (double YScale, double YBias, double ChromaScale, double ChromaBias) GetEncodeScale(bool isFullRange)
        => isFullRange
            ? (255.0, 0.0, 256.0, 128.0)
            : (219.0, 16.0, 224.0, 128.0);

    private static byte ClampToByte(double value)
    {
        double rounded = Math.Round(value);
        if (rounded < 0)
        {
            return 0;
        }

        if (rounded > 255)
        {
            return 255;
        }

        return (byte)rounded;
    }

    /// <summary>
    /// Luma weights for an RGB-to-Y'CbCr matrix (Wr, Wg, Wb), per ITU-T H.273.
    /// </summary>
    private readonly record struct YuvMatrix(double Wr, double Wb)
    {
        public double Wg => 1.0 - this.Wr - this.Wb;
    }
}
