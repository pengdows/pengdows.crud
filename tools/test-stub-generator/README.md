# test-stub-generator

Writes a throwing implementation of any interface in `pengdows.crud.abstractions`, for the test doubles in
`pengdows.crud.Tests/TestDoubles/*.Generated.cs`. Moq is never used in this repository; a test that needs an implementer of a
library interface that is not the library's own class (to check a fallback or a defensive throw) uses one of these stubs, and a
hand-written partial class scripts the few members that test needs.

Every member throws `NotSupportedException("<class> does not script <member>")`. Default interface members keep their default
bodies.

Regenerate after an interface gains a member (the stub stops compiling):

```bash
dotnet run --project tools/test-stub-generator -c Release -- \
  pengdows.crud.abstractions/bin/Release/net8.0/pengdows.crud.abstractions.dll \
  pengdows.crud.IDatabaseContext ScriptedDatabaseContext \
  pengdows.crud.Tests/TestDoubles/ScriptedDatabaseContext.Generated.cs
# ITransactionContext skips the member its hand-written partial scripts:
#   pengdows.crud.ITransactionContext ScriptedTransactionContext .../ScriptedTransactionContext.Generated.cs CreateSqlContainer
# ISqlDialect: pengdows.crud.dialects.ISqlDialect ScriptedSqlDialect .../ScriptedSqlDialect.Generated.cs
```

Arguments: assembly path, interface full name, class name, output file, and an optional comma-separated list of members to leave
out (the ones a hand-written partial implements).
