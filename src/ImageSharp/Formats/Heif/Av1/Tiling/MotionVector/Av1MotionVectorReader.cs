// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Formats.Heif.Av1.Entropy;

namespace SixLabors.ImageSharp.Formats.Heif.Av1.Tiling.MotionVector;

/// <summary>
/// Spec 5.11.31 (<c>read_mv</c>) and 5.11.32 (<c>read_mv_component</c>). Decodes
/// a motion-vector difference in 1/8-pel units. Under intra block copy,
/// <c>force_integer_mv</c> is on and <c>allow_high_precision_mv</c> is off, so
/// the result has its low 3 bits zero (integer-pel only).
/// </summary>
internal static class Av1MotionVectorReader
{
    /// <summary>
    /// Spec 5.11.26 (<c>assign_mv</c>) for intra-block-copy blocks. Builds the
    /// IBC predictor stack from already-decoded neighbors (falling back to the
    /// synthesized predictor when no IBC candidate is found), reads the
    /// motion-vector difference, and writes the resulting absolute DV onto the
    /// block's mode info.
    /// </summary>
    public static void AssignIntraBlockCopyMotionVector(
        ref Av1SymbolDecoder reader,
        Av1PartitionInfo partitionInfo,
        Av1TileInfo tileInfo,
        int superblockModeInfoSize,
        Av1IntraBlockCopyMotionVectorStack.MiLookup lookupMi)
    {
        Av1BlockSize blockSize = partitionInfo.ModeInfo.BlockSize;
        Av1MotionVector predictor = Av1IntraBlockCopyMotionVectorStack.BuildPredictor(
            lookupMi,
            tileInfo,
            superblockModeInfoSize,
            partitionInfo.RowIndex,
            partitionInfo.ColumnIndex,
            blockSize.Get4x4WideCount(),
            blockSize.Get4x4HighCount());
        Av1MotionVector difference = ReadMotionVectorDifference(
            ref reader, Av1MotionVectorContext.IntraBlockCopy, forceIntegerMv: true, allowHighPrecisionMv: false);
        Av1MotionVector finalDv = predictor + difference;
        partitionInfo.ModeInfo.DisplacementVector = finalDv;

        if (Av1SymbolTrace.IbcEnabled)
        {
            Av1SymbolTrace.WriteIbcDv(
                partitionInfo.RowIndex,
                partitionInfo.ColumnIndex,
                (int)blockSize,
                predictor.Row,
                predictor.Col,
                finalDv.Row,
                finalDv.Col);
        }
    }

    /// <summary>
    /// Reads the motion-vector difference relative to a predictor. The returned
    /// vector is the diff only; callers add it to <c>PredMv</c>.
    /// </summary>
    public static Av1MotionVector ReadMotionVectorDifference(
        ref Av1SymbolDecoder reader,
        Av1MotionVectorContext ctx,
        bool forceIntegerMv,
        bool allowHighPrecisionMv)
    {
        Av1MotionVectorJoint joint = reader.ReadMotionVectorJoint(ctx);
        short row = 0;
        short col = 0;

        if (joint is Av1MotionVectorJoint.VerticalNonZero or Av1MotionVectorJoint.HorizontalAndVerticalNonZero)
        {
            row = (short)ReadComponent(ref reader, ctx, Av1MotionVectorComponent.Vertical, forceIntegerMv, allowHighPrecisionMv);
        }

        if (joint is Av1MotionVectorJoint.HorizontalNonZero or Av1MotionVectorJoint.HorizontalAndVerticalNonZero)
        {
            col = (short)ReadComponent(ref reader, ctx, Av1MotionVectorComponent.Horizontal, forceIntegerMv, allowHighPrecisionMv);
        }

        return new Av1MotionVector(row, col);
    }

    private static int ReadComponent(
        ref Av1SymbolDecoder reader,
        Av1MotionVectorContext ctx,
        Av1MotionVectorComponent comp,
        bool forceIntegerMv,
        bool allowHighPrecisionMv)
    {
        bool sign = reader.ReadMotionVectorSign(ctx, comp);
        int mvClass = reader.ReadMotionVectorClass(ctx, comp);
        int magnitude;

        if (mvClass == 0)
        {
            int class0Bit = reader.ReadMotionVectorClass0Bit(ctx, comp);
            int fr = forceIntegerMv ? 3 : reader.ReadMotionVectorClass0Fraction(ctx, comp, class0Bit);
            int hp = allowHighPrecisionMv ? reader.ReadMotionVectorClass0HighPrecision(ctx, comp) : 1;
            magnitude = ((class0Bit << 3) | (fr << 1) | hp) + 1;
        }
        else
        {
            int d = 0;
            for (int i = 0; i < mvClass; i++)
            {
                int bit = reader.ReadMotionVectorBit(ctx, comp, i);
                d |= bit << i;
            }

            magnitude = Av1MotionVectorConstants.Class0Size << (mvClass + 2);
            int fr = forceIntegerMv ? 3 : reader.ReadMotionVectorFraction(ctx, comp);
            int hp = allowHighPrecisionMv ? reader.ReadMotionVectorHighPrecision(ctx, comp) : 1;
            magnitude += ((d << 3) | (fr << 1) | hp) + 1;
        }

        return sign ? -magnitude : magnitude;
    }
}
