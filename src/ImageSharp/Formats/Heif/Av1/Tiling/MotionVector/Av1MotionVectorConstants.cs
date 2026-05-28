// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Formats.Heif.Av1.Tiling.MotionVector;

/// <summary>
/// Motion-vector constants, taken from spec section 3 (Symbols and abbreviated
/// terms) and the table at lines 196-266 of the AV1 bitstream specification.
/// </summary>
internal static class Av1MotionVectorConstants
{
    public const int IntraBlockCopyDelayPixels = 256;

    public const int IntraBlockCopyDelaySuperblocks64 = 4;

    /// <summary>Number of values for <c>mv_class</c> (<c>MV_CLASSES</c>).</summary>
    public const int Classes = 11;

    /// <summary>Number of values for <c>mv_class0_bit</c> (<c>CLASS0_SIZE</c>).</summary>
    public const int Class0Size = 2;
}
