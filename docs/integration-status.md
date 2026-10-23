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
| Sqlite | 153 pass, 0 fail, 0 skip · 2026-10-18 10:13Z · 94b0adbe | 153 pass, 0 fail, 0 skip · 2026-10-23 14:28Z · af80775a+ |
| PostgreSql | 169 pass, 0 fail, 0 skip · 2026-10-22 10:11Z · 6d44c081+ | 169 pass, 0 fail, 0 skip · 2026-10-23 14:28Z · af80775a+ |
| SqlServer | 156 pass, 0 fail, 0 skip · 2026-10-22 10:10Z · 6d44c081+ | 156 pass, 0 fail, 0 skip · 2026-10-23 14:29Z · af80775a+ |
| MySql | 158 pass, 1 fail, 0 skip · 2026-10-23 00:09Z · 6d44c081+ | 159 pass, 0 fail, 0 skip · 2026-10-23 14:27Z · af80775a+ |
| MariaDb | 159 pass, 0 fail, 0 skip · 2026-10-23 00:09Z · 6d44c081+ | 159 pass, 0 fail, 0 skip · 2026-10-23 00:10Z · 6d44c081+ |
| Firebird | 152 pass, 0 fail, 0 skip · 2026-10-18 10:02Z · 94b0adbe | 152 pass, 0 fail, 0 skip · 2026-10-22 12:19Z · 6d44c081+ |
| CockroachDb | 165 pass, 0 fail, 0 skip · 2026-10-18 10:08Z · 94b0adbe | 165 pass, 0 fail, 0 skip · 2026-10-18 10:28Z · 94b0adbe |
| DuckDB | 152 pass, 0 fail, 0 skip · 2026-10-18 13:35Z · 4cc2c964 | 152 pass, 0 fail, 0 skip · 2026-10-22 12:15Z · 6d44c081+ |
| Oracle | 153 pass, 0 fail, 0 skip · 2026-10-22 10:07Z · 6d44c081+ | 153 pass, 0 fail, 0 skip · 2026-10-22 14:40Z · 6d44c081+ |
| YugabyteDb | 169 pass, 0 fail, 0 skip · 2026-10-18 10:04Z · 94b0adbe | 169 pass, 0 fail, 0 skip · 2026-10-18 10:24Z · 94b0adbe |
| TiDb | 159 pass, 0 fail, 0 skip · 2026-10-18 10:14Z · 94b0adbe | 159 pass, 0 fail, 0 skip · 2026-10-18 10:34Z · 94b0adbe |
| FlatFile | 152 pass, 0 fail, 0 skip · 2026-10-22 22:19Z · 6d44c081+ | 152 pass, 0 fail, 0 skip · 2026-10-22 22:19Z · 6d44c081+ |
| Db2 | 152 pass, 0 fail, 0 skip · 2026-10-18 09:58Z · 94b0adbe | 152 pass, 0 fail, 0 skip · 2026-10-18 10:19Z · 94b0adbe |
| Informix | 152 pass, 0 fail, 0 skip · 2026-10-18 10:14Z · 94b0adbe | 152 pass, 0 fail, 0 skip · 2026-10-22 11:31Z · 6d44c081+ |
| SybaseASE | 152 pass, 0 fail, 0 skip · 2026-10-18 10:09Z · 94b0adbe | 152 pass, 0 fail, 0 skip · 2026-10-18 10:29Z · 94b0adbe |
| Spanner | 152 pass, 0 fail, 0 skip · 2026-10-18 10:13Z · 94b0adbe | 152 pass, 0 fail, 0 skip · 2026-10-18 10:33Z · 94b0adbe |
| SingleStore | 159 pass, 0 fail, 0 skip · 2026-10-18 10:11Z · 94b0adbe | 159 pass, 0 fail, 0 skip · 2026-10-18 10:31Z · 94b0adbe |
| Snowflake | 146 pass, 0 fail, 0 skip · 2026-10-12 14:58Z · 0d031db1 | 146 pass, 0 fail, 0 skip · 2026-10-12 15:45Z · 0d031db1 |
| SapHana | 147 pass, 0 fail, 0 skip · 2026-10-17 11:16Z · 5f813c36+ | 147 pass, 0 fail, 0 skip · 2026-10-17 11:38Z · 5f813c36+ |
| InterBase | 147 pass, 0 fail, 0 skip · 2026-10-17 11:33Z · 5f813c36+ | 147 pass, 0 fail, 0 skip · 2026-10-17 11:55Z · 5f813c36+ |
| Access | 144 pass, 0 fail, 0 skip · 2026-10-12 19:05Z · e0137a99+ | 144 pass, 0 fail, 0 skip · 2026-10-12 19:07Z · e0137a99+ |
| AuroraPostgreSql | 144 pass, 0 fail, 0 skip · 2026-10-12 15:21Z · 0d031db1 | 144 pass, 0 fail, 0 skip · 2026-10-13 19:30Z · 6a127448+ |
| AuroraMySql | 144 pass, 0 fail, 0 skip · 2026-10-12 15:25Z · 0d031db1 | 144 pass, 0 fail, 0 skip · 2026-10-13 19:35Z · 6a127448+ |

Failing now: MySql net8.0.
