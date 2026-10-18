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
| Sqlite | 153 pass, 0 fail, 0 skip · 2026-10-18 10:13Z · 94b0adbe | 153 pass, 0 fail, 0 skip · 2026-10-18 10:34Z · 94b0adbe |
| PostgreSql | 169 pass, 0 fail, 0 skip · 2026-10-18 10:13Z · 94b0adbe | 169 pass, 0 fail, 0 skip · 2026-10-18 10:33Z · 94b0adbe |
| SqlServer | 156 pass, 0 fail, 0 skip · 2026-10-18 10:06Z · 94b0adbe | 156 pass, 0 fail, 0 skip · 2026-10-18 10:26Z · 94b0adbe |
| MySql | 159 pass, 0 fail, 0 skip · 2026-10-18 10:12Z · 94b0adbe | 159 pass, 0 fail, 0 skip · 2026-10-18 10:32Z · 94b0adbe |
| MariaDb | 159 pass, 0 fail, 0 skip · 2026-10-18 10:13Z · 94b0adbe | 159 pass, 0 fail, 0 skip · 2026-10-18 10:33Z · 94b0adbe |
| Firebird | 152 pass, 0 fail, 0 skip · 2026-10-18 10:02Z · 94b0adbe | 152 pass, 0 fail, 0 skip · 2026-10-18 10:22Z · 94b0adbe |
| CockroachDb | 165 pass, 0 fail, 0 skip · 2026-10-18 10:08Z · 94b0adbe | 165 pass, 0 fail, 0 skip · 2026-10-18 10:28Z · 94b0adbe |
| DuckDB | 152 pass, 0 fail, 0 skip · 2026-10-18 13:35Z · 4cc2c964 | 152 pass, 0 fail, 0 skip · 2026-10-18 13:36Z · 4cc2c964 |
| Oracle | 153 pass, 0 fail, 0 skip · 2026-10-18 10:10Z · 94b0adbe | 153 pass, 0 fail, 0 skip · 2026-10-18 10:30Z · 94b0adbe |
| YugabyteDb | 169 pass, 0 fail, 0 skip · 2026-10-18 10:04Z · 94b0adbe | 169 pass, 0 fail, 0 skip · 2026-10-18 10:24Z · 94b0adbe |
| TiDb | 159 pass, 0 fail, 0 skip · 2026-10-18 10:14Z · 94b0adbe | 159 pass, 0 fail, 0 skip · 2026-10-18 10:34Z · 94b0adbe |
| FlatFile | 152 pass, 0 fail, 0 skip · 2026-10-18 10:14Z · 94b0adbe | 152 pass, 0 fail, 0 skip · 2026-10-18 10:34Z · 94b0adbe |
| Db2 | 152 pass, 0 fail, 0 skip · 2026-10-18 09:58Z · 94b0adbe | 152 pass, 0 fail, 0 skip · 2026-10-18 10:19Z · 94b0adbe |
| Informix | 152 pass, 0 fail, 0 skip · 2026-10-18 10:14Z · 94b0adbe | 152 pass, 0 fail, 0 skip · 2026-10-18 10:34Z · 94b0adbe |
| SybaseASE | 152 pass, 0 fail, 0 skip · 2026-10-18 10:09Z · 94b0adbe | 152 pass, 0 fail, 0 skip · 2026-10-18 10:29Z · 94b0adbe |
| Spanner | 152 pass, 0 fail, 0 skip · 2026-10-18 10:13Z · 94b0adbe | 152 pass, 0 fail, 0 skip · 2026-10-18 10:33Z · 94b0adbe |
| SingleStore | 159 pass, 0 fail, 0 skip · 2026-10-18 10:11Z · 94b0adbe | 159 pass, 0 fail, 0 skip · 2026-10-18 10:31Z · 94b0adbe |
| Snowflake | 146 pass, 0 fail, 0 skip · 2026-10-12 14:58Z · 0d031db1 | 146 pass, 0 fail, 0 skip · 2026-10-12 15:45Z · 0d031db1 |
| SapHana | 147 pass, 0 fail, 0 skip · 2026-10-17 11:16Z · 5f813c36+ | 147 pass, 0 fail, 0 skip · 2026-10-17 11:38Z · 5f813c36+ |
| InterBase | 147 pass, 0 fail, 0 skip · 2026-10-17 11:33Z · 5f813c36+ | 147 pass, 0 fail, 0 skip · 2026-10-17 11:55Z · 5f813c36+ |
| Access | 144 pass, 0 fail, 0 skip · 2026-10-12 19:05Z · e0137a99+ | 144 pass, 0 fail, 0 skip · 2026-10-12 19:07Z · e0137a99+ |
| AuroraPostgreSql | 144 pass, 0 fail, 0 skip · 2026-10-12 15:21Z · 0d031db1 | 144 pass, 0 fail, 0 skip · 2026-10-13 19:30Z · 6a127448+ |
| AuroraMySql | 144 pass, 0 fail, 0 skip · 2026-10-12 15:25Z · 0d031db1 | 144 pass, 0 fail, 0 skip · 2026-10-13 19:35Z · 6a127448+ |

Failing now: none.
