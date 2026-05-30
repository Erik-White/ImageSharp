// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Formats.Heif.Av1.Pipeline.Cdef;

/// <summary>
/// A single CDEF filter-tap offset from <c>Cdef_Directions</c> (spec 7.15.3), expressed as a
/// (row, column) delta. The caller multiplies <see cref="Dy"/> by the input stride.
/// </summary>
internal readonly record struct Av1CdefDirectionOffset(int Dy, int Dx);
