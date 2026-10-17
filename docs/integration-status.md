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
| Sqlite | 148 pass, 0 fail, 0 skip · 2026-10-17 16:40Z · 21d76903 | 148 pass, 0 fail, 0 skip · 2026-10-17 16:58Z · 21d76903 |
| PostgreSql | 164 pass, 0 fail, 0 skip · 2026-10-17 16:39Z · 21d76903 | 164 pass, 0 fail, 0 skip · 2026-10-17 16:59Z · 21d76903 |
| SqlServer | 151 pass, 0 fail, 0 skip · 2026-10-17 16:30Z · 21d76903 | 151 pass, 0 fail, 0 skip · 2026-10-17 16:52Z · 21d76903 |
| MySql | 154 pass, 0 fail, 0 skip · 2026-10-17 16:39Z · 21d76903 | 154 pass, 0 fail, 0 skip · 2026-10-17 16:58Z · 21d76903 |
| MariaDb | 154 pass, 0 fail, 0 skip · 2026-10-17 16:39Z · 21d76903 | 154 pass, 0 fail, 0 skip · 2026-10-17 16:58Z · 21d76903 |
| Firebird | 147 pass, 0 fail, 0 skip · 2026-10-17 16:26Z · 21d76903 | 147 pass, 0 fail, 0 skip · 2026-10-17 16:47Z · 21d76903 |
| CockroachDb | 160 pass, 0 fail, 0 skip · 2026-10-17 16:32Z · 21d76903 | 160 pass, 0 fail, 0 skip · 2026-10-17 16:53Z · 21d76903 |
| DuckDB | 147 pass, 0 fail, 0 skip · 2026-10-17 16:38Z · 21d76903 | 147 pass, 0 fail, 0 skip · 2026-10-17 16:57Z · 21d76903 |
| Oracle | 148 pass, 0 fail, 0 skip · 2026-10-17 16:39Z · 21d76903 | 148 pass, 0 fail, 0 skip · 2026-10-17 16:55Z · 21d76903 |
| YugabyteDb | 164 pass, 0 fail, 0 skip · 2026-10-17 16:28Z · 21d76903 | 164 pass, 0 fail, 0 skip · 2026-10-17 16:49Z · 21d76903 |
| TiDb | 154 pass, 0 fail, 0 skip · 2026-10-17 16:40Z · 21d76903 | 154 pass, 0 fail, 0 skip · 2026-10-17 16:59Z · 21d76903 |
| FlatFile | 147 pass, 0 fail, 0 skip · 2026-10-17 16:40Z · 21d76903 | 147 pass, 0 fail, 0 skip · 2026-10-17 16:59Z · 21d76903 |
| Db2 | 147 pass, 0 fail, 0 skip · 2026-10-17 16:23Z · 21d76903 | 147 pass, 0 fail, 0 skip · 2026-10-17 16:44Z · 21d76903 |
| Informix | 147 pass, 0 fail, 0 skip · 2026-10-17 16:40Z · 21d76903 | 147 pass, 0 fail, 0 skip · 2026-10-17 16:59Z · 21d76903 |
| SybaseASE | 147 pass, 0 fail, 0 skip · 2026-10-17 16:33Z · 21d76903 | 147 pass, 0 fail, 0 skip · 2026-10-17 16:54Z · 21d76903 |
| Spanner | 147 pass, 0 fail, 0 skip · 2026-10-17 16:37Z · 21d76903 | 147 pass, 0 fail, 0 skip · 2026-10-17 16:58Z · 21d76903 |
| SingleStore | 154 pass, 0 fail, 0 skip · 2026-10-17 16:37Z · 21d76903 | 154 pass, 0 fail, 0 skip · 2026-10-17 16:56Z · 21d76903 |
| Snowflake | 146 pass, 0 fail, 0 skip · 2026-10-12 14:58Z · 0d031db1 | 146 pass, 0 fail, 0 skip · 2026-10-12 15:45Z · 0d031db1 |
| SapHana | 147 pass, 0 fail, 0 skip · 2026-10-17 11:16Z · 5f813c36+ | 147 pass, 0 fail, 0 skip · 2026-10-17 11:38Z · 5f813c36+ |
| InterBase | 147 pass, 0 fail, 0 skip · 2026-10-17 11:33Z · 5f813c36+ | 147 pass, 0 fail, 0 skip · 2026-10-17 11:55Z · 5f813c36+ |
| Access | 144 pass, 0 fail, 0 skip · 2026-10-12 19:05Z · e0137a99+ | 144 pass, 0 fail, 0 skip · 2026-10-12 19:07Z · e0137a99+ |
| AuroraPostgreSql | 144 pass, 0 fail, 0 skip · 2026-10-12 15:21Z · 0d031db1 | 144 pass, 0 fail, 0 skip · 2026-10-13 19:30Z · 6a127448+ |
| AuroraMySql | 144 pass, 0 fail, 0 skip · 2026-10-12 15:25Z · 0d031db1 | 144 pass, 0 fail, 0 skip · 2026-10-13 19:35Z · 6a127448+ |

Failing now: none.
