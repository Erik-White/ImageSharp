// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Formats.Heif.Av1.OpenBitstreamUnit;

namespace SixLabors.ImageSharp.Formats.Heif.Av1.Pipeline.LoopFilter;

/// <summary>
/// Per-frame loop-filter threshold tables, indexed by filter strength level. Each entry holds
/// the (limit, blimit, thresh) triplet specified by section 7.14.4 (adaptive filter strength
/// process) of the AV1 specification.
/// </summary>
/// <remarks>
/// The threshold values are derived once per frame from <c>loop_filter_level</c> and
/// <c>loop_filter_sharpness</c>, and looked up by per-edge filter strength rather than recomputed
/// for each edge.
/// </remarks>
internal sealed class Av1LoopFilterContext
{
    public const int MaxLoopFilter = 63;

    private readonly Av1LoopFilterThreshold[] thresholds;

    public Av1LoopFilterContext(ObuLoopFilterParameters lfParams)
    {
        this.thresholds = new Av1LoopFilterThreshold[MaxLoopFilter + 1];
        int sharpnessLevel = lfParams.SharpnessLevel;
        int rightShift = (sharpnessLevel > 0 ? 1 : 0) + (sharpnessLevel > 4 ? 1 : 0);
        int sharpnessCap = 9 - sharpnessLevel;

        for (int level = 0; level <= MaxLoopFilter; level++)
        {
            int innerLimit = level >> rightShift;
            if (sharpnessLevel > 0)
            {
                innerLimit = Math.Min(innerLimit, sharpnessCap);
            }

            innerLimit = Math.Max(innerLimit, 1);

            this.thresholds[level] = new Av1LoopFilterThreshold(
                Limit: (byte)innerLimit,
                MbLimit: (byte)((2 * (level + 2)) + innerLimit),
                HevThreshold: (byte)(level >> 4));
        }
    }

    public ref readonly Av1LoopFilterThreshold GetThreshold(int level) => ref this.thresholds[level];
}
