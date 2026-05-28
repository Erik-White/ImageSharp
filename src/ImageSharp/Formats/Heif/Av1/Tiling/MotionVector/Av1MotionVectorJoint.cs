// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Formats.Heif.Av1.Tiling.MotionVector;

/// <summary>
/// 5.11.31. <c>mv_joint</c> values, indicating which axes of the motion vector
/// are non-zero. Numeric values match the spec.
/// </summary>
internal enum Av1MotionVectorJoint
{
    Zero = 0,
    HorizontalNonZero = 1,
    VerticalNonZero = 2,
    HorizontalAndVerticalNonZero = 3,
}
