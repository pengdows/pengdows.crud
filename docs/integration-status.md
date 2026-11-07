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
| Sqlite | 157 pass, 0 fail, 0 skip · 2026-11-06 23:34Z · 665c7975+ | 157 pass, 0 fail, 0 skip · 2026-11-06 23:58Z · 665c7975+ |
| PostgreSql | 173 pass, 0 fail, 0 skip · 2026-11-06 23:34Z · 665c7975+ | 173 pass, 0 fail, 0 skip · 2026-11-06 23:58Z · 665c7975+ |
| SqlServer | 160 pass, 0 fail, 0 skip · 2026-11-06 23:27Z · 665c7975+ | 160 pass, 0 fail, 0 skip · 2026-11-06 23:51Z · 665c7975+ |
| MySql | 163 pass, 0 fail, 0 skip · 2026-11-06 23:33Z · 665c7975+ | 163 pass, 0 fail, 0 skip · 2026-11-06 23:57Z · 665c7975+ |
| MariaDb | 163 pass, 0 fail, 0 skip · 2026-11-06 23:33Z · 665c7975+ | 163 pass, 0 fail, 0 skip · 2026-11-06 23:57Z · 665c7975+ |
| Firebird | 156 pass, 0 fail, 0 skip · 2026-11-06 23:22Z · 665c7975+ | 156 pass, 0 fail, 0 skip · 2026-11-06 23:46Z · 665c7975+ |
| CockroachDb | 169 pass, 0 fail, 0 skip · 2026-11-06 23:28Z · 665c7975+ | 169 pass, 0 fail, 0 skip · 2026-11-06 23:52Z · 665c7975+ |
| DuckDB | 156 pass, 0 fail, 0 skip · 2026-11-06 23:32Z · 665c7975+ | 156 pass, 0 fail, 0 skip · 2026-11-06 23:56Z · 665c7975+ |
| Oracle | 158 pass, 0 fail, 0 skip · 2026-11-06 23:30Z · 665c7975+ | 158 pass, 0 fail, 0 skip · 2026-11-06 23:54Z · 665c7975+ |
| YugabyteDb | 173 pass, 0 fail, 0 skip · 2026-11-06 23:24Z · 665c7975+ | 173 pass, 0 fail, 0 skip · 2026-11-06 23:48Z · 665c7975+ |
| TiDb | 163 pass, 0 fail, 0 skip · 2026-11-06 23:35Z · 665c7975+ | 163 pass, 0 fail, 0 skip · 2026-11-06 23:58Z · 665c7975+ |
| FlatFile | 156 pass, 0 fail, 0 skip · 2026-11-06 23:35Z · 665c7975+ | 156 pass, 0 fail, 0 skip · 2026-11-06 23:59Z · 665c7975+ |
| Db2 | 156 pass, 0 fail, 0 skip · 2026-11-06 23:18Z · 665c7975+ | 156 pass, 0 fail, 0 skip · 2026-11-06 23:42Z · 665c7975+ |
| Informix | 156 pass, 0 fail, 0 skip · 2026-11-06 23:35Z · 665c7975+ | 156 pass, 0 fail, 0 skip · 2026-11-06 23:59Z · 665c7975+ |
| SybaseASE | 156 pass, 0 fail, 0 skip · 2026-11-06 23:29Z · 665c7975+ | 156 pass, 0 fail, 0 skip · 2026-11-06 23:53Z · 665c7975+ |
| Spanner | 156 pass, 0 fail, 0 skip · 2026-11-06 23:38Z · 665c7975+ | 156 pass, 0 fail, 0 skip · 2026-11-07 00:02Z · 665c7975+ |
| SingleStore | 163 pass, 0 fail, 0 skip · 2026-11-06 23:31Z · 665c7975+ | 163 pass, 0 fail, 0 skip · 2026-11-06 23:55Z · 665c7975+ |
| Snowflake | 146 pass, 0 fail, 0 skip · 2026-10-12 14:58Z · 0d031db1 | 146 pass, 0 fail, 0 skip · 2026-10-12 15:45Z · 0d031db1 |
| SapHana | 152 pass, 0 fail, 0 skip · 2026-11-01 10:13Z · d6b387d1+ | 152 pass, 0 fail, 0 skip · 2026-11-01 10:38Z · d6b387d1+ |
| InterBase | 152 pass, 0 fail, 0 skip · 2026-11-01 10:32Z · d6b387d1+ | 152 pass, 0 fail, 0 skip · 2026-11-01 10:55Z · d6b387d1+ |
| Access | 154 pass, 0 fail, 0 skip · 2026-11-05 17:58Z · 9af672d9+ | 154 pass, 0 fail, 0 skip · 2026-11-05 18:00Z · 9af672d9+ |
| AuroraPostgreSql | 170 pass, 0 fail, 0 skip · 2026-11-01 12:05Z · 584d3e41 | 170 pass, 0 fail, 0 skip · 2026-11-01 12:18Z · 584d3e41 |
| AuroraMySql | 160 pass, 0 fail, 0 skip · 2026-11-01 12:09Z · 584d3e41 | 160 pass, 0 fail, 0 skip · 2026-11-01 12:23Z · 584d3e41 |

Failing now: none.
