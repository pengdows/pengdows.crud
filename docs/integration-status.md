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
| Sqlite | 153 pass, 0 fail, 0 skip · 2026-10-18 00:19Z · c6290bb5 | 153 pass, 0 fail, 0 skip · 2026-10-18 00:38Z · c6290bb5 |
| PostgreSql | 169 pass, 0 fail, 0 skip · 2026-10-18 00:18Z · c6290bb5 | 169 pass, 0 fail, 0 skip · 2026-10-18 00:38Z · c6290bb5 |
| SqlServer | 156 pass, 0 fail, 0 skip · 2026-10-18 00:11Z · c6290bb5 | 156 pass, 0 fail, 0 skip · 2026-10-18 00:31Z · c6290bb5 |
| MySql | 159 pass, 0 fail, 0 skip · 2026-10-18 00:17Z · c6290bb5 | 159 pass, 0 fail, 0 skip · 2026-10-18 00:37Z · c6290bb5 |
| MariaDb | 159 pass, 0 fail, 0 skip · 2026-10-18 00:18Z · c6290bb5 | 159 pass, 0 fail, 0 skip · 2026-10-18 00:37Z · c6290bb5 |
| Firebird | 152 pass, 0 fail, 0 skip · 2026-10-18 00:07Z · c6290bb5 | 152 pass, 0 fail, 0 skip · 2026-10-18 00:26Z · c6290bb5 |
| CockroachDb | 165 pass, 0 fail, 0 skip · 2026-10-18 00:13Z · c6290bb5 | 165 pass, 0 fail, 0 skip · 2026-10-18 00:32Z · c6290bb5 |
| DuckDB | 152 pass, 0 fail, 0 skip · 2026-10-18 00:17Z · c6290bb5 | 152 pass, 0 fail, 0 skip · 2026-10-18 00:36Z · c6290bb5 |
| Oracle | 153 pass, 0 fail, 0 skip · 2026-10-18 00:15Z · c6290bb5 | 153 pass, 0 fail, 0 skip · 2026-10-18 00:34Z · c6290bb5 |
| YugabyteDb | 169 pass, 0 fail, 0 skip · 2026-10-18 00:09Z · c6290bb5 | 169 pass, 0 fail, 0 skip · 2026-10-18 00:29Z · c6290bb5 |
| TiDb | 159 pass, 0 fail, 0 skip · 2026-10-18 00:19Z · c6290bb5 | 159 pass, 0 fail, 0 skip · 2026-10-18 00:38Z · c6290bb5 |
| FlatFile | 152 pass, 0 fail, 0 skip · 2026-10-18 00:19Z · c6290bb5 | 152 pass, 0 fail, 0 skip · 2026-10-18 00:39Z · c6290bb5 |
| Db2 | 152 pass, 0 fail, 0 skip · 2026-10-18 00:04Z · c6290bb5 | 152 pass, 0 fail, 0 skip · 2026-10-18 00:23Z · c6290bb5 |
| Informix | 152 pass, 0 fail, 0 skip · 2026-10-18 00:19Z · c6290bb5 | 152 pass, 0 fail, 0 skip · 2026-10-18 00:39Z · c6290bb5 |
| SybaseASE | 152 pass, 0 fail, 0 skip · 2026-10-18 00:14Z · c6290bb5 | 152 pass, 0 fail, 0 skip · 2026-10-18 00:33Z · c6290bb5 |
| Spanner | 152 pass, 0 fail, 0 skip · 2026-10-18 00:18Z · c6290bb5 | 152 pass, 0 fail, 0 skip · 2026-10-18 00:39Z · c6290bb5 |
| SingleStore | 159 pass, 0 fail, 0 skip · 2026-10-18 00:16Z · c6290bb5 | 159 pass, 0 fail, 0 skip · 2026-10-18 00:35Z · c6290bb5 |
| Snowflake | 146 pass, 0 fail, 0 skip · 2026-10-12 14:58Z · 0d031db1 | 146 pass, 0 fail, 0 skip · 2026-10-12 15:45Z · 0d031db1 |
| SapHana | 147 pass, 0 fail, 0 skip · 2026-10-17 11:16Z · 5f813c36+ | 147 pass, 0 fail, 0 skip · 2026-10-17 11:38Z · 5f813c36+ |
| InterBase | 147 pass, 0 fail, 0 skip · 2026-10-17 11:33Z · 5f813c36+ | 147 pass, 0 fail, 0 skip · 2026-10-17 11:55Z · 5f813c36+ |
| Access | 144 pass, 0 fail, 0 skip · 2026-10-12 19:05Z · e0137a99+ | 144 pass, 0 fail, 0 skip · 2026-10-12 19:07Z · e0137a99+ |
| AuroraPostgreSql | 144 pass, 0 fail, 0 skip · 2026-10-12 15:21Z · 0d031db1 | 144 pass, 0 fail, 0 skip · 2026-10-13 19:30Z · 6a127448+ |
| AuroraMySql | 144 pass, 0 fail, 0 skip · 2026-10-12 15:25Z · 0d031db1 | 144 pass, 0 fail, 0 skip · 2026-10-13 19:35Z · 6a127448+ |

Failing now: none.
