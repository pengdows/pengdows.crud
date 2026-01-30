// =============================================================================
// FILE: TypeForwards.cs
// PURPOSE: Declares type forwarding from pengdows.crud to
//          pengdows.crud.abstractions for types that were moved.
//
// AI SUMMARY:
// - This file enables binary compatibility when types are moved between
//   assemblies (from crud to crud.abstractions).
// - Types forwarded:
//   * TypeCoercionOptions - Configuration for type coercion behavior
//   * JsonPassThrough - Controls JSON column handling
//   * TimeMappingPolicy - Controls DateTime/DateTimeOffset mapping
// - The TypeForwardingAnchor class is an empty anchor to satisfy namespace
//   requirements for the assembly-level attributes.
// - Consumers referencing these types from pengdows.crud will automatically
//   be redirected to the abstractions assembly at runtime.
// =============================================================================

#region

using System.Runtime.CompilerServices;

#endregion

[assembly: TypeForwardedTo(typeof(pengdows.crud.TypeCoercionOptions))]
[assembly: TypeForwardedTo(typeof(pengdows.crud.JsonPassThrough))]
[assembly: TypeForwardedTo(typeof(pengdows.crud.TimeMappingPolicy))]

namespace pengdows.crud;

/// <summary>
/// Internal anchor class required for type forwarding assembly attributes.
/// </summary>
/// <remarks>
/// This class has no functionality - it exists solely to satisfy the requirement
/// that assembly-level attributes must be in a namespace with at least one type.
/// </remarks>
internal static class TypeForwardingAnchor
{
}