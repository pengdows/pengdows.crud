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
| Sqlite | 153 pass, 0 fail, 0 skip · 2026-10-23 16:36Z · 33755cb4 | 153 pass, 0 fail, 0 skip · 2026-10-23 16:56Z · 33755cb4 |
| PostgreSql | 169 pass, 0 fail, 0 skip · 2026-10-23 16:36Z · 33755cb4 | 169 pass, 0 fail, 0 skip · 2026-10-23 16:56Z · 33755cb4 |
| SqlServer | 156 pass, 0 fail, 0 skip · 2026-10-23 16:29Z · 33755cb4 | 156 pass, 0 fail, 0 skip · 2026-10-23 16:49Z · 33755cb4 |
| MySql | 159 pass, 0 fail, 0 skip · 2026-10-23 16:35Z · 33755cb4 | 159 pass, 0 fail, 0 skip · 2026-10-23 16:54Z · 33755cb4 |
| MariaDb | 159 pass, 0 fail, 0 skip · 2026-10-23 16:35Z · 33755cb4 | 159 pass, 0 fail, 0 skip · 2026-10-23 16:55Z · 33755cb4 |
| Firebird | 152 pass, 0 fail, 0 skip · 2026-10-23 16:24Z · 33755cb4 | 152 pass, 0 fail, 0 skip · 2026-10-23 16:44Z · 33755cb4 |
| CockroachDb | 165 pass, 0 fail, 0 skip · 2026-10-23 16:30Z · 33755cb4 | 165 pass, 0 fail, 0 skip · 2026-10-23 16:50Z · 33755cb4 |
| DuckDB | 152 pass, 0 fail, 0 skip · 2026-10-23 16:34Z · 33755cb4 | 152 pass, 0 fail, 0 skip · 2026-10-23 16:54Z · 33755cb4 |
| Oracle | 154 pass, 0 fail, 0 skip · 2026-10-23 16:32Z · 33755cb4 | 154 pass, 0 fail, 0 skip · 2026-10-23 16:52Z · 33755cb4 |
| YugabyteDb | 169 pass, 0 fail, 0 skip · 2026-10-23 16:26Z · 33755cb4 | 169 pass, 0 fail, 0 skip · 2026-10-23 16:46Z · 33755cb4 |
| TiDb | 159 pass, 0 fail, 0 skip · 2026-10-23 16:36Z · 33755cb4 | 159 pass, 0 fail, 0 skip · 2026-10-23 16:56Z · 33755cb4 |
| FlatFile | 152 pass, 0 fail, 0 skip · 2026-10-23 16:36Z · 33755cb4 | 152 pass, 0 fail, 0 skip · 2026-10-23 16:56Z · 33755cb4 |
| Db2 | 152 pass, 0 fail, 0 skip · 2026-10-23 16:21Z · 33755cb4 | 152 pass, 0 fail, 0 skip · 2026-10-23 16:41Z · 33755cb4 |
| Informix | 152 pass, 0 fail, 0 skip · 2026-10-23 16:36Z · 33755cb4 | 152 pass, 0 fail, 0 skip · 2026-10-23 16:56Z · 33755cb4 |
| SybaseASE | 152 pass, 0 fail, 0 skip · 2026-10-23 16:31Z · 33755cb4 | 152 pass, 0 fail, 0 skip · 2026-10-23 16:51Z · 33755cb4 |
| Spanner | 152 pass, 0 fail, 0 skip · 2026-10-23 16:35Z · 33755cb4 | 152 pass, 0 fail, 0 skip · 2026-10-23 16:56Z · 33755cb4 |
| SingleStore | 159 pass, 0 fail, 0 skip · 2026-10-23 16:33Z · 33755cb4 | 159 pass, 0 fail, 0 skip · 2026-10-23 16:53Z · 33755cb4 |
| Snowflake | 146 pass, 0 fail, 0 skip · 2026-10-12 14:58Z · 0d031db1 | 146 pass, 0 fail, 0 skip · 2026-10-12 15:45Z · 0d031db1 |
| SapHana | 152 pass, 0 fail, 0 skip · 2026-10-23 17:52Z · 33755cb4+ | 152 pass, 0 fail, 0 skip · 2026-10-23 17:58Z · 33755cb4+ |
| InterBase | 152 pass, 0 fail, 0 skip · 2026-10-23 17:47Z · 33755cb4+ | 152 pass, 0 fail, 0 skip · 2026-10-23 17:53Z · 33755cb4+ |
| Access | 144 pass, 0 fail, 0 skip · 2026-10-12 19:05Z · e0137a99+ | 144 pass, 0 fail, 0 skip · 2026-10-12 19:07Z · e0137a99+ |
| AuroraPostgreSql | 170 pass, 0 fail, 0 skip · 2026-10-23 20:04Z · 9ad08fd9+ | 170 pass, 0 fail, 0 skip · 2026-10-23 20:14Z · 9ad08fd9+ |
| AuroraMySql | 160 pass, 0 fail, 0 skip · 2026-10-23 18:46Z · 30c6d8ba | 160 pass, 0 fail, 0 skip · 2026-10-23 18:59Z · 30c6d8ba |

Failing now: none.
