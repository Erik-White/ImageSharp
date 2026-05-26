// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Formats.Heif.Av1.Pipeline.LoopFilter;

/// <summary>
/// Direction of the deblocking edge being filtered. The enum values are aligned with the
/// <c>FilterLevel</c> indexing convention of section 7.14.4 of the AV1 specification, where index 0
/// selects the vertical-edge level and index 1 selects the horizontal-edge level.
/// </summary>
internal enum Av1EdgeDirection
{
    Vertical = 0,
    Horizontal = 1,
}
