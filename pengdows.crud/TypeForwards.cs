// =============================================================================
// FILE: TypeForwards.cs
// PURPOSE: Declares type forwarding from pengdows.crud to
//          pengdows.crud.abstractions for types that were moved.
//
// AI SUMMARY:
// - This file enables binary compatibility when types are moved between
//   assemblies (from crud to crud.abstractions).
// - Types forwarded:
//   * JsonPassThrough - Controls JSON column handling
//   * TimeMappingPolicy - Controls DateTime/DateTimeOffset mapping
// - The TypeForwardingAnchor class is an empty placeholder type with no
//   functionality.
// - Consumers referencing these types from pengdows.crud will automatically
//   be redirected to the abstractions assembly at runtime.
// =============================================================================

#region

using System.Runtime.CompilerServices;

#endregion

[assembly: TypeForwardedTo(typeof(pengdows.crud.JsonPassThrough))]
[assembly: TypeForwardedTo(typeof(pengdows.crud.TimeMappingPolicy))]

namespace pengdows.crud;

/// <summary>
/// Empty internal placeholder type declared alongside the type forwarding assembly attributes.
/// </summary>
/// <remarks>
/// This class has no functionality.
/// </remarks>
internal static class TypeForwardingAnchor
{
}