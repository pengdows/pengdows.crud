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
| Sqlite | 158 pass, 0 fail, 0 skip · 2026-11-25 20:24Z · c47cf68b+ | 158 pass, 0 fail, 0 skip · 2026-11-25 20:59Z · c47cf68b+ |
| PostgreSql | 191 pass, 0 fail, 0 skip · 2026-11-25 20:24Z · c47cf68b+ | 191 pass, 0 fail, 0 skip · 2026-11-25 20:59Z · c47cf68b+ |
| SqlServer | 161 pass, 0 fail, 0 skip · 2026-11-25 20:22Z · c47cf68b+ | 161 pass, 0 fail, 0 skip · 2026-11-25 20:55Z · c47cf68b+ |
| MySql | 169 pass, 0 fail, 0 skip · 2026-11-25 20:23Z · c47cf68b+ | 169 pass, 0 fail, 0 skip · 2026-11-25 20:57Z · c47cf68b+ |
| MariaDb | 169 pass, 0 fail, 0 skip · 2026-11-25 20:23Z · c47cf68b+ | 169 pass, 0 fail, 0 skip · 2026-11-25 20:58Z · c47cf68b+ |
| Firebird | 157 pass, 0 fail, 0 skip · 2026-11-25 20:13Z · c47cf68b+ | 157 pass, 0 fail, 0 skip · 2026-11-25 20:54Z · c47cf68b+ |
| CockroachDb | 187 pass, 0 fail, 0 skip · 2026-11-25 20:16Z · c47cf68b+ | 187 pass, 0 fail, 0 skip · 2026-11-25 20:56Z · c47cf68b+ |
| DuckDB | 157 pass, 0 fail, 0 skip · 2026-11-25 20:22Z · c47cf68b+ | 157 pass, 0 fail, 0 skip · 2026-11-25 20:58Z · c47cf68b+ |
| Oracle | 159 pass, 0 fail, 0 skip · 2026-11-25 20:20Z · c47cf68b+ | 159 pass, 0 fail, 0 skip · 2026-11-25 20:57Z · c47cf68b+ |
| YugabyteDb | 191 pass, 0 fail, 0 skip · 2026-11-25 20:15Z · c47cf68b+ | 191 pass, 0 fail, 0 skip · 2026-11-25 20:55Z · c47cf68b+ |
| TiDb | 169 pass, 0 fail, 0 skip · 2026-11-25 20:24Z · c47cf68b+ | 169 pass, 0 fail, 0 skip · 2026-11-25 20:59Z · c47cf68b+ |
| FlatFile | 157 pass, 0 fail, 0 skip · 2026-11-25 20:25Z · c47cf68b+ | 157 pass, 0 fail, 0 skip · 2026-11-25 20:59Z · c47cf68b+ |
| Db2 | 157 pass, 0 fail, 0 skip · 2026-11-25 20:12Z · c47cf68b+ | 157 pass, 0 fail, 0 skip · 2026-11-25 20:53Z · c47cf68b+ |
| Informix | 158 pass, 0 fail, 0 skip · 2026-11-25 20:25Z · c47cf68b+ | 158 pass, 0 fail, 0 skip · 2026-11-25 20:59Z · c47cf68b+ |
| SybaseASE | 157 pass, 0 fail, 0 skip · 2026-11-25 20:18Z · c47cf68b+ | 157 pass, 0 fail, 0 skip · 2026-11-25 20:56Z · c47cf68b+ |
| Spanner | 157 pass, 0 fail, 0 skip · 2026-11-25 20:11Z · c47cf68b+ | 157 pass, 0 fail, 0 skip · 2026-11-25 20:50Z · c47cf68b+ |
| SingleStore | 169 pass, 0 fail, 0 skip · 2026-11-25 20:21Z · c47cf68b+ | 169 pass, 0 fail, 0 skip · 2026-11-25 20:57Z · c47cf68b+ |
| Snowflake | 157 pass, 0 fail, 0 skip · 2026-11-25 20:06Z · c47cf68b+ | 157 pass, 0 fail, 0 skip · 2026-11-25 20:49Z · c47cf68b+ |
| SapHana | 157 pass, 0 fail, 0 skip · 2026-11-25 20:09Z · c47cf68b+ | 157 pass, 0 fail, 0 skip · 2026-11-25 20:52Z · c47cf68b+ |
| InterBase | 157 pass, 0 fail, 0 skip · 2026-11-25 20:27Z · c47cf68b+ | 157 pass, 0 fail, 0 skip · 2026-11-25 21:02Z · c47cf68b+ |
| Access | 156 pass, 0 fail, 0 skip · 2026-11-21 21:21Z · 83c33667+ | 156 pass, 0 fail, 0 skip · 2026-11-21 21:23Z · 83c33667+ |
| AuroraPostgreSql | 174 pass, 0 fail, 0 skip · 2026-11-13 16:22Z · e11ef215 | 174 pass, 0 fail, 0 skip · 2026-11-13 16:47Z · e11ef215 |
| AuroraMySql | 164 pass, 0 fail, 0 skip · 2026-11-13 16:12Z · e11ef215 | 164 pass, 0 fail, 0 skip · 2026-11-13 16:37Z · e11ef215 |

Failing now: none.
