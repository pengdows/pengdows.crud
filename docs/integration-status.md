# Integration run status

When each database last passed `run-integration-tests.sh`, per framework, and against which commit.
Maintained by `tools/record-integration-run.py`, which folds the latest `TestResults/integration/summary-*.md`
files in right after a run; a database that was not run keeps its previous cell. A trailing `+` on a commit
means the working tree had uncommitted changes when it was recorded.

Cells read `pass, fail, skip · time of the run (UTC) · commit`. A skip is a capability skip (a test that does
not apply to that database); the counts are that database's own tests only.

Ready to release means every always-on row is green on the commit being released, and every opt-in database
that was run for the release is green. Access runs only on Windows.

| Database | net8.0 | net10.0 |
|---|---|---|
| Sqlite | not recorded | not recorded |
| PostgreSql | not recorded | not recorded |
| SqlServer | not recorded | not recorded |
| MySql | not recorded | not recorded |
| MariaDb | not recorded | not recorded |
| Firebird | not recorded | not recorded |
| CockroachDb | not recorded | not recorded |
| DuckDB | not recorded | not recorded |
| Oracle | not recorded | not recorded |
| YugabyteDb | not recorded | not recorded |
| TiDb | not recorded | not recorded |
| FlatFile | not recorded | not recorded |
| Db2 | not recorded | not recorded |
| Informix | not recorded | not recorded |
| SybaseASE | not recorded | not recorded |
| Spanner | not recorded | not recorded |
| SingleStore | not recorded | not recorded |
| Snowflake | 146 pass, 0 fail, 0 skip · 2026-10-12 10:32Z · e8d73d64 | 146 pass, 0 fail, 0 skip · 2026-10-12 10:49Z · e8d73d64 |
| SapHana | 146 pass, 0 fail, 0 skip · 2026-10-12 11:10Z · e8d73d64 | 146 pass, 0 fail, 0 skip · 2026-10-12 11:16Z · e8d73d64 |
| InterBase | 146 pass, 0 fail, 0 skip · 2026-10-12 11:04Z · e8d73d64 | 146 pass, 0 fail, 0 skip · 2026-10-12 11:05Z · e8d73d64 |

Opt-in, never recorded: Access.

Failing now: none.
