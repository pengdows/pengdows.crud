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
| Sqlite | 147 pass, 0 fail, 0 skip · 2026-10-12 15:11Z · 0d031db1 | 147 pass, 0 fail, 0 skip · 2026-10-12 15:57Z · 0d031db1 |
| PostgreSql | 163 pass, 0 fail, 0 skip · 2026-10-12 15:10Z · 0d031db1 | 163 pass, 0 fail, 0 skip · 2026-10-12 15:57Z · 0d031db1 |
| SqlServer | 150 pass, 0 fail, 0 skip · 2026-10-12 15:07Z · 0d031db1 | 150 pass, 0 fail, 0 skip · 2026-10-12 15:54Z · 0d031db1 |
| MySql | 152 pass, 0 fail, 0 skip · 2026-10-12 15:09Z · 0d031db1 | 152 pass, 0 fail, 0 skip · 2026-10-12 15:56Z · 0d031db1 |
| MariaDb | 151 pass, 0 fail, 0 skip · 2026-10-12 15:10Z · 0d031db1 | 151 pass, 0 fail, 0 skip · 2026-10-12 15:56Z · 0d031db1 |
| Firebird | 146 pass, 0 fail, 0 skip · 2026-10-12 15:05Z · 0d031db1 | 146 pass, 0 fail, 0 skip · 2026-10-12 15:52Z · 0d031db1 |
| CockroachDb | 157 pass, 0 fail, 0 skip · 2026-10-12 15:06Z · 0d031db1 | 157 pass, 0 fail, 0 skip · 2026-10-12 15:54Z · 0d031db1 |
| DuckDB | 146 pass, 0 fail, 0 skip · 2026-10-12 15:09Z · 0d031db1 | 146 pass, 0 fail, 0 skip · 2026-10-12 15:56Z · 0d031db1 |
| Oracle | 147 pass, 0 fail, 0 skip · 2026-10-12 16:27Z · 0d031db1 | 147 pass, 0 fail, 0 skip · 2026-10-12 16:29Z · 0d031db1 |
| YugabyteDb | 161 pass, 0 fail, 0 skip · 2026-10-12 15:05Z · 0d031db1 | 161 pass, 0 fail, 0 skip · 2026-10-12 15:52Z · 0d031db1 |
| TiDb | 151 pass, 0 fail, 0 skip · 2026-10-12 15:11Z · 0d031db1 | 151 pass, 0 fail, 0 skip · 2026-10-12 15:57Z · 0d031db1 |
| FlatFile | 146 pass, 0 fail, 0 skip · 2026-10-12 15:21Z · 0d031db1 | 146 pass, 0 fail, 0 skip · 2026-10-12 16:06Z · 0d031db1 |
| Db2 | 146 pass, 0 fail, 0 skip · 2026-10-12 15:02Z · 0d031db1 | 146 pass, 0 fail, 0 skip · 2026-10-12 15:50Z · 0d031db1 |
| Informix | 146 pass, 0 fail, 0 skip · 2026-10-12 15:12Z · 0d031db1 | 146 pass, 0 fail, 0 skip · 2026-10-12 15:57Z · 0d031db1 |
| SybaseASE | 146 pass, 0 fail, 0 skip · 2026-10-12 15:07Z · 0d031db1 | 146 pass, 0 fail, 0 skip · 2026-10-12 15:54Z · 0d031db1 |
| Spanner | 146 pass, 0 fail, 0 skip · 2026-10-12 14:56Z · 0d031db1 | 146 pass, 0 fail, 0 skip · 2026-10-12 15:43Z · 0d031db1 |
| SingleStore | 148 pass, 0 fail, 0 skip · 2026-10-12 15:08Z · 0d031db1 | 148 pass, 0 fail, 0 skip · 2026-10-12 15:55Z · 0d031db1 |
| Snowflake | 146 pass, 0 fail, 0 skip · 2026-10-12 14:58Z · 0d031db1 | 146 pass, 0 fail, 0 skip · 2026-10-12 15:45Z · 0d031db1 |
| SapHana | 146 pass, 0 fail, 0 skip · 2026-10-12 15:02Z · 0d031db1 | 146 pass, 0 fail, 0 skip · 2026-10-12 15:49Z · 0d031db1 |
| InterBase | 146 pass, 0 fail, 0 skip · 2026-10-12 15:12Z · 0d031db1 | 146 pass, 0 fail, 0 skip · 2026-10-12 15:57Z · 0d031db1 |
| AuroraPostgreSql | 144 pass, 0 fail, 0 skip · 2026-10-12 15:21Z · 0d031db1 | 144 pass, 0 fail, 0 skip · 2026-10-12 16:06Z · 0d031db1 |
| AuroraMySql | 144 pass, 0 fail, 0 skip · 2026-10-12 15:25Z · 0d031db1 | 144 pass, 0 fail, 0 skip · 2026-10-12 16:11Z · 0d031db1 |

Opt-in, never recorded: Access.

Failing now: none.
