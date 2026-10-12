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
| Sqlite | 147 pass, 0 fail, 0 skip · 2026-10-12 11:38Z · dcfcd1c1+ | 147 pass, 0 fail, 0 skip · 2026-10-12 11:58Z · dcfcd1c1+ |
| PostgreSql | 163 pass, 0 fail, 0 skip · 2026-10-12 11:38Z · dcfcd1c1+ | 163 pass, 0 fail, 0 skip · 2026-10-12 11:58Z · dcfcd1c1+ |
| SqlServer | 150 pass, 0 fail, 0 skip · 2026-10-12 14:25Z · dcfcd1c1+ | 150 pass, 0 fail, 0 skip · 2026-10-12 14:27Z · dcfcd1c1+ |
| MySql | 152 pass, 0 fail, 0 skip · 2026-10-12 11:37Z · dcfcd1c1+ | 152 pass, 0 fail, 0 skip · 2026-10-12 11:58Z · dcfcd1c1+ |
| MariaDb | 151 pass, 0 fail, 0 skip · 2026-10-12 11:37Z · dcfcd1c1+ | 151 pass, 0 fail, 0 skip · 2026-10-12 11:58Z · dcfcd1c1+ |
| Firebird | 146 pass, 0 fail, 0 skip · 2026-10-12 11:27Z · dcfcd1c1+ | 146 pass, 0 fail, 0 skip · 2026-10-12 11:46Z · dcfcd1c1+ |
| CockroachDb | 157 pass, 0 fail, 0 skip · 2026-10-12 11:33Z · dcfcd1c1+ | 157 pass, 0 fail, 0 skip · 2026-10-12 11:54Z · dcfcd1c1+ |
| DuckDB | 146 pass, 0 fail, 0 skip · 2026-10-12 11:36Z · dcfcd1c1+ | 146 pass, 0 fail, 0 skip · 2026-10-12 11:57Z · dcfcd1c1+ |
| Oracle | 147 pass, 0 fail, 0 skip · 2026-10-12 11:35Z · dcfcd1c1+ | 147 pass, 0 fail, 0 skip · 2026-10-12 11:56Z · dcfcd1c1+ |
| YugabyteDb | 161 pass, 0 fail, 0 skip · 2026-10-12 11:29Z · dcfcd1c1+ | 161 pass, 0 fail, 0 skip · 2026-10-12 11:48Z · dcfcd1c1+ |
| TiDb | 151 pass, 0 fail, 0 skip · 2026-10-12 11:38Z · dcfcd1c1+ | 151 pass, 0 fail, 0 skip · 2026-10-12 11:58Z · dcfcd1c1+ |
| FlatFile | 146 pass, 0 fail, 0 skip · 2026-10-12 11:39Z · dcfcd1c1+ | 146 pass, 0 fail, 0 skip · 2026-10-12 11:59Z · dcfcd1c1+ |
| Db2 | 146 pass, 0 fail, 0 skip · 2026-10-12 11:24Z · dcfcd1c1+ | 146 pass, 0 fail, 0 skip · 2026-10-12 11:43Z · dcfcd1c1+ |
| Informix | 146 pass, 0 fail, 0 skip · 2026-10-12 11:38Z · dcfcd1c1+ | 146 pass, 0 fail, 0 skip · 2026-10-12 11:58Z · dcfcd1c1+ |
| SybaseASE | 146 pass, 0 fail, 0 skip · 2026-10-12 11:34Z · dcfcd1c1+ | 146 pass, 0 fail, 0 skip · 2026-10-12 11:55Z · dcfcd1c1+ |
| Spanner | 146 pass, 0 fail, 0 skip · 2026-10-12 11:38Z · dcfcd1c1+ | 146 pass, 0 fail, 0 skip · 2026-10-12 11:56Z · dcfcd1c1+ |
| SingleStore | 148 pass, 0 fail, 0 skip · 2026-10-12 11:35Z · dcfcd1c1+ | 148 pass, 0 fail, 0 skip · 2026-10-12 11:57Z · dcfcd1c1+ |
| Snowflake | 146 pass, 0 fail, 0 skip · 2026-10-12 11:38Z · dcfcd1c1+ | 146 pass, 0 fail, 0 skip · 2026-10-12 10:49Z · e8d73d64 |
| SapHana | 146 pass, 0 fail, 0 skip · 2026-10-12 11:10Z · e8d73d64 | 146 pass, 0 fail, 0 skip · 2026-10-12 11:16Z · e8d73d64 |
| InterBase | 146 pass, 0 fail, 0 skip · 2026-10-12 11:04Z · e8d73d64 | 146 pass, 0 fail, 0 skip · 2026-10-12 11:05Z · e8d73d64 |
| AuroraPostgreSql | 144 pass, 0 fail, 0 skip · 2026-10-12 13:50Z · dcfcd1c1+ | 144 pass, 0 fail, 0 skip · 2026-10-12 14:03Z · dcfcd1c1+ |
| AuroraMySql | 144 pass, 0 fail, 0 skip · 2026-10-12 13:55Z · dcfcd1c1+ | 144 pass, 0 fail, 0 skip · 2026-10-12 14:08Z · dcfcd1c1+ |

Opt-in, never recorded: Access.

Failing now: none.
