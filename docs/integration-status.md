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
| Sqlite | 153 pass, 0 fail, 0 skip · 2026-10-30 16:46Z · e07bd260+ | 153 pass, 0 fail, 0 skip · 2026-10-30 17:13Z · e07bd260+ |
| PostgreSql | 169 pass, 0 fail, 0 skip · 2026-10-30 16:46Z · e07bd260+ | 169 pass, 0 fail, 0 skip · 2026-10-30 17:13Z · e07bd260+ |
| SqlServer | 156 pass, 0 fail, 0 skip · 2026-10-30 16:42Z · e07bd260+ | 156 pass, 0 fail, 0 skip · 2026-10-30 17:09Z · e07bd260+ |
| MySql | 159 pass, 0 fail, 0 skip · 2026-10-30 16:46Z · e07bd260+ | 159 pass, 0 fail, 0 skip · 2026-10-30 17:12Z · e07bd260+ |
| MariaDb | 159 pass, 0 fail, 0 skip · 2026-10-30 16:46Z · e07bd260+ | 159 pass, 0 fail, 0 skip · 2026-10-30 17:13Z · e07bd260+ |
| Firebird | 152 pass, 0 fail, 0 skip · 2026-10-30 16:37Z · e07bd260+ | 152 pass, 0 fail, 0 skip · 2026-10-30 17:04Z · e07bd260+ |
| CockroachDb | 165 pass, 0 fail, 0 skip · 2026-10-30 16:44Z · e07bd260+ | 165 pass, 0 fail, 0 skip · 2026-10-30 17:10Z · e07bd260+ |
| DuckDB | 152 pass, 0 fail, 0 skip · 2026-10-30 16:46Z · e07bd260+ | 152 pass, 0 fail, 0 skip · 2026-10-30 17:12Z · e07bd260+ |
| Oracle | 154 pass, 0 fail, 0 skip · 2026-10-30 16:45Z · e07bd260+ | 154 pass, 0 fail, 0 skip · 2026-10-30 17:11Z · e07bd260+ |
| YugabyteDb | 168 pass, 1 fail, 0 skip · 2026-10-30 16:40Z · e07bd260+ | 169 pass, 0 fail, 0 skip · 2026-10-30 18:03Z · e07bd260+ |
| TiDb | 159 pass, 0 fail, 0 skip · 2026-10-30 16:47Z · e07bd260+ | 159 pass, 0 fail, 0 skip · 2026-10-30 17:13Z · e07bd260+ |
| FlatFile | 152 pass, 0 fail, 0 skip · 2026-10-30 16:47Z · e07bd260+ | 152 pass, 0 fail, 0 skip · 2026-10-30 17:14Z · e07bd260+ |
| Db2 | 152 pass, 0 fail, 0 skip · 2026-10-30 16:34Z · e07bd260+ | 152 pass, 0 fail, 0 skip · 2026-10-30 17:01Z · e07bd260+ |
| Informix | 152 pass, 0 fail, 0 skip · 2026-10-30 16:47Z · e07bd260+ | 152 pass, 0 fail, 0 skip · 2026-10-30 17:13Z · e07bd260+ |
| SybaseASE | 152 pass, 0 fail, 0 skip · 2026-10-30 16:44Z · e07bd260+ | 152 pass, 0 fail, 0 skip · 2026-10-30 17:10Z · e07bd260+ |
| Spanner | 152 pass, 0 fail, 0 skip · 2026-10-30 16:43Z · e07bd260+ | 152 pass, 0 fail, 0 skip · 2026-10-30 17:09Z · e07bd260+ |
| SingleStore | 159 pass, 0 fail, 0 skip · 2026-10-30 16:45Z · e07bd260+ | 159 pass, 0 fail, 0 skip · 2026-10-30 17:11Z · e07bd260+ |
| Snowflake | 146 pass, 0 fail, 0 skip · 2026-10-12 14:58Z · 0d031db1 | 146 pass, 0 fail, 0 skip · 2026-10-12 15:45Z · 0d031db1 |
| SapHana | 152 pass, 0 fail, 0 skip · 2026-10-30 16:29Z · e07bd260+ | 152 pass, 0 fail, 0 skip · 2026-10-30 16:56Z · e07bd260+ |
| InterBase | 152 pass, 0 fail, 0 skip · 2026-10-30 23:34Z · e07bd260+ | 152 pass, 0 fail, 0 skip · 2026-10-30 23:34Z · e07bd260+ |
| Access | 150 pass, 0 fail, 0 skip · 2026-10-24 11:00Z · 0bcea2fa | 150 pass, 0 fail, 0 skip · 2026-10-24 11:02Z · 0bcea2fa |
| AuroraPostgreSql | 170 pass, 0 fail, 0 skip · 2026-10-24 04:36Z · 43c1ea61 | 170 pass, 0 fail, 0 skip · 2026-10-24 05:10Z · 43c1ea61 |
| AuroraMySql | 160 pass, 0 fail, 0 skip · 2026-10-24 04:40Z · 43c1ea61 | 160 pass, 0 fail, 0 skip · 2026-10-24 05:14Z · 43c1ea61 |

Failing now: YugabyteDb net8.0.
