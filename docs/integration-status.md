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
| Sqlite | 158 pass, 0 fail, 0 skip · 2026-11-27 19:59Z · 28d5903a+ | 159 pass, 0 fail, 0 skip · 2026-11-28 12:24Z · 28d5903a+ |
| PostgreSql | 191 pass, 0 fail, 0 skip · 2026-11-27 19:59Z · 28d5903a+ | 192 pass, 0 fail, 0 skip · 2026-11-28 11:20Z · 28d5903a+ |
| SqlServer | 161 pass, 0 fail, 0 skip · 2026-11-27 19:54Z · 28d5903a+ | 162 pass, 0 fail, 0 skip · 2026-11-28 11:26Z · 28d5903a+ |
| MySql | 169 pass, 0 fail, 0 skip · 2026-11-27 19:58Z · 28d5903a+ | 170 pass, 0 fail, 0 skip · 2026-11-28 11:21Z · 28d5903a+ |
| MariaDb | 169 pass, 0 fail, 0 skip · 2026-11-27 19:58Z · 28d5903a+ | 170 pass, 0 fail, 0 skip · 2026-11-28 11:22Z · 28d5903a+ |
| Firebird | 157 pass, 0 fail, 0 skip · 2026-11-27 19:50Z · 28d5903a+ | 158 pass, 0 fail, 0 skip · 2026-11-28 11:36Z · 28d5903a+ |
| CockroachDb | 187 pass, 0 fail, 0 skip · 2026-11-27 19:55Z · 28d5903a+ | 188 pass, 0 fail, 0 skip · 2026-11-28 11:46Z · 28d5903a+ |
| DuckDB | 157 pass, 0 fail, 0 skip · 2026-11-27 19:57Z · 28d5903a+ | 158 pass, 0 fail, 0 skip · 2026-11-28 12:23Z · 28d5903a+ |
| Oracle | 159 pass, 0 fail, 0 skip · 2026-11-27 18:57Z · 28d5903a+ | 160 pass, 0 fail, 0 skip · 2026-11-28 01:26Z · 28d5903a+ |
| YugabyteDb | 191 pass, 0 fail, 0 skip · 2026-11-27 19:52Z · 28d5903a+ | 192 pass, 0 fail, 0 skip · 2026-11-28 11:50Z · 28d5903a+ |
| TiDb | 169 pass, 0 fail, 0 skip · 2026-11-27 19:59Z · 28d5903a+ | 170 pass, 0 fail, 0 skip · 2026-11-28 11:23Z · 28d5903a+ |
| FlatFile | 157 pass, 0 fail, 0 skip · 2026-11-27 20:03Z · 28d5903a+ | 157 pass, 0 fail, 0 skip · 2026-11-27 20:26Z · 28d5903a+ |
| Db2 | 157 pass, 0 fail, 0 skip · 2026-11-27 19:47Z · 28d5903a+ | 158 pass, 0 fail, 0 skip · 2026-11-28 11:41Z · 28d5903a+ |
| Informix | 158 pass, 0 fail, 0 skip · 2026-11-27 20:00Z · 28d5903a+ | 159 pass, 0 fail, 0 skip · 2026-11-28 12:50Z · 28d5903a+ |
| SybaseASE | 157 pass, 0 fail, 0 skip · 2026-11-27 19:55Z · 28d5903a+ | 158 pass, 0 fail, 0 skip · 2026-11-28 12:58Z · 28d5903a+ |
| Spanner | 157 pass, 0 fail, 0 skip · 2026-11-27 20:07Z · 28d5903a+ | 158 pass, 0 fail, 0 skip · 2026-11-28 12:19Z · 28d5903a+ |
| SingleStore | 169 pass, 0 fail, 0 skip · 2026-11-27 19:56Z · 28d5903a+ | 170 pass, 0 fail, 0 skip · 2026-11-28 11:24Z · 28d5903a+ |
| Snowflake | 157 pass, 0 fail, 0 skip · 2026-11-27 22:35Z · 28d5903a+ | 93 pass, 0 fail, 0 skip · 2026-11-28 11:19Z · 28d5903a+ |
| SapHana | 157 pass, 0 fail, 0 skip · 2026-11-27 19:22Z · 28d5903a+ | 158 pass, 0 fail, 0 skip · 2026-11-28 12:42Z · 28d5903a+ |
| InterBase | 157 pass, 0 fail, 0 skip · 2026-11-27 20:03Z · 28d5903a+ | 157 pass, 1 fail, 0 skip · 2026-11-28 13:02Z · 28d5903a+ |
| Access | 157 pass, 0 fail, 0 skip · 2026-11-26 13:00Z · 72dfc910+ | 157 pass, 0 fail, 0 skip · 2026-11-26 13:02Z · 72dfc910+ |
| AuroraPostgreSql | 192 pass, 0 fail, 0 skip · 2026-11-27 16:35Z · 28d5903a+ | 192 pass, 0 fail, 0 skip · 2026-11-27 16:51Z · 28d5903a+ |
| AuroraMySql | 170 pass, 0 fail, 0 skip · 2026-11-27 16:40Z · 28d5903a+ | 170 pass, 0 fail, 0 skip · 2026-11-27 16:55Z · 28d5903a+ |

Failing now: InterBase net10.0.
