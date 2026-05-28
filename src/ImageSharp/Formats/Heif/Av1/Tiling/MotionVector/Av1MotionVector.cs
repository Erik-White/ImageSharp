// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Formats.Heif.Av1.Tiling.MotionVector;

/// <summary>
/// 6.10.x. A motion (or displacement) vector in 1/8-pel units, matching the
/// spec's <c>Mv[ref][0]=row</c>, <c>Mv[ref][1]=col</c> convention. For intra
/// block copy blocks the low three bits are zero (integer-pel only).
/// </summary>
internal readonly record struct Av1MotionVector(short Row, short Col)
{
    public static Av1MotionVector Zero => default;

    public bool IsZero => this.Row == 0 && this.Col == 0;

    public static Av1MotionVector operator +(Av1MotionVector a, Av1MotionVector b)
        => new((short)(a.Row + b.Row), (short)(a.Col + b.Col));
}
