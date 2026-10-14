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
| Sqlite | 147 pass, 0 fail, 0 skip · 2026-10-13 18:21Z · 9a1a3059 | 147 pass, 0 fail, 0 skip · 2026-10-13 19:21Z · 6a127448+ |
| PostgreSql | 163 pass, 0 fail, 0 skip · 2026-10-12 15:10Z · 0d031db1 | 163 pass, 0 fail, 0 skip · 2026-10-13 19:20Z · 6a127448+ |
| SqlServer | 150 pass, 0 fail, 0 skip · 2026-10-12 15:07Z · 0d031db1 | 150 pass, 0 fail, 0 skip · 2026-10-13 19:17Z · 6a127448+ |
| MySql | 152 pass, 0 fail, 0 skip · 2026-10-12 15:09Z · 0d031db1 | 152 pass, 0 fail, 0 skip · 2026-10-13 19:20Z · 6a127448+ |
| MariaDb | 151 pass, 0 fail, 0 skip · 2026-10-12 15:10Z · 0d031db1 | 151 pass, 0 fail, 0 skip · 2026-10-13 19:20Z · 6a127448+ |
| Firebird | 146 pass, 0 fail, 0 skip · 2026-10-12 15:05Z · 0d031db1 | 146 pass, 0 fail, 0 skip · 2026-10-13 19:12Z · 6a127448+ |
| CockroachDb | 157 pass, 0 fail, 0 skip · 2026-10-12 15:06Z · 0d031db1 | 157 pass, 0 fail, 0 skip · 2026-10-13 19:18Z · 6a127448+ |
| DuckDB | 146 pass, 0 fail, 0 skip · 2026-10-12 15:09Z · 0d031db1 | 146 pass, 0 fail, 0 skip · 2026-10-13 19:20Z · 6a127448+ |
| Oracle | 147 pass, 0 fail, 0 skip · 2026-10-12 16:27Z · 0d031db1 | 147 pass, 0 fail, 0 skip · 2026-10-13 19:19Z · 6a127448+ |
| YugabyteDb | 161 pass, 0 fail, 0 skip · 2026-10-12 15:05Z · 0d031db1 | 161 pass, 0 fail, 0 skip · 2026-10-13 19:14Z · 6a127448+ |
| TiDb | 151 pass, 0 fail, 0 skip · 2026-10-12 15:11Z · 0d031db1 | 151 pass, 0 fail, 0 skip · 2026-10-13 19:21Z · 6a127448+ |
| FlatFile | 146 pass, 0 fail, 0 skip · 2026-10-12 15:21Z · 0d031db1 | 146 pass, 0 fail, 0 skip · 2026-10-13 19:30Z · 6a127448+ |
| Db2 | 146 pass, 0 fail, 0 skip · 2026-10-12 15:02Z · 0d031db1 | 146 pass, 0 fail, 0 skip · 2026-10-13 19:09Z · 6a127448+ |
| Informix | 146 pass, 0 fail, 0 skip · 2026-10-12 15:12Z · 0d031db1 | 146 pass, 0 fail, 0 skip · 2026-10-13 19:21Z · 6a127448+ |
| SybaseASE | 146 pass, 0 fail, 0 skip · 2026-10-12 15:07Z · 0d031db1 | 146 pass, 0 fail, 0 skip · 2026-10-13 19:18Z · 6a127448+ |
| Spanner | 146 pass, 0 fail, 0 skip · 2026-10-12 14:56Z · 0d031db1 | 146 pass, 0 fail, 0 skip · 2026-10-13 19:17Z · 6a127448+ |
| SingleStore | 148 pass, 0 fail, 0 skip · 2026-10-12 15:08Z · 0d031db1 | 148 pass, 0 fail, 0 skip · 2026-10-13 19:19Z · 6a127448+ |
| Snowflake | 146 pass, 0 fail, 0 skip · 2026-10-12 14:58Z · 0d031db1 | 146 pass, 0 fail, 0 skip · 2026-10-12 15:45Z · 0d031db1 |
| SapHana | 146 pass, 0 fail, 0 skip · 2026-10-12 15:02Z · 0d031db1 | 146 pass, 0 fail, 0 skip · 2026-10-13 19:05Z · 6a127448+ |
| InterBase | 146 pass, 0 fail, 0 skip · 2026-10-12 15:12Z · 0d031db1 | 146 pass, 0 fail, 0 skip · 2026-10-13 19:21Z · 6a127448+ |
| Access | 144 pass, 0 fail, 0 skip · 2026-10-12 19:05Z · e0137a99+ | 144 pass, 0 fail, 0 skip · 2026-10-12 19:07Z · e0137a99+ |
| AuroraPostgreSql | 144 pass, 0 fail, 0 skip · 2026-10-12 15:21Z · 0d031db1 | 144 pass, 0 fail, 0 skip · 2026-10-13 19:30Z · 6a127448+ |
| AuroraMySql | 144 pass, 0 fail, 0 skip · 2026-10-12 15:25Z · 0d031db1 | 144 pass, 0 fail, 0 skip · 2026-10-13 19:35Z · 6a127448+ |

Failing now: none.
