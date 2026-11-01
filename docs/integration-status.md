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
| Sqlite | 153 pass, 0 fail, 0 skip · 2026-11-01 10:31Z · d6b387d1+ | 153 pass, 0 fail, 0 skip · 2026-11-01 10:54Z · d6b387d1+ |
| PostgreSql | 169 pass, 0 fail, 0 skip · 2026-11-01 10:31Z · d6b387d1+ | 169 pass, 0 fail, 0 skip · 2026-11-01 10:54Z · d6b387d1+ |
| SqlServer | 156 pass, 0 fail, 0 skip · 2026-11-01 10:26Z · d6b387d1+ | 156 pass, 0 fail, 0 skip · 2026-11-01 10:50Z · d6b387d1+ |
| MySql | 159 pass, 0 fail, 0 skip · 2026-11-01 10:31Z · d6b387d1+ | 159 pass, 0 fail, 0 skip · 2026-11-01 10:53Z · d6b387d1+ |
| MariaDb | 159 pass, 0 fail, 0 skip · 2026-11-01 10:31Z · d6b387d1+ | 159 pass, 0 fail, 0 skip · 2026-11-01 10:54Z · d6b387d1+ |
| Firebird | 152 pass, 0 fail, 0 skip · 2026-11-01 10:21Z · d6b387d1+ | 152 pass, 0 fail, 0 skip · 2026-11-01 10:46Z · d6b387d1+ |
| CockroachDb | 165 pass, 0 fail, 0 skip · 2026-11-01 10:28Z · d6b387d1+ | 165 pass, 0 fail, 0 skip · 2026-11-01 10:51Z · d6b387d1+ |
| DuckDB | 152 pass, 0 fail, 0 skip · 2026-11-01 10:30Z · d6b387d1+ | 152 pass, 0 fail, 0 skip · 2026-11-01 10:53Z · d6b387d1+ |
| Oracle | 154 pass, 0 fail, 0 skip · 2026-11-01 10:30Z · d6b387d1+ | 154 pass, 0 fail, 0 skip · 2026-11-01 10:53Z · d6b387d1+ |
| YugabyteDb | 169 pass, 0 fail, 0 skip · 2026-11-01 10:23Z · d6b387d1+ | 169 pass, 0 fail, 0 skip · 2026-11-01 10:48Z · d6b387d1+ |
| TiDb | 159 pass, 0 fail, 0 skip · 2026-11-01 10:32Z · d6b387d1+ | 159 pass, 0 fail, 0 skip · 2026-11-01 10:54Z · d6b387d1+ |
| FlatFile | 152 pass, 0 fail, 0 skip · 2026-11-01 10:32Z · d6b387d1+ | 152 pass, 0 fail, 0 skip · 2026-11-01 10:55Z · d6b387d1+ |
| Db2 | 152 pass, 0 fail, 0 skip · 2026-11-01 10:17Z · d6b387d1+ | 152 pass, 0 fail, 0 skip · 2026-11-01 10:42Z · d6b387d1+ |
| Informix | 152 pass, 0 fail, 0 skip · 2026-11-01 10:32Z · d6b387d1+ | 152 pass, 0 fail, 0 skip · 2026-11-01 10:55Z · d6b387d1+ |
| SybaseASE | 152 pass, 0 fail, 0 skip · 2026-11-01 10:27Z · d6b387d1+ | 152 pass, 0 fail, 0 skip · 2026-11-01 10:51Z · d6b387d1+ |
| Spanner | 152 pass, 0 fail, 0 skip · 2026-11-01 10:26Z · d6b387d1+ | 152 pass, 0 fail, 0 skip · 2026-11-01 10:50Z · d6b387d1+ |
| SingleStore | 159 pass, 0 fail, 0 skip · 2026-11-01 10:29Z · d6b387d1+ | 159 pass, 0 fail, 0 skip · 2026-11-01 10:52Z · d6b387d1+ |
| Snowflake | 146 pass, 0 fail, 0 skip · 2026-10-12 14:58Z · 0d031db1 | 146 pass, 0 fail, 0 skip · 2026-10-12 15:45Z · 0d031db1 |
| SapHana | 152 pass, 0 fail, 0 skip · 2026-11-01 10:13Z · d6b387d1+ | 152 pass, 0 fail, 0 skip · 2026-11-01 10:38Z · d6b387d1+ |
| InterBase | 152 pass, 0 fail, 0 skip · 2026-11-01 10:32Z · d6b387d1+ | 152 pass, 0 fail, 0 skip · 2026-11-01 10:55Z · d6b387d1+ |
| Access | 150 pass, 0 fail, 0 skip · 2026-11-01 11:31Z · 34a03801 | 150 pass, 0 fail, 0 skip · 2026-11-01 11:33Z · 34a03801 |
| AuroraPostgreSql | 170 pass, 0 fail, 0 skip · 2026-10-24 04:36Z · 43c1ea61 | 170 pass, 0 fail, 0 skip · 2026-10-24 05:10Z · 43c1ea61 |
| AuroraMySql | 160 pass, 0 fail, 0 skip · 2026-10-24 04:40Z · 43c1ea61 | 160 pass, 0 fail, 0 skip · 2026-10-24 05:14Z · 43c1ea61 |

Failing now: none.
