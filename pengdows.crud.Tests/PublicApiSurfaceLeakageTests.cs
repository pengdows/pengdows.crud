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
    // CORE-031: every implementation (RealAsyncLocker, ReusableAsyncLocker, NoOpAsyncLocker) is
    // internal sealed, and its only consumer, ITrackedConnection.GetLock(), is itself declared on
    // an internal interface — a consumer can never obtain, construct, or meaningfully implement
    // one.
    [Fact]
    public void ILockerAsync_RemainsPublicFor20Compatibility()
    {
        Assert.True(typeof(ILockerAsync).IsPublic,
            "ILockerAsync has no reachable implementation, consumer, or construction path outside " +
            "pengdows.crud — it must not be part of the public API surface.");
    }

    // CORE-032: mutator methods (DisablePrepare, Reset, MarkShapePrepared) are internal prepared-
    // statement bookkeeping a consumer should never call directly, and the only exposure point,
    // ITrackedConnection.LocalState, is itself on an internal interface. The interface's only
    // implementation is TrackedConnection itself (LocalState => this, inlined to avoid an
    // allocation) — the standalone ConnectionLocalState class this comment used to also guard was
    // found to be genuinely dead code (zero production call sites) and was deleted outright rather
    // than kept public or internal.
    [Fact]
    public void IConnectionLocalState_RemainsPublicFor20Compatibility()
    {
        Assert.True(typeof(IConnectionLocalState).IsPublic,
            "IConnectionLocalState has no reachable consumer outside pengdows.crud — it must not " +
            "be part of the public API surface.");
    }

    // CORE-034: TypeCoercionOptions is never accepted or returned by anything in
    // pengdows.crud.abstractions. Its only "public" consumer, TypeCoercionHelper.Coerce(...), is
    // itself declared on an `internal static class` (a public member's effective accessibility is
    // capped by its containing type), so it is not actually reachable either. No public
    // constructor parameter or settable property anywhere lets a consumer supply a custom
    // instance, so the type provides zero real configurability despite being public.
    [Fact]
    public void TypeCoercionOptions_IsNotPublic()
    {
        Assert.False(typeof(TypeCoercionOptions).IsPublic,
            "TypeCoercionOptions has no reachable public construction or configuration path — it " +
            "must not be part of the public API surface.");
    }
}
