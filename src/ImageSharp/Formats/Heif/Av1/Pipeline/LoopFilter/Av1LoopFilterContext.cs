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
internal class Av1LoopFilterContext
{
    public const int MaxLoopFilter = 63;
    public const int MaxModeLoopFilterDeltas = 2;

    private readonly Av1LoopFilterThreshold[] thresholds = new Av1LoopFilterThreshold[MaxLoopFilter + 1];

    public Av1LoopFilterContext()
    {
        for (int i = 0; i <= MaxLoopFilter; i++)
        {
            this.thresholds[i] = new Av1LoopFilterThreshold();
        }
    }

    public Av1LoopFilterThreshold GetThreshold(int level) => this.thresholds[level];

    /// <summary>
    /// Initializes the threshold table for the entire range of filter strength levels using the
    /// formulas in section 7.14.4 of the AV1 specification.
    /// </summary>
    public void Initialize(ObuLoopFilterParameters lfParams)
    {
        this.UpdateSharpness(lfParams.SharpnessLevel);
        for (int lvl = 0; lvl <= MaxLoopFilter; lvl++)
        {
            this.thresholds[lvl].HevThreshold = (byte)(lvl >> 4);
        }
    }

    private void UpdateSharpness(int sharpnessLevel)
    {
        for (int lvl = 0; lvl <= MaxLoopFilter; lvl++)
        {
            int rightShift = (sharpnessLevel > 0 ? 1 : 0) + (sharpnessLevel > 4 ? 1 : 0);
            int blockInsideLimit = lvl >> rightShift;

            if (sharpnessLevel > 0 && blockInsideLimit > (9 - sharpnessLevel))
            {
                blockInsideLimit = 9 - sharpnessLevel;
            }

            if (blockInsideLimit < 1)
            {
                blockInsideLimit = 1;
            }

            this.thresholds[lvl].Limit = (byte)blockInsideLimit;
            this.thresholds[lvl].MbLimit = (byte)((2 * (lvl + 2)) + blockInsideLimit);
        }
    }
}

internal class Av1LoopFilterThreshold
{
    public byte MbLimit { get; set; }

    public byte Limit { get; set; }

    public byte HevThreshold { get; set; }
}
