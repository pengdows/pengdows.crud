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
| Sqlite | 155 pass, 0 fail, 0 skip · 2026-11-03 11:29Z · b8eccb7f+ | 155 pass, 0 fail, 0 skip · 2026-11-03 11:49Z · b8eccb7f+ |
| PostgreSql | 171 pass, 0 fail, 0 skip · 2026-11-03 11:29Z · b8eccb7f+ | 171 pass, 0 fail, 0 skip · 2026-11-03 11:49Z · b8eccb7f+ |
| SqlServer | 158 pass, 0 fail, 0 skip · 2026-11-03 11:23Z · b8eccb7f+ | 158 pass, 0 fail, 0 skip · 2026-11-03 11:42Z · b8eccb7f+ |
| MySql | 161 pass, 0 fail, 0 skip · 2026-11-03 11:29Z · b8eccb7f+ | 161 pass, 0 fail, 0 skip · 2026-11-03 11:48Z · b8eccb7f+ |
| MariaDb | 161 pass, 0 fail, 0 skip · 2026-11-03 11:29Z · b8eccb7f+ | 161 pass, 0 fail, 0 skip · 2026-11-03 11:49Z · b8eccb7f+ |
| Firebird | 154 pass, 0 fail, 0 skip · 2026-11-03 11:18Z · b8eccb7f+ | 154 pass, 0 fail, 0 skip · 2026-11-03 11:37Z · b8eccb7f+ |
| CockroachDb | 167 pass, 0 fail, 0 skip · 2026-11-03 11:24Z · b8eccb7f+ | 167 pass, 0 fail, 0 skip · 2026-11-03 11:44Z · b8eccb7f+ |
| DuckDB | 154 pass, 0 fail, 0 skip · 2026-11-03 11:28Z · b8eccb7f+ | 154 pass, 0 fail, 0 skip · 2026-11-03 11:47Z · b8eccb7f+ |
| Oracle | 156 pass, 0 fail, 0 skip · 2026-11-03 11:26Z · b8eccb7f+ | 156 pass, 0 fail, 0 skip · 2026-11-03 11:46Z · b8eccb7f+ |
| YugabyteDb | 171 pass, 0 fail, 0 skip · 2026-11-03 11:53Z · b8eccb7f+ | 171 pass, 0 fail, 0 skip · 2026-11-03 12:11Z · b8eccb7f+ |
| TiDb | 161 pass, 0 fail, 0 skip · 2026-11-03 11:30Z · b8eccb7f+ | 161 pass, 0 fail, 0 skip · 2026-11-03 11:49Z · b8eccb7f+ |
| FlatFile | 154 pass, 0 fail, 0 skip · 2026-11-03 11:30Z · b8eccb7f+ | 154 pass, 0 fail, 0 skip · 2026-11-03 11:50Z · b8eccb7f+ |
| Db2 | 154 pass, 0 fail, 0 skip · 2026-11-03 11:15Z · b8eccb7f+ | 154 pass, 0 fail, 0 skip · 2026-11-03 11:34Z · b8eccb7f+ |
| Informix | 154 pass, 0 fail, 0 skip · 2026-11-03 11:30Z · b8eccb7f+ | 154 pass, 0 fail, 0 skip · 2026-11-03 11:50Z · b8eccb7f+ |
| SybaseASE | 154 pass, 0 fail, 0 skip · 2026-11-03 11:25Z · b8eccb7f+ | 154 pass, 0 fail, 0 skip · 2026-11-03 11:45Z · b8eccb7f+ |
| Spanner | 154 pass, 0 fail, 0 skip · 2026-11-03 12:08Z · b8eccb7f+ | 154 pass, 0 fail, 0 skip · 2026-11-03 12:26Z · b8eccb7f+ |
| SingleStore | 161 pass, 0 fail, 0 skip · 2026-11-03 11:27Z · b8eccb7f+ | 161 pass, 0 fail, 0 skip · 2026-11-03 11:47Z · b8eccb7f+ |
| Snowflake | 146 pass, 0 fail, 0 skip · 2026-10-12 14:58Z · 0d031db1 | 146 pass, 0 fail, 0 skip · 2026-10-12 15:45Z · 0d031db1 |
| SapHana | 152 pass, 0 fail, 0 skip · 2026-11-01 10:13Z · d6b387d1+ | 152 pass, 0 fail, 0 skip · 2026-11-01 10:38Z · d6b387d1+ |
| InterBase | 152 pass, 0 fail, 0 skip · 2026-11-01 10:32Z · d6b387d1+ | 152 pass, 0 fail, 0 skip · 2026-11-01 10:55Z · d6b387d1+ |
| Access | 154 pass, 0 fail, 0 skip · 2026-11-05 17:58Z · 9af672d9+ | 154 pass, 0 fail, 0 skip · 2026-11-05 18:00Z · 9af672d9+ |
| AuroraPostgreSql | 170 pass, 0 fail, 0 skip · 2026-11-01 12:05Z · 584d3e41 | 170 pass, 0 fail, 0 skip · 2026-11-01 12:18Z · 584d3e41 |
| AuroraMySql | 160 pass, 0 fail, 0 skip · 2026-11-01 12:09Z · 584d3e41 | 160 pass, 0 fail, 0 skip · 2026-11-01 12:23Z · 584d3e41 |

Failing now: PostgreSql net10.0.
