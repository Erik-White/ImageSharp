// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Formats.Heif.Av1.Tiling.MotionVector;

/// <summary>
/// 5.11.32. <c>read_mv_component</c> argument indicating which component of
/// the motion vector is being read. The numeric values follow the spec
/// convention (<c>comp = 0</c> for vertical, <c>comp = 1</c> for horizontal).
/// </summary>
internal enum Av1MotionVectorComponent
{
    Vertical = 0,
    Horizontal = 1,
}
