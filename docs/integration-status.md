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
| Sqlite | 153 pass, 0 fail, 0 skip · 2026-10-24 04:26Z · 43c1ea61 | 153 pass, 0 fail, 0 skip · 2026-10-24 05:00Z · 43c1ea61 |
| PostgreSql | 169 pass, 0 fail, 0 skip · 2026-10-24 04:26Z · 43c1ea61 | 169 pass, 0 fail, 0 skip · 2026-10-24 05:00Z · 43c1ea61 |
| SqlServer | 156 pass, 0 fail, 0 skip · 2026-10-24 04:16Z · 43c1ea61 | 156 pass, 0 fail, 0 skip · 2026-10-24 04:53Z · 43c1ea61 |
| MySql | 159 pass, 0 fail, 0 skip · 2026-10-24 04:25Z · 43c1ea61 | 159 pass, 0 fail, 0 skip · 2026-10-24 04:59Z · 43c1ea61 |
| MariaDb | 159 pass, 0 fail, 0 skip · 2026-10-24 04:25Z · 43c1ea61 | 159 pass, 0 fail, 0 skip · 2026-10-24 04:59Z · 43c1ea61 |
| Firebird | 152 pass, 0 fail, 0 skip · 2026-10-24 04:12Z · 43c1ea61 | 152 pass, 0 fail, 0 skip · 2026-10-24 04:48Z · 43c1ea61 |
| CockroachDb | 165 pass, 0 fail, 0 skip · 2026-10-24 04:18Z · 43c1ea61 | 165 pass, 0 fail, 0 skip · 2026-10-24 04:54Z · 43c1ea61 |
| DuckDB | 152 pass, 0 fail, 0 skip · 2026-10-24 04:24Z · 43c1ea61 | 152 pass, 0 fail, 0 skip · 2026-10-24 04:58Z · 43c1ea61 |
| Oracle | 154 pass, 0 fail, 0 skip · 2026-10-24 04:25Z · 43c1ea61 | 154 pass, 0 fail, 0 skip · 2026-10-24 04:56Z · 43c1ea61 |
| YugabyteDb | 169 pass, 0 fail, 0 skip · 2026-10-24 04:14Z · 43c1ea61 | 169 pass, 0 fail, 0 skip · 2026-10-24 04:50Z · 43c1ea61 |
| TiDb | 159 pass, 0 fail, 0 skip · 2026-10-24 04:26Z · 43c1ea61 | 159 pass, 0 fail, 0 skip · 2026-10-24 05:00Z · 43c1ea61 |
| FlatFile | 152 pass, 0 fail, 0 skip · 2026-10-24 04:36Z · 43c1ea61 | 152 pass, 0 fail, 0 skip · 2026-10-24 05:10Z · 43c1ea61 |
| Db2 | 152 pass, 0 fail, 0 skip · 2026-10-24 04:08Z · 43c1ea61 | 152 pass, 0 fail, 0 skip · 2026-10-24 04:45Z · 43c1ea61 |
| Informix | 152 pass, 0 fail, 0 skip · 2026-10-24 04:26Z · 43c1ea61 | 152 pass, 0 fail, 0 skip · 2026-10-24 05:00Z · 43c1ea61 |
| SybaseASE | 152 pass, 0 fail, 0 skip · 2026-10-24 04:19Z · 43c1ea61 | 152 pass, 0 fail, 0 skip · 2026-10-24 04:55Z · 43c1ea61 |
| Spanner | 152 pass, 0 fail, 0 skip · 2026-10-24 04:23Z · 43c1ea61 | 152 pass, 0 fail, 0 skip · 2026-10-24 04:59Z · 43c1ea61 |
| SingleStore | 159 pass, 0 fail, 0 skip · 2026-10-24 04:24Z · 43c1ea61 | 159 pass, 0 fail, 0 skip · 2026-10-24 04:57Z · 43c1ea61 |
| Snowflake | 146 pass, 0 fail, 0 skip · 2026-10-12 14:58Z · 0d031db1 | 146 pass, 0 fail, 0 skip · 2026-10-12 15:45Z · 0d031db1 |
| SapHana | 152 pass, 0 fail, 0 skip · 2026-10-24 10:30Z · 054eb79b | 152 pass, 0 fail, 0 skip · 2026-10-24 10:35Z · 054eb79b |
| InterBase | 152 pass, 0 fail, 0 skip · 2026-10-24 10:23Z · 43c1ea61+ | 152 pass, 0 fail, 0 skip · 2026-10-24 05:00Z · 43c1ea61 |
| Access | 144 pass, 0 fail, 0 skip · 2026-10-12 19:05Z · e0137a99+ | 144 pass, 0 fail, 0 skip · 2026-10-12 19:07Z · e0137a99+ |
| AuroraPostgreSql | 170 pass, 0 fail, 0 skip · 2026-10-24 04:36Z · 43c1ea61 | 170 pass, 0 fail, 0 skip · 2026-10-24 05:10Z · 43c1ea61 |
| AuroraMySql | 160 pass, 0 fail, 0 skip · 2026-10-24 04:40Z · 43c1ea61 | 160 pass, 0 fail, 0 skip · 2026-10-24 05:14Z · 43c1ea61 |

Failing now: none.
