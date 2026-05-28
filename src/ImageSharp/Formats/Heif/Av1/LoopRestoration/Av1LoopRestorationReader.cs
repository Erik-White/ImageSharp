// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Formats.Heif.Av1.Entropy;
using SixLabors.ImageSharp.Formats.Heif.Av1.OpenBitstreamUnit;

namespace SixLabors.ImageSharp.Formats.Heif.Av1.LoopRestoration;

/// <summary>
/// Spec 5.11.4 (<c>read_lr_unit</c>) and the libaom equivalents
/// <c>loop_restoration_read_sb_coeffs</c>, <c>read_wiener_filter</c>,
/// <c>read_sgrproj_filter</c>: reads one restoration unit's filter selection plus
/// (when applicable) its coefficients, threaded through a per-plane reference state
/// so consecutive units can delta-code their parameters.
/// </summary>
internal static class Av1LoopRestorationReader
{
    /// <summary>
    /// Reads the per-unit filter selection and coefficients. <paramref name="frameType"/>
    /// is the plane's <c>frame_restoration_type</c>; the per-unit value is signalled
    /// only when the frame-level type is non-NONE.
    /// </summary>
    public static void ReadUnit(
        ref Av1SymbolDecoder reader,
        ObuRestorationType frameType,
        Av1RestorationUnitInfo unit,
        Av1WienerInfo referenceWiener,
        Av1SgrProjInfo referenceSgrProj,
        bool isChroma)
    {
        if (frameType == ObuRestorationType.None)
        {
            unit.RestorationType = ObuRestorationType.None;
            return;
        }

        ObuRestorationType type = frameType switch
        {
            ObuRestorationType.Switchable => reader.ReadSwitchableRestoration(),
            ObuRestorationType.Weiner => reader.ReadUsesWienerRestoration() ? ObuRestorationType.Weiner : ObuRestorationType.None,
            ObuRestorationType.SgrProj => reader.ReadUsesSgrProjRestoration() ? ObuRestorationType.SgrProj : ObuRestorationType.None,
            _ => ObuRestorationType.None,
        };
        unit.RestorationType = type;

        if (type == ObuRestorationType.Weiner)
        {
            ReadWienerFilter(ref reader, unit.WienerInfo, referenceWiener, isChroma);
        }
        else if (type == ObuRestorationType.SgrProj)
        {
            ReadSgrProjFilter(ref reader, unit.SgrProjInfo, referenceSgrProj);
        }
    }

    /// <summary>
    /// libaom <c>read_wiener_filter</c>: three taps per pass (horizontal + vertical),
    /// each delta-coded against the previous unit's filter; the centre tap is implicit
    /// (<c>STEP - 2*(tap0 + tap1 + tap2)</c>) and the right/bottom halves mirror the
    /// left/top. Chroma uses a 5-tap filter so tap[0] is forced to zero.
    /// </summary>
    private static void ReadWienerFilter(
        ref Av1SymbolDecoder reader,
        Av1WienerInfo wiener,
        Av1WienerInfo reference,
        bool isChroma)
    {
        Array.Clear(wiener.VerticalFilter);
        Array.Clear(wiener.HorizontalFilter);

        int wienerWin = isChroma ? Av1RestorationConstants.WienerWinChroma : Av1RestorationConstants.WienerWin;

        ReadWienerTaps(ref reader, wiener.VerticalFilter, reference.VerticalFilter, wienerWin);
        ReadWienerTaps(ref reader, wiener.HorizontalFilter, reference.HorizontalFilter, wienerWin);

        reference.CopyFrom(wiener);
    }

    private static void ReadWienerTaps(
        ref Av1SymbolDecoder reader,
        int[] filter,
        int[] referenceFilter,
        int wienerWin)
    {
        int tap0;
        if (wienerWin == Av1RestorationConstants.WienerWin)
        {
            tap0 = reader.ReadPrimitiveReferenceSubexpFin(
                Av1RestorationConstants.WienerFilterTap0MaxValue - Av1RestorationConstants.WienerFilterTap0MinValue + 1,
                Av1RestorationConstants.WienerFilterTap0SubexpK,
                referenceFilter[0] - Av1RestorationConstants.WienerFilterTap0MinValue) + Av1RestorationConstants.WienerFilterTap0MinValue;
        }
        else
        {
            tap0 = 0;
        }

        int tap1 = reader.ReadPrimitiveReferenceSubexpFin(
            Av1RestorationConstants.WienerFilterTap1MaxValue - Av1RestorationConstants.WienerFilterTap1MinValue + 1,
            Av1RestorationConstants.WienerFilterTap1SubexpK,
            referenceFilter[1] - Av1RestorationConstants.WienerFilterTap1MinValue) + Av1RestorationConstants.WienerFilterTap1MinValue;

        int tap2 = reader.ReadPrimitiveReferenceSubexpFin(
            Av1RestorationConstants.WienerFilterTap2MaxValue - Av1RestorationConstants.WienerFilterTap2MinValue + 1,
            Av1RestorationConstants.WienerFilterTap2SubexpK,
            referenceFilter[2] - Av1RestorationConstants.WienerFilterTap2MinValue) + Av1RestorationConstants.WienerFilterTap2MinValue;

        // Mirror taps about the centre. Centre is implicit: -2 * sum-of-side-taps + STEP.
        filter[0] = tap0;
        filter[Av1RestorationConstants.WienerWin - 1] = tap0;
        filter[1] = tap1;
        filter[Av1RestorationConstants.WienerWin - 2] = tap1;
        filter[2] = tap2;
        filter[Av1RestorationConstants.WienerWin - 3] = tap2;
        filter[Av1RestorationConstants.WienerHalfWin] = Av1RestorationConstants.WienerFilterStep - (2 * (tap0 + tap1 + tap2));
    }

    /// <summary>
    /// libaom <c>read_sgrproj_filter</c>: 4-bit parameter index plus up to two
    /// projection coefficients. When <c>params.r[i] == 0</c> the corresponding pass is
    /// disabled and only the other coefficient is signalled (the disabled side is
    /// either zero or derived to make the projection sum to a fixed value).
    /// </summary>
    private static void ReadSgrProjFilter(
        ref Av1SymbolDecoder reader,
        Av1SgrProjInfo sgrProj,
        Av1SgrProjInfo reference)
    {
        sgrProj.Ep = reader.ReadLiteralBits(Av1RestorationConstants.SgrProjParamsBits);
        (int r0, int r1, _, _) = Av1RestorationConstants.SgrParams[sgrProj.Ep];

        if (r0 == 0)
        {
            sgrProj.Xqd[0] = 0;
            sgrProj.Xqd[1] = reader.ReadPrimitiveReferenceSubexpFin(
                Av1RestorationConstants.SgrProjPrjMax1 - Av1RestorationConstants.SgrProjPrjMin1 + 1,
                Av1RestorationConstants.SgrProjPrjSubexpK,
                reference.Xqd[1] - Av1RestorationConstants.SgrProjPrjMin1) + Av1RestorationConstants.SgrProjPrjMin1;
        }
        else if (r1 == 0)
        {
            sgrProj.Xqd[0] = reader.ReadPrimitiveReferenceSubexpFin(
                Av1RestorationConstants.SgrProjPrjMax0 - Av1RestorationConstants.SgrProjPrjMin0 + 1,
                Av1RestorationConstants.SgrProjPrjSubexpK,
                reference.Xqd[0] - Av1RestorationConstants.SgrProjPrjMin0) + Av1RestorationConstants.SgrProjPrjMin0;
            sgrProj.Xqd[1] = Math.Clamp(
                (1 << Av1RestorationConstants.SgrProjPrjBits) - sgrProj.Xqd[0],
                Av1RestorationConstants.SgrProjPrjMin1,
                Av1RestorationConstants.SgrProjPrjMax1);
        }
        else
        {
            sgrProj.Xqd[0] = reader.ReadPrimitiveReferenceSubexpFin(
                Av1RestorationConstants.SgrProjPrjMax0 - Av1RestorationConstants.SgrProjPrjMin0 + 1,
                Av1RestorationConstants.SgrProjPrjSubexpK,
                reference.Xqd[0] - Av1RestorationConstants.SgrProjPrjMin0) + Av1RestorationConstants.SgrProjPrjMin0;
            sgrProj.Xqd[1] = reader.ReadPrimitiveReferenceSubexpFin(
                Av1RestorationConstants.SgrProjPrjMax1 - Av1RestorationConstants.SgrProjPrjMin1 + 1,
                Av1RestorationConstants.SgrProjPrjSubexpK,
                reference.Xqd[1] - Av1RestorationConstants.SgrProjPrjMin1) + Av1RestorationConstants.SgrProjPrjMin1;
        }

        reference.CopyFrom(sgrProj);
    }
}
