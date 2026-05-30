// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Formats.Heif.Av1.Pipeline.Cdef;

/// <summary>
/// AV1 CDEF (Constrained Directional Enhancement Filter) constants. See spec 5.9.21
/// (<c>cdef_params()</c> syntax), 6.10.14 (CDEF semantics), 7.15 (CDEF process).
/// </summary>
internal static class Av1CdefConstants
{
    /// <summary>
    /// Number of secondary strength buckets carried in the low 2 bits of each
    /// <c>cdef_*_strength</c> value (spec 6.10.14). The decoder remaps bucket 3 to 4 in
    /// 7.15.1 to skip an unused magnitude.
    /// </summary>
    public const int SecondaryStrengthCount = 4;

    /// <summary>
    /// CDEF unit size in luma samples. Spec 7.15.1 fixes the unit at 64×64.
    /// </summary>
    public const int BlockSize = 64;

    /// <summary>
    /// Vertical pixel padding around each CDEF unit (libaom-internal; the spec only
    /// requires neighbours within ±2 rows for the 5×5-aligned filter).
    /// </summary>
    public const int VerticalBorder = 2;

    /// <summary>
    /// Horizontal pixel padding around each CDEF unit. Aligned to 8 for SIMD — below
    /// spec resolution; cf. libaom <c>cdef_block.h:CDEF_HBORDER</c>.
    /// </summary>
    public const int HorizontalBorder = 8;

    /// <summary>
    /// Working-buffer stride <c>ALIGN_POWER_OF_TWO(128 + 16, 3) = 144</c>
    /// (libaom <c>CDEF_BSTRIDE</c>; SIMD-driven, below spec resolution).
    /// </summary>
    public const int BufferStride = 144;

    /// <summary>
    /// Sentinel placed at off-frame samples so the filter's max/min clip step can detect
    /// them without a separate flag (libaom <c>CDEF_VERY_LARGE</c>; the spec expresses the
    /// same idea by skipping out-of-frame neighbours in 7.15.2).
    /// </summary>
    public const int VeryLarge = 0x4000;

    /// <summary>
    /// <c>Cdef_Directions</c> from spec 7.15.3 (the 8 direction × 2 taps offsets), with two
    /// extra entries on each end so callers can index by <c>direction ± 2</c> without
    /// modular arithmetic. Padding rows mirror the wrap-around values that direction
    /// arithmetic would produce mod 8 (libaom-organisational; not in spec).
    /// </summary>
    public static readonly Av1CdefDirectionOffset[][] Directions =
    [
        [new(1, 0), new(2, 0)],
        [new(1, 0), new(2, -1)],
        [new(-1, 1), new(-2, 2)],
        [new(0, 1), new(-1, 2)],
        [new(0, 1), new(0, 2)],
        [new(0, 1), new(1, 2)],
        [new(1, 1), new(2, 2)],
        [new(1, 0), new(2, 1)],
        [new(1, 0), new(2, 0)],
        [new(1, 0), new(2, -1)],
        [new(-1, 1), new(-2, 2)],
        [new(0, 1), new(-1, 2)],
    ];

    /// <summary>
    /// Primary filter taps from spec 7.15.2 (<c>{4, 2}</c> when the primary strength
    /// magnitude is even, <c>{3, 3}</c> when odd; selected by
    /// <c>(pri_strength &gt;&gt; coeff_shift) &amp; 1</c>).
    /// </summary>
    public static readonly int[][] PrimaryTaps =
    [
        [4, 2],
        [3, 3],
    ];

    /// <summary>
    /// Secondary filter taps <c>{2, 1}</c> from spec 7.15.2.
    /// </summary>
    public static readonly int[] SecondaryTaps = [2, 1];

    /// <summary>
    /// Chroma direction remap for 4:2:2 (horizontally subsampled) — luma direction is mapped
    /// onto the closest direction valid in the half-width 8×4 chroma block. Spec 7.15.2.2;
    /// libaom <c>cdef.c:cdef_fb_col</c>.
    /// </summary>
    public static readonly int[] ChromaConv422 = [7, 0, 2, 4, 5, 6, 6, 6];

    /// <summary>
    /// Chroma direction remap for 4:4:0 (vertically subsampled) — luma direction is mapped
    /// onto the closest direction valid in the half-height 4×8 chroma block. Spec 7.15.2.2;
    /// libaom <c>cdef.c:cdef_fb_col</c>.
    /// </summary>
    public static readonly int[] ChromaConv440 = [1, 2, 2, 2, 3, 4, 6, 0];
}
