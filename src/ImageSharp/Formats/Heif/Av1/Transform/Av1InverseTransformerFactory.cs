// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Formats.Heif.Av1.Transform.Inverse;

namespace SixLabors.ImageSharp.Formats.Heif.Av1.Transform;

internal static class Av1InverseTransformerFactory
{
    /// <summary>
    /// SVT: svt_av1_inv_txfm_add
    /// </summary>
    public static unsafe void InverseTransformAdd(Span<int> coefficients, Span<byte> readBuffer, int readStride, Span<byte> writeBuffer, int writeStride, Av1TransformFunctionParameters transformFunctionParameters)
    {
        Guard.MustBeLessThanOrEqualTo(transformFunctionParameters.BitDepth, 8, nameof(transformFunctionParameters));
        Guard.IsFalse(transformFunctionParameters.Is16BitPipeline, nameof(transformFunctionParameters), "Calling 8-bit pipeline while 16-bit is requested.");
        if (transformFunctionParameters.IsLossless)
        {
            // libaom highbd_inv_txfm_add_4x4_c: for lossless frames the only valid transform
            // is DCT_DCT and it is replaced by the integer 4x4 inverse Walsh-Hadamard. The
            // read and write buffers are aliased here.
            Guard.IsTrue(transformFunctionParameters.TransformSize == Av1TransformSize.Size4x4, nameof(transformFunctionParameters), "Lossless transforms must be 4x4.");
            Guard.IsTrue(transformFunctionParameters.TransformType == Av1TransformType.DctDct, nameof(transformFunctionParameters), "Lossless transform type must be DCT_DCT.");
            Av1Inverse2dTransformer.InverseWalshHadamard4x4Add(coefficients, writeBuffer, writeStride, transformFunctionParameters.EndOfBuffer);
            return;
        }

        int width = transformFunctionParameters.TransformSize.GetWidth();
        int height = transformFunctionParameters.TransformSize.GetHeight();
        Span<int> buffer = new int[(width * height) + (2 * Math.Max(width, height))];
        Av1Transform2dFlipConfiguration config = new(transformFunctionParameters.TransformType, transformFunctionParameters.TransformSize, isInverse: true);
        Av1Inverse2dTransformer.Transform2dAdd(coefficients, readBuffer, readStride, writeBuffer, writeStride, config, buffer);
    }

    public static unsafe void InverseTransformAdd(Span<int> coefficients, Span<short> readBuffer, int readStride, Span<short> writeBuffer, int writeStride, Av1TransformFunctionParameters transformFunctionParameters)
    {
        Guard.IsTrue(transformFunctionParameters.Is16BitPipeline, nameof(transformFunctionParameters), "Calling 16-bit pipeline while 8-bit is requested.");
        int width = transformFunctionParameters.TransformSize.GetWidth();
        int height = transformFunctionParameters.TransformSize.GetHeight();
        Span<int> buffer = new int[(width * height) + (2 * Math.Max(width, height))];
        Av1Transform2dFlipConfiguration config = new(transformFunctionParameters.TransformType, transformFunctionParameters.TransformSize, isInverse: true);
        Av1Inverse2dTransformer.Transform2dAdd(coefficients, readBuffer, readStride, writeBuffer, writeStride, config, buffer, transformFunctionParameters.BitDepth);
    }

    internal static IAv1Transformer1d? GetTransformer(Av1TransformFunctionType type) => type switch
    {
        Av1TransformFunctionType.Dct4 => new Av1Dct4Inverse1dTransformer(),
        Av1TransformFunctionType.Dct8 => new Av1Dct8Inverse1dTransformer(),
        Av1TransformFunctionType.Dct16 => new Av1Dct16Inverse1dTransformer(),
        Av1TransformFunctionType.Dct32 => new Av1Dct32Inverse1dTransformer(),
        Av1TransformFunctionType.Dct64 => new Av1Dct64Inverse1dTransformer(),
        Av1TransformFunctionType.Adst4 => new Av1Adst4Inverse1dTransformer(),
        Av1TransformFunctionType.Adst8 => new Av1Adst8Inverse1dTransformer(),
        Av1TransformFunctionType.Adst16 => new Av1Adst16Inverse1dTransformer(),
        Av1TransformFunctionType.Adst32 => new Av1Adst32Inverse1dTransformer(),
        Av1TransformFunctionType.Identity4 => new Av1Identity4Inverse1dTransformer(),
        Av1TransformFunctionType.Identity8 => new Av1Identity8Inverse1dTransformer(),
        Av1TransformFunctionType.Identity16 => new Av1Identity16Inverse1dTransformer(),
        Av1TransformFunctionType.Identity32 => new Av1Identity32Inverse1dTransformer(),
        Av1TransformFunctionType.Identity64 => new Av1Identity64Inverse1dTransformer(),
        _ => null
    };
}
