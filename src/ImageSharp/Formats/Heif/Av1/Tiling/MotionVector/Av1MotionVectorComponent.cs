// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Formats.Heif.Av1.Tiling.MotionVector;

/// <summary>
/// 5.11.32. <c>read_mv_component</c> argument selecting which axis of the
/// motion vector is being read. Numeric values match the spec's <c>comp</c>.
/// </summary>
internal enum Av1MotionVectorComponent
{
    Vertical = 0,
    Horizontal = 1,
}
