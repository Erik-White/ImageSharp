// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Formats.Heif.Av1.Entropy;

namespace SixLabors.ImageSharp.Formats.Heif.Av1.Tiling.MotionVector;

/// <summary>
/// Inverse of <see cref="Av1MotionVectorReader"/>: writes a motion-vector
/// difference (5.11.31, 5.11.32). Currently exists primarily to support
/// round-trip testing of the decoder symbol layer.
/// </summary>
internal static class Av1MotionVectorWriter
{
    public static void WriteMotionVectorDifference(
        Av1SymbolEncoder encoder,
        Av1MotionVectorContext ctx,
        Av1MotionVector difference,
        bool forceIntegerMv,
        bool allowHighPrecisionMv)
    {
        Av1MotionVectorJoint joint = ComputeJoint(difference);
        encoder.WriteMotionVectorJoint(ctx, joint);

        if (joint is Av1MotionVectorJoint.VerticalNonZero or Av1MotionVectorJoint.HorizontalAndVerticalNonZero)
        {
            WriteComponent(encoder, ctx, difference.Row, Av1MotionVectorComponent.Vertical, forceIntegerMv, allowHighPrecisionMv);
        }

        if (joint is Av1MotionVectorJoint.HorizontalNonZero or Av1MotionVectorJoint.HorizontalAndVerticalNonZero)
        {
            WriteComponent(encoder, ctx, difference.Col, Av1MotionVectorComponent.Horizontal, forceIntegerMv, allowHighPrecisionMv);
        }
    }

    private static Av1MotionVectorJoint ComputeJoint(Av1MotionVector mv)
    {
        bool rowNonZero = mv.Row != 0;
        bool colNonZero = mv.Col != 0;
        return (rowNonZero, colNonZero) switch
        {
            (false, false) => Av1MotionVectorJoint.Zero,
            (false, true) => Av1MotionVectorJoint.HorizontalNonZero,
            (true, false) => Av1MotionVectorJoint.VerticalNonZero,
            (true, true) => Av1MotionVectorJoint.HorizontalAndVerticalNonZero,
        };
    }

    private static void WriteComponent(
        Av1SymbolEncoder encoder,
        Av1MotionVectorContext ctx,
        int signedValue,
        Av1MotionVectorComponent comp,
        bool forceIntegerMv,
        bool allowHighPrecisionMv)
    {
        bool sign = signedValue < 0;
        encoder.WriteMotionVectorSign(ctx, sign, comp);

        int magnitude = sign ? -signedValue : signedValue;

        // Inverse of read_mv_component: extract class from magnitude. The reader
        // computes magnitude in two cases (class==0 vs class>0); we mirror by
        // first deriving the class from the magnitude before the +1 offset.
        int magnitudeMinusOne = magnitude - 1;
        int mvClass = ClassifyMagnitude(magnitudeMinusOne);
        encoder.WriteMotionVectorClass(ctx, mvClass, comp);

        if (mvClass == 0)
        {
            int class0Bit = (magnitudeMinusOne >> 3) & 1;
            int fr = (magnitudeMinusOne >> 1) & 3;
            int hp = magnitudeMinusOne & 1;
            encoder.WriteMotionVectorClass0Bit(ctx, class0Bit, comp);

            if (!forceIntegerMv)
            {
                encoder.WriteMotionVectorClass0Fraction(ctx, fr, comp, class0Bit);
            }

            if (allowHighPrecisionMv)
            {
                encoder.WriteMotionVectorClass0HighPrecision(ctx, hp, comp);
            }
        }
        else
        {
            int rest = magnitudeMinusOne - (Av1MotionVectorConstants.Class0Size << (mvClass + 2));
            int d = (rest >> 3) & ((1 << mvClass) - 1);
            int fr = (rest >> 1) & 3;
            int hp = rest & 1;
            for (int i = 0; i < mvClass; i++)
            {
                int bit = (d >> i) & 1;
                encoder.WriteMotionVectorBit(ctx, bit, comp, i);
            }

            if (!forceIntegerMv)
            {
                encoder.WriteMotionVectorFraction(ctx, fr, comp);
            }

            if (allowHighPrecisionMv)
            {
                encoder.WriteMotionVectorHighPrecision(ctx, hp, comp);
            }
        }
    }

    private static int ClassifyMagnitude(int magnitudeMinusOne)
    {
        // Class 0 covers magnitudes 1..16 (mag-1 in 0..15, top bit at 4).
        // Class k>0 has range CLASS0_SIZE<<(k+2) .. (CLASS0_SIZE<<(k+3)) - 1.
        if (magnitudeMinusOne < (Av1MotionVectorConstants.Class0Size << 3))
        {
            return 0;
        }

        for (int k = 1; k < Av1MotionVectorConstants.Classes; k++)
        {
            int upperExclusive = Av1MotionVectorConstants.Class0Size << (k + 3);
            if (magnitudeMinusOne < upperExclusive)
            {
                return k;
            }
        }

        return Av1MotionVectorConstants.Classes - 1;
    }
}
