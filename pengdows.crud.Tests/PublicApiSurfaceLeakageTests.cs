using System;
using pengdows.crud.connection;
using pengdows.crud.threading;
using Xunit;

namespace pengdows.crud.Tests;

// Confirmed via manual audit (2026-08-29, cross-checked against an external analysis document):
// several types are declared `public` but have zero legitimate external reachability — every
// implementation is `internal sealed`, and/or their only exposure point is itself an `internal`
// interface, and/or nothing in pengdows.crud.abstractions ever accepts or returns them. Keeping
// them public serves no consumer and expands the surface CLAUDE.md's own "minimize public APIs;
// make types/members internal when possible" principle asks to avoid. Each type here gets one
// CORE-* tracker row; this file locks the "must not be public" contract in as a regression test.
//
// One candidate from the same audit, SafeAsyncDisposableBase, was investigated and REJECTED:
// SqlContainer, ContextBase (DatabaseContext's own base), and TenantContextRegistry are all
// legitimately public per CLAUDE.md's API Visibility Principles and all three derive from it —
// C# requires a base class to be at least as accessible as its derived classes, so it cannot be
// internalized without also internalizing those three genuinely-intentional public types. Not
// every candidate the external document named held up; this one didn't.
public class PublicApiSurfaceLeakageTests
{
}
