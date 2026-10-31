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
| Sqlite | 153 pass, 0 fail, 0 skip · 2026-10-31 20:02Z · 9250ee42+ | 153 pass, 0 fail, 0 skip · 2026-10-31 20:09Z · 9250ee42+ |
| PostgreSql | 169 pass, 0 fail, 0 skip · 2026-10-31 20:02Z · 9250ee42+ | 169 pass, 0 fail, 0 skip · 2026-10-31 20:08Z · 9250ee42+ |
| SqlServer | 156 pass, 0 fail, 0 skip · 2026-10-31 20:06Z · 9250ee42+ | 156 pass, 0 fail, 0 skip · 2026-10-31 20:09Z · 9250ee42+ |
| MySql | 159 pass, 0 fail, 0 skip · 2026-10-31 19:56Z · 9250ee42+ | 159 pass, 0 fail, 0 skip · 2026-10-31 20:07Z · 9250ee42+ |
| MariaDb | 159 pass, 0 fail, 0 skip · 2026-10-31 19:58Z · 9250ee42+ | 159 pass, 0 fail, 0 skip · 2026-10-31 20:07Z · 9250ee42+ |
| Firebird | 152 pass, 0 fail, 0 skip · 2026-10-31 15:30Z · 9250ee42+ | 152 pass, 0 fail, 0 skip · 2026-10-31 15:52Z · 9250ee42+ |
| CockroachDb | 165 pass, 0 fail, 0 skip · 2026-10-31 15:35Z · 9250ee42+ | 165 pass, 0 fail, 0 skip · 2026-10-31 15:57Z · 9250ee42+ |
| DuckDB | 152 pass, 0 fail, 0 skip · 2026-10-31 15:39Z · 9250ee42+ | 152 pass, 0 fail, 0 skip · 2026-10-31 16:00Z · 9250ee42+ |
| Oracle | 154 pass, 0 fail, 0 skip · 2026-10-31 15:38Z · 9250ee42+ | 154 pass, 0 fail, 0 skip · 2026-10-31 15:59Z · 9250ee42+ |
| YugabyteDb | 169 pass, 0 fail, 0 skip · 2026-10-31 19:11Z · 9250ee42+ | 169 pass, 0 fail, 0 skip · 2026-10-31 19:22Z · 9250ee42+ |
| TiDb | 159 pass, 0 fail, 0 skip · 2026-10-31 15:42Z · 9250ee42+ | 159 pass, 0 fail, 0 skip · 2026-10-31 16:02Z · 9250ee42+ |
| FlatFile | 152 pass, 0 fail, 0 skip · 2026-10-31 15:43Z · 9250ee42+ | 152 pass, 0 fail, 0 skip · 2026-10-31 16:03Z · 9250ee42+ |
| Db2 | 152 pass, 0 fail, 0 skip · 2026-10-31 15:26Z · 9250ee42+ | 152 pass, 0 fail, 0 skip · 2026-10-31 15:48Z · 9250ee42+ |
| Informix | 152 pass, 0 fail, 0 skip · 2026-10-31 15:42Z · 9250ee42+ | 152 pass, 0 fail, 0 skip · 2026-10-31 16:03Z · 9250ee42+ |
| SybaseASE | 152 pass, 0 fail, 0 skip · 2026-10-31 15:37Z · 9250ee42+ | 152 pass, 0 fail, 0 skip · 2026-10-31 15:58Z · 9250ee42+ |
| Spanner | 152 pass, 0 fail, 0 skip · 2026-10-31 15:41Z · 9250ee42+ | 152 pass, 0 fail, 0 skip · 2026-10-31 16:01Z · 9250ee42+ |
| SingleStore | 159 pass, 0 fail, 0 skip · 2026-10-31 19:16Z · 9250ee42+ | 159 pass, 0 fail, 0 skip · 2026-10-31 19:23Z · 9250ee42+ |
| Snowflake | 146 pass, 0 fail, 0 skip · 2026-10-12 14:58Z · 0d031db1 | 146 pass, 0 fail, 0 skip · 2026-10-12 15:45Z · 0d031db1 |
| SapHana | 152 pass, 0 fail, 0 skip · 2026-10-31 19:15Z · 9250ee42+ | 152 pass, 0 fail, 0 skip · 2026-10-31 19:25Z · 9250ee42+ |
| InterBase | 152 pass, 0 fail, 0 skip · 2026-10-31 19:33Z · 9250ee42+ | 152 pass, 0 fail, 0 skip · 2026-10-31 19:33Z · 9250ee42+ |
| Access | 150 pass, 0 fail, 0 skip · 2026-10-24 11:00Z · 0bcea2fa | 150 pass, 0 fail, 0 skip · 2026-10-24 11:02Z · 0bcea2fa |
| AuroraPostgreSql | 170 pass, 0 fail, 0 skip · 2026-10-24 04:36Z · 43c1ea61 | 170 pass, 0 fail, 0 skip · 2026-10-24 05:10Z · 43c1ea61 |
| AuroraMySql | 160 pass, 0 fail, 0 skip · 2026-10-24 04:40Z · 43c1ea61 | 160 pass, 0 fail, 0 skip · 2026-10-24 05:14Z · 43c1ea61 |

Failing now: none.
