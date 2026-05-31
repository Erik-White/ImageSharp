// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Formats.Heif.Av1.Tiling;
using SixLabors.ImageSharp.Formats.Heif.Av1.Transform;

namespace SixLabors.ImageSharp.Formats.Heif.Av1.Entropy;

internal static class Av1NzMap
{
    // SIG_COEF_CONTEXTS_2D = 26
    private const int NzMapContext0 = 26;
    private const int NzMapContext5 = NzMapContext0 + 5;
    private const int NzMapContext10 = NzMapContext0 + 10;

    private static readonly int[] NzMapContextOffset1d = [
        NzMapContext0,  NzMapContext5,  NzMapContext10, NzMapContext10, NzMapContext10, NzMapContext10, NzMapContext10,
        NzMapContext10, NzMapContext10, NzMapContext10, NzMapContext10, NzMapContext10, NzMapContext10, NzMapContext10,
        NzMapContext10, NzMapContext10, NzMapContext10, NzMapContext10, NzMapContext10, NzMapContext10, NzMapContext10,
        NzMapContext10, NzMapContext10, NzMapContext10, NzMapContext10, NzMapContext10, NzMapContext10, NzMapContext10,
        NzMapContext10, NzMapContext10, NzMapContext10, NzMapContext10,
    ];

    // Spec 8.3.2 Coeff_Base_Ctx_Offset[txSz][Min(row,4)][Min(col,4)], 19 transform sizes each
    // flattened row-major as a 5x5 block (index = Min(row,4) * 5 + Min(col,4)). row/col derive
    // from the row-major scan position: row = pos >> bwl, col = pos - (row << bwl), with
    // bwl = Tx_Width_Log2[Adjusted_Tx_Size[txSz]].
    private static readonly int[][] CoeffBaseContextOffset = [
        [0, 1, 6, 6, 0, 1, 6, 6, 21, 0, 6, 6, 21, 21, 0, 6, 21, 21, 21, 0, 0, 0, 0, 0, 0], // TX_4x4
        [0, 1, 6, 6, 21, 1, 6, 6, 21, 21, 6, 6, 21, 21, 21, 6, 21, 21, 21, 21, 21, 21, 21, 21, 21], // TX_8x8
        [0, 1, 6, 6, 21, 1, 6, 6, 21, 21, 6, 6, 21, 21, 21, 6, 21, 21, 21, 21, 21, 21, 21, 21, 21], // TX_16x16
        [0, 1, 6, 6, 21, 1, 6, 6, 21, 21, 6, 6, 21, 21, 21, 6, 21, 21, 21, 21, 21, 21, 21, 21, 21], // TX_32x32
        [0, 1, 6, 6, 21, 1, 6, 6, 21, 21, 6, 6, 21, 21, 21, 6, 21, 21, 21, 21, 21, 21, 21, 21, 21], // TX_64x64
        [0, 11, 11, 11, 0, 11, 11, 11, 11, 0, 6, 6, 21, 21, 0, 6, 21, 21, 21, 0, 21, 21, 21, 21, 0], // TX_4x8
        [0, 16, 6, 6, 21, 16, 16, 6, 21, 21, 16, 16, 21, 21, 21, 16, 16, 21, 21, 21, 0, 0, 0, 0, 0], // TX_8x4
        [0, 11, 11, 11, 11, 11, 11, 11, 11, 11, 6, 6, 21, 21, 21, 6, 21, 21, 21, 21, 21, 21, 21, 21, 21], // TX_8x16
        [0, 16, 6, 6, 21, 16, 16, 6, 21, 21, 16, 16, 21, 21, 21, 16, 16, 21, 21, 21, 16, 16, 21, 21, 21], // TX_16x8
        [0, 11, 11, 11, 11, 11, 11, 11, 11, 11, 6, 6, 21, 21, 21, 6, 21, 21, 21, 21, 21, 21, 21, 21, 21], // TX_16x32
        [0, 16, 6, 6, 21, 16, 16, 6, 21, 21, 16, 16, 21, 21, 21, 16, 16, 21, 21, 21, 16, 16, 21, 21, 21], // TX_32x16
        [0, 11, 11, 11, 11, 11, 11, 11, 11, 11, 6, 6, 21, 21, 21, 6, 21, 21, 21, 21, 21, 21, 21, 21, 21], // TX_32x64
        [0, 16, 6, 6, 21, 16, 16, 6, 21, 21, 16, 16, 21, 21, 21, 16, 16, 21, 21, 21, 16, 16, 21, 21, 21], // TX_64x32
        [0, 11, 11, 11, 0, 11, 11, 11, 11, 0, 6, 6, 21, 21, 0, 6, 21, 21, 21, 0, 21, 21, 21, 21, 0], // TX_4x16
        [0, 16, 6, 6, 21, 16, 16, 6, 21, 21, 16, 16, 21, 21, 21, 16, 16, 21, 21, 21, 0, 0, 0, 0, 0], // TX_16x4
        [0, 11, 11, 11, 11, 11, 11, 11, 11, 11, 6, 6, 21, 21, 21, 6, 21, 21, 21, 21, 21, 21, 21, 21, 21], // TX_8x32
        [0, 16, 6, 6, 21, 16, 16, 6, 21, 21, 16, 16, 21, 21, 21, 16, 16, 21, 21, 21, 16, 16, 21, 21, 21], // TX_32x8
        [0, 11, 11, 11, 11, 11, 11, 11, 11, 11, 6, 6, 21, 21, 21, 6, 21, 21, 21, 21, 21, 21, 21, 21, 21], // TX_16x64
        [0, 16, 6, 6, 21, 16, 16, 6, 21, 21, 16, 16, 21, 21, 21, 16, 16, 21, 21, 21, 16, 16, 21, 21, 21], // TX_64x16
    ];

    /// <summary>
    /// SVT: get_nz_mag
    /// </summary>
    public static int GetNzMagnitude(Av1LevelBuffer levels, Point position, Av1TransformClass transformClass)
    {
        int mag;
        Span<byte> row0 = levels.GetRow(position.Y)[position.X..];
        Span<byte> row1 = levels.GetRow(position.Y + 1)[position.X..];
        Span<byte> row2 = levels.GetRow(position.Y + 2)[position.X..];

        // Note: AOMMIN(level, 3) is useless for decoder since level < 3.
        mag = ClipMax3(row0[1]); // { 0, 1 }
        mag += ClipMax3(row1[0]); // { 1, 0 }

        switch (transformClass)
        {
            case Av1TransformClass.Class2D:
                mag += ClipMax3(row1[1]); // { 1, 1 }
                mag += ClipMax3(row0[2]); // { 0, 2 }
                mag += ClipMax3(row2[0]); // { 2, 0 }
                break;

            case Av1TransformClass.ClassVertical:
                Span<byte> row3 = levels.GetRow(position.Y + 3)[position.X..];
                Span<byte> row4 = levels.GetRow(position.Y + 4)[position.X..];
                mag += ClipMax3(row2[0]); // { 2, 0 }
                mag += ClipMax3(row3[0]); // { 3, 0 }
                mag += ClipMax3(row4[0]); // { 4, 0 }
                break;
            case Av1TransformClass.ClassHorizontal:
                mag += ClipMax3(row0[2]); // { 0, 2 }
                mag += ClipMax3(row0[3]); // { 0, 3 }
                mag += ClipMax3(row0[4]); // { 0, 4 }
                break;
        }

        return mag;
    }

    public static int GetNzMapContextFromStats(int stats, Point position, Av1TransformSize transformSize, Av1TransformClass transformClass)
    {
        // tx_class == 0(TX_CLASS_2D)
        if (transformClass == 0 && (position.X == 0) && (position.Y == 0))
        {
            return 0;
        }

        int ctx = (stats + 1) >> 1;
        ctx = Math.Min(ctx, 4);
        switch (transformClass)
        {
            case Av1TransformClass.Class2D:
                return ctx + GetNzMapContext(transformSize, position);
            case Av1TransformClass.ClassHorizontal:
                return ctx + NzMapContextOffset1d[position.X];
            case Av1TransformClass.ClassVertical:
                return ctx + NzMapContextOffset1d[position.Y];
            default:
                break;
        }

        return 0;
    }

    // Spec 8.3.2 get_coeff_base_ctx: ctx + Coeff_Base_Ctx_Offset[txSz][Min(row,4)][Min(col,4)].
    // The position is already the row-major (col=X, row=Y) decode of the scan value, so index
    // the 5x5 block directly.
    public static int GetNzMapContext(Av1TransformSize transformSize, Point pos)
    {
        int row = Math.Min(pos.Y, 4);
        int col = Math.Min(pos.X, 4);
        return CoeffBaseContextOffset[(int)transformSize][(row * 5) + col];
    }

    private static int ClipMax3(int value) => Math.Min(value, 3);
}
