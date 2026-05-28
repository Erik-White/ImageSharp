// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Formats.Heif.Av1.LoopRestoration;

/// <summary>
/// Loop-restoration constants ported from libaom <c>av1/common/restoration.h</c>. Names
/// preserve libaom's naming so cross-references (and the spec's section 7.17 wording)
/// continue to apply.
/// </summary>
internal static class Av1RestorationConstants
{
    public const int WienerHalfWin = 3;
    public const int WienerWin = (2 * WienerHalfWin) + 1;
    public const int WienerWinChroma = WienerWin - 2;

    /// <summary>
    /// libaom WIENER_FILT_PREC_BITS / WIENER_FILT_STEP. The implicit centre tap is
    /// <c>STEP - 2 * (tap0 + tap1 + tap2)</c>.
    /// </summary>
    public const int WienerFilterPrecisionBits = 7;
    public const int WienerFilterStep = 1 << WienerFilterPrecisionBits;

    public const int WienerFilterTap0MidValue = 3;
    public const int WienerFilterTap1MidValue = -7;
    public const int WienerFilterTap2MidValue = 15;

    public const int WienerFilterTap0Bits = 4;
    public const int WienerFilterTap1Bits = 5;
    public const int WienerFilterTap2Bits = 6;

    public const int WienerFilterTap0MinValue = WienerFilterTap0MidValue - ((1 << WienerFilterTap0Bits) / 2);
    public const int WienerFilterTap0MaxValue = WienerFilterTap0MidValue - 1 + ((1 << WienerFilterTap0Bits) / 2);
    public const int WienerFilterTap1MinValue = WienerFilterTap1MidValue - ((1 << WienerFilterTap1Bits) / 2);
    public const int WienerFilterTap1MaxValue = WienerFilterTap1MidValue - 1 + ((1 << WienerFilterTap1Bits) / 2);
    public const int WienerFilterTap2MinValue = WienerFilterTap2MidValue - ((1 << WienerFilterTap2Bits) / 2);
    public const int WienerFilterTap2MaxValue = WienerFilterTap2MidValue - 1 + ((1 << WienerFilterTap2Bits) / 2);

    public const int WienerFilterTap0SubexpK = 1;
    public const int WienerFilterTap1SubexpK = 2;
    public const int WienerFilterTap2SubexpK = 3;

    public const int SgrProjParamsBits = 4;
    public const int SgrProjParamsCount = 1 << SgrProjParamsBits;

    public const int SgrProjPrjBits = 7;

    public const int SgrProjPrjMin0 = -((1 << SgrProjPrjBits) * 3 / 4);
    public const int SgrProjPrjMax0 = SgrProjPrjMin0 + (1 << SgrProjPrjBits) - 1;
    public const int SgrProjPrjMin1 = -((1 << SgrProjPrjBits) / 4);
    public const int SgrProjPrjMax1 = SgrProjPrjMin1 + (1 << SgrProjPrjBits) - 1;

    public const int SgrProjPrjSubexpK = 4;

    /// <summary>
    /// libaom <c>av1_sgr_params</c>: 16 entries each selecting two radii (r[0], r[1])
    /// and two sigma-derived multipliers (e[0], e[1]). When <c>r[i] == 0</c> the
    /// corresponding pass is disabled and only <c>xqd[1-i]</c> is signalled.
    /// </summary>
    public static readonly (int R0, int R1, int E0, int E1)[] SgrParams =
    [
        (2, 1, 140, 3236),
        (2, 1, 112, 2158),
        (2, 1, 93, 1618),
        (2, 1, 80, 1438),
        (2, 1, 70, 1295),
        (2, 1, 58, 1177),
        (2, 1, 47, 1079),
        (2, 1, 37, 996),
        (2, 1, 30, 925),
        (2, 1, 25, 863),
        (0, 1, -1, 2589),
        (0, 1, -1, 1618),
        (0, 1, -1, 1177),
        (0, 1, -1, 925),
        (2, 0, 56, -1),
        (2, 0, 22, -1),
    ];
}
