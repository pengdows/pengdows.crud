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
| Sqlite | 158 pass, 0 fail, 0 skip · 2026-11-26 14:12Z · 3ec89de4 | 158 pass, 0 fail, 0 skip · 2026-11-26 14:34Z · 3ec89de4 |
| PostgreSql | 191 pass, 0 fail, 0 skip · 2026-11-26 14:12Z · 3ec89de4 | 191 pass, 0 fail, 0 skip · 2026-11-26 14:34Z · 3ec89de4 |
| SqlServer | 161 pass, 0 fail, 0 skip · 2026-11-26 14:05Z · 3ec89de4 | 161 pass, 0 fail, 0 skip · 2026-11-26 14:28Z · 3ec89de4 |
| MySql | 169 pass, 0 fail, 0 skip · 2026-11-26 14:11Z · 3ec89de4 | 169 pass, 0 fail, 0 skip · 2026-11-26 14:33Z · 3ec89de4 |
| MariaDb | 169 pass, 0 fail, 0 skip · 2026-11-26 14:11Z · 3ec89de4 | 169 pass, 0 fail, 0 skip · 2026-11-26 14:33Z · 3ec89de4 |
| Firebird | 157 pass, 0 fail, 0 skip · 2026-11-26 14:01Z · 3ec89de4 | 157 pass, 0 fail, 0 skip · 2026-11-26 14:24Z · 3ec89de4 |
| CockroachDb | 187 pass, 0 fail, 0 skip · 2026-11-26 14:06Z · 3ec89de4 | 187 pass, 0 fail, 0 skip · 2026-11-26 14:29Z · 3ec89de4 |
| DuckDB | 157 pass, 0 fail, 0 skip · 2026-11-26 14:10Z · 3ec89de4 | 157 pass, 0 fail, 0 skip · 2026-11-26 14:32Z · 3ec89de4 |
| Oracle | 159 pass, 0 fail, 0 skip · 2026-11-26 14:08Z · 3ec89de4 | 159 pass, 0 fail, 0 skip · 2026-11-26 14:30Z · 3ec89de4 |
| YugabyteDb | 191 pass, 0 fail, 0 skip · 2026-11-26 14:04Z · 3ec89de4 | 191 pass, 0 fail, 0 skip · 2026-11-26 14:26Z · 3ec89de4 |
| TiDb | 169 pass, 0 fail, 0 skip · 2026-11-26 14:12Z · 3ec89de4 | 169 pass, 0 fail, 0 skip · 2026-11-26 14:34Z · 3ec89de4 |
| FlatFile | 157 pass, 0 fail, 0 skip · 2026-11-26 14:13Z · 3ec89de4 | 157 pass, 0 fail, 0 skip · 2026-11-26 14:35Z · 3ec89de4 |
| Db2 | 157 pass, 0 fail, 0 skip · 2026-11-26 13:59Z · 3ec89de4 | 157 pass, 0 fail, 0 skip · 2026-11-26 14:22Z · 3ec89de4 |
| Informix | 158 pass, 0 fail, 0 skip · 2026-11-26 14:13Z · 3ec89de4 | 158 pass, 0 fail, 0 skip · 2026-11-26 14:35Z · 3ec89de4 |
| SybaseASE | 157 pass, 0 fail, 0 skip · 2026-11-26 14:07Z · 3ec89de4 | 157 pass, 0 fail, 0 skip · 2026-11-26 14:29Z · 3ec89de4 |
| Spanner | 157 pass, 0 fail, 0 skip · 2026-11-26 14:16Z · 3ec89de4 | 157 pass, 0 fail, 0 skip · 2026-11-26 14:39Z · 3ec89de4 |
| SingleStore | 169 pass, 0 fail, 0 skip · 2026-11-26 14:09Z · 3ec89de4 | 169 pass, 0 fail, 0 skip · 2026-11-26 14:31Z · 3ec89de4 |
| Snowflake | 157 pass, 0 fail, 0 skip · 2026-11-26 16:20Z · 3ec89de4+ | 157 pass, 0 fail, 0 skip · 2026-11-26 16:23Z · 3ec89de4+ |
| SapHana | 157 pass, 0 fail, 0 skip · 2026-11-26 13:56Z · 3ec89de4 | 157 pass, 0 fail, 0 skip · 2026-11-26 14:19Z · 3ec89de4 |
| InterBase | 157 pass, 0 fail, 0 skip · 2026-11-26 14:59Z · 3ec89de4+ | 157 pass, 0 fail, 0 skip · 2026-11-26 15:02Z · 3ec89de4+ |
| Access | 157 pass, 0 fail, 0 skip · 2026-11-26 13:00Z · 72dfc910+ | 157 pass, 0 fail, 0 skip · 2026-11-26 13:02Z · 72dfc910+ |
| AuroraPostgreSql | 192 pass, 0 fail, 0 skip · 2026-11-26 15:20Z · 3ec89de4+ | 192 pass, 0 fail, 0 skip · 2026-11-26 15:35Z · 3ec89de4+ |
| AuroraMySql | 170 pass, 0 fail, 0 skip · 2026-11-26 15:24Z · 3ec89de4+ | 170 pass, 0 fail, 0 skip · 2026-11-26 15:38Z · 3ec89de4+ |

Failing now: none.
