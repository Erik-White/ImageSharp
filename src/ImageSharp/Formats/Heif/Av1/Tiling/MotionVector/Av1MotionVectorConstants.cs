// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Formats.Heif.Av1.Tiling.MotionVector;

/// <summary>
/// 3. Symbols and abbreviated terms — motion vector related constants. Values
/// are taken directly from the spec table on lines 196-266 of the AV1
/// bitstream specification.
/// </summary>
internal static class Av1MotionVectorConstants
{
    /// <summary>Number of luma samples before intra block copy can be used.</summary>
    public const int IntraBlockCopyDelayPixels = 256;

    /// <summary>Number of 64x64 blocks before intra block copy can be used.</summary>
    public const int IntraBlockCopyDelaySuperblocks64 = 4;

    /// <summary>
    /// Number of contexts for decoding motion vectors, including one for intra
    /// block copy (<c>MV_CONTEXTS</c>).
    /// </summary>
    public const int Contexts = 2;

    /// <summary>
    /// Motion vector context used for intra block copy
    /// (<c>MV_INTRABC_CONTEXT</c>).
    /// </summary>
    public const int IntraBlockCopyContext = 1;

    /// <summary>Number of values for <c>mv_joint</c>.</summary>
    public const int Joints = 4;

    /// <summary>Number of values for <c>mv_class</c>.</summary>
    public const int Classes = 11;

    /// <summary>Number of values for <c>mv_class0_bit</c>.</summary>
    public const int Class0Size = 2;

    /// <summary>Maximum number of bits used when decoding motion vectors.</summary>
    public const int OffsetBits = 10;

    /// <summary>Value used when clipping motion vectors at the frame border.</summary>
    public const int Border = 128;

    /// <summary>Number of motion vector components (vertical, horizontal).</summary>
    public const int Components = 2;
}
