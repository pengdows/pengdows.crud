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
| Sqlite | 158 pass, 0 fail, 0 skip · 2026-11-25 23:35Z · b516c8d4+ | 158 pass, 0 fail, 0 skip · 2026-11-25 23:58Z · b516c8d4+ |
| PostgreSql | 191 pass, 0 fail, 0 skip · 2026-11-26 01:48Z · b516c8d4+ | 191 pass, 0 fail, 0 skip · 2026-11-26 01:49Z · b516c8d4+ |
| SqlServer | 161 pass, 0 fail, 0 skip · 2026-11-25 23:29Z · b516c8d4+ | 161 pass, 0 fail, 0 skip · 2026-11-25 23:52Z · b516c8d4+ |
| MySql | 169 pass, 0 fail, 0 skip · 2026-11-26 01:48Z · b516c8d4+ | 169 pass, 0 fail, 0 skip · 2026-11-26 01:49Z · b516c8d4+ |
| MariaDb | 169 pass, 0 fail, 0 skip · 2026-11-25 23:34Z · b516c8d4+ | 169 pass, 0 fail, 0 skip · 2026-11-25 23:57Z · b516c8d4+ |
| Firebird | 157 pass, 0 fail, 0 skip · 2026-11-25 23:25Z · b516c8d4+ | 157 pass, 0 fail, 0 skip · 2026-11-25 23:48Z · b516c8d4+ |
| CockroachDb | 187 pass, 0 fail, 0 skip · 2026-11-25 23:30Z · b516c8d4+ | 187 pass, 0 fail, 0 skip · 2026-11-25 23:53Z · b516c8d4+ |
| DuckDB | 157 pass, 0 fail, 0 skip · 2026-11-25 23:34Z · b516c8d4+ | 157 pass, 0 fail, 0 skip · 2026-11-25 23:56Z · b516c8d4+ |
| Oracle | 159 pass, 0 fail, 0 skip · 2026-11-25 23:32Z · b516c8d4+ | 159 pass, 0 fail, 0 skip · 2026-11-25 23:54Z · b516c8d4+ |
| YugabyteDb | 191 pass, 0 fail, 0 skip · 2026-11-25 23:28Z · b516c8d4+ | 191 pass, 0 fail, 0 skip · 2026-11-25 23:51Z · b516c8d4+ |
| TiDb | 169 pass, 0 fail, 0 skip · 2026-11-25 23:36Z · b516c8d4+ | 169 pass, 0 fail, 0 skip · 2026-11-25 23:58Z · b516c8d4+ |
| FlatFile | 157 pass, 0 fail, 0 skip · 2026-11-25 23:36Z · b516c8d4+ | 157 pass, 0 fail, 0 skip · 2026-11-25 23:59Z · b516c8d4+ |
| Db2 | 157 pass, 0 fail, 0 skip · 2026-11-25 23:23Z · b516c8d4+ | 157 pass, 0 fail, 0 skip · 2026-11-25 23:46Z · b516c8d4+ |
| Informix | 158 pass, 0 fail, 0 skip · 2026-11-25 23:36Z · b516c8d4+ | 158 pass, 0 fail, 0 skip · 2026-11-25 23:59Z · b516c8d4+ |
| SybaseASE | 157 pass, 0 fail, 0 skip · 2026-11-25 23:31Z · b516c8d4+ | 157 pass, 0 fail, 0 skip · 2026-11-25 23:54Z · b516c8d4+ |
| Spanner | 157 pass, 0 fail, 0 skip · 2026-11-25 23:40Z · b516c8d4+ | 157 pass, 0 fail, 0 skip · 2026-11-26 00:04Z · b516c8d4+ |
| SingleStore | 169 pass, 0 fail, 0 skip · 2026-11-25 23:32Z · b516c8d4+ | 169 pass, 0 fail, 0 skip · 2026-11-25 23:55Z · b516c8d4+ |
| Snowflake | 157 pass, 0 fail, 0 skip · 2026-11-25 20:06Z · c47cf68b+ | 157 pass, 0 fail, 0 skip · 2026-11-25 20:49Z · c47cf68b+ |
| SapHana | 157 pass, 0 fail, 0 skip · 2026-11-25 23:20Z · b516c8d4+ | 157 pass, 0 fail, 0 skip · 2026-11-25 23:43Z · b516c8d4+ |
| InterBase | 157 pass, 0 fail, 0 skip · 2026-11-26 00:23Z · b516c8d4+ | 157 pass, 0 fail, 0 skip · 2026-11-26 00:26Z · b516c8d4+ |
| Access | 157 pass, 0 fail, 0 skip · 2026-11-26 13:00Z · 72dfc910+ | 157 pass, 0 fail, 0 skip · 2026-11-26 13:02Z · 72dfc910+ |
| AuroraPostgreSql | 192 pass, 0 fail, 0 skip · 2026-11-26 02:12Z · b516c8d4+ | 192 pass, 0 fail, 0 skip · 2026-11-26 02:28Z · b516c8d4+ |
| AuroraMySql | 170 pass, 0 fail, 0 skip · 2026-11-26 02:17Z · b516c8d4+ | 170 pass, 0 fail, 0 skip · 2026-11-26 02:32Z · b516c8d4+ |

Failing now: none.
