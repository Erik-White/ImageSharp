// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Formats.Heif.Av1.Prediction;
using SixLabors.ImageSharp.Formats.Heif.Av1.Tiling.MotionVector;

namespace SixLabors.ImageSharp.Formats.Heif.Av1.Tiling;

internal class Av1BlockModeInfo
{
    public const int PaletteMaxSize = 8;

    private int[] paletteSize;

    public Av1BlockModeInfo(Av1BlockSize blockSize, Point positionInSuperblock)
    {
        this.BlockSize = blockSize;
        this.PositionInSuperblock = positionInSuperblock;

        // Two slots per array — one per Av1PlaneType (Y, Uv). The UV slot stays
        // unread in monochrome but the parser unconditionally writes both.
        this.AngleDelta = new int[2];
        this.paletteSize = new int[2];
        this.PaletteColorsY = [];
        this.PaletteColorsU = [];
        this.PaletteColorsV = [];
        this.FilterIntraModeInfo = new();
        this.FirstTransformLocation = new int[2];
        this.TransformUnitsCount = new int[2];
    }

    public Av1BlockSize BlockSize { get; }

    /// <summary>
    /// Gets or sets the <see cref="Av1PredictionMode"/> for the luminance channel.
    /// </summary>
    public Av1PredictionMode YMode { get; set; }

    public bool Skip { get; set; }

    public Av1PartitionType PartitionType { get; set; }

    public bool SkipMode { get; set; }

    public int SegmentId { get; set; }

    /// <summary>
    /// Gets or sets the <see cref="Av1PredictionMode"/> for the chroma channels.
    /// </summary>
    public Av1PredictionMode UvMode { get; set; }

    public bool UseIntraBlockCopy { get; set; }

    /// <summary>
    /// Gets or sets the displacement vector for an intra-block-copy block.
    /// 1/8-pel units; low 3 bits are zero (integer-pel only). Default zero
    /// when the block is not IBC or before assignment.
    /// </summary>
    public Av1MotionVector DisplacementVector { get; set; }

    public int ChromaFromLumaAlphaIndex { get; set; }

    public int ChromaFromLumaAlphaSign { get; set; }

    public int[] AngleDelta { get; set; }

    /// <summary>
    /// Gets the position relative to the Superblock, counted in mode info (4x4 pixels).
    /// </summary>
    public Point PositionInSuperblock { get; }

    public Av1IntraFilterModeInfo FilterIntraModeInfo { get; internal set; }

    /// <summary>
    /// Gets the index of the first <see cref="Av1TransformInfo"/> of this Mode Info in the <see cref="Av1FrameInfo"/>.
    /// </summary>
    public int[] FirstTransformLocation { get; }

    public int[] TransformUnitsCount { get; internal set; }

    /// <summary>
    /// Gets or sets the sorted base colors for the luminance palette (length == GetPaletteSize(Y)).
    /// </summary>
    public ushort[] PaletteColorsY { get; set; }

    /// <summary>
    /// Gets or sets the sorted base colors for the U palette (length == GetPaletteSize(Uv)).
    /// </summary>
    public ushort[] PaletteColorsU { get; set; }

    /// <summary>
    /// Gets or sets the V palette colors (length == GetPaletteSize(Uv); not sorted, U is the cache key).
    /// </summary>
    public ushort[] PaletteColorsV { get; set; }

    /// <summary>
    /// Gets or sets the per-sample color index map for the Y plane, in row-major order
    /// with stride <see cref="ColorMapWidthY"/>. Empty when the block does not use a Y palette.
    /// </summary>
    public byte[] ColorIndexMapY { get; set; } = [];

    /// <summary>
    /// Gets or sets the per-sample color index map shared by the U and V planes, in row-major
    /// order with stride <see cref="ColorMapWidthUv"/>. Empty when the block does not use a UV palette.
    /// </summary>
    public byte[] ColorIndexMapUv { get; set; } = [];

    /// <summary>
    /// Gets or sets the row stride of <see cref="ColorIndexMapY"/> in samples. Equals the block
    /// plane width (with chroma 4-sample padding when applicable, though Y is never padded).
    /// </summary>
    public int ColorMapWidthY { get; set; }

    /// <summary>
    /// Gets or sets the row stride of <see cref="ColorIndexMapUv"/> in samples.
    /// </summary>
    public int ColorMapWidthUv { get; set; }

    public int GetPaletteSize(Av1Plane plane) => this.paletteSize[Math.Min(1, (int)plane)];

    public int GetPaletteSize(Av1PlaneType planeType) => this.paletteSize[(int)planeType];

    public void SetPaletteSizes(int ySize, int uvSize) => this.paletteSize = [ySize, uvSize];
}
