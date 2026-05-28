// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Formats.Heif.Av1.Tiling.MotionVector;

/// <summary>
/// 5.11.31. <c>mv_joint</c> values, indicating which components of the motion
/// vector are non-zero. The component order follows the spec: row first,
/// column second.
/// </summary>
internal enum Av1MotionVectorJoint
{
    /// <summary>Both components are zero.</summary>
    Zero = 0,

    /// <summary>Row component is zero, column is non-zero.</summary>
    HorizontalNonZero = 1,

    /// <summary>Row component is non-zero, column is zero.</summary>
    VerticalNonZero = 2,

    /// <summary>Both components are non-zero.</summary>
    HorizontalAndVerticalNonZero = 3,
}
