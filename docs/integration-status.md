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
| Sqlite | 148 pass, 0 fail, 0 skip · 2026-10-17 11:32Z · 5f813c36+ | 148 pass, 0 fail, 0 skip · 2026-10-17 11:55Z · 5f813c36+ |
| PostgreSql | 164 pass, 0 fail, 0 skip · 2026-10-17 11:32Z · 5f813c36+ | 164 pass, 0 fail, 0 skip · 2026-10-17 11:54Z · 5f813c36+ |
| SqlServer | 151 pass, 0 fail, 0 skip · 2026-10-17 11:28Z · 5f813c36+ | 151 pass, 0 fail, 0 skip · 2026-10-17 11:51Z · 5f813c36+ |
| MySql | 154 pass, 0 fail, 0 skip · 2026-10-17 11:32Z · 5f813c36+ | 154 pass, 0 fail, 0 skip · 2026-10-17 11:54Z · 5f813c36+ |
| MariaDb | 154 pass, 0 fail, 0 skip · 2026-10-17 11:32Z · 5f813c36+ | 154 pass, 0 fail, 0 skip · 2026-10-17 11:54Z · 5f813c36+ |
| Firebird | 147 pass, 0 fail, 0 skip · 2026-10-17 11:24Z · 5f813c36+ | 147 pass, 0 fail, 0 skip · 2026-10-17 11:46Z · 5f813c36+ |
| CockroachDb | 160 pass, 0 fail, 0 skip · 2026-10-17 11:30Z · 5f813c36+ | 160 pass, 0 fail, 0 skip · 2026-10-17 11:52Z · 5f813c36+ |
| DuckDB | 147 pass, 0 fail, 0 skip · 2026-10-17 11:31Z · 5f813c36+ | 147 pass, 0 fail, 0 skip · 2026-10-17 11:54Z · 5f813c36+ |
| Oracle | 148 pass, 0 fail, 0 skip · 2026-10-17 11:31Z · 5f813c36+ | 148 pass, 0 fail, 0 skip · 2026-10-17 11:53Z · 5f813c36+ |
| YugabyteDb | 164 pass, 0 fail, 0 skip · 2026-10-17 11:26Z · 5f813c36+ | 164 pass, 0 fail, 0 skip · 2026-10-17 11:48Z · 5f813c36+ |
| TiDb | 154 pass, 0 fail, 0 skip · 2026-10-17 11:33Z · 5f813c36+ | 154 pass, 0 fail, 0 skip · 2026-10-17 11:55Z · 5f813c36+ |
| FlatFile | 147 pass, 0 fail, 0 skip · 2026-10-17 11:33Z · 5f813c36+ | 147 pass, 0 fail, 0 skip · 2026-10-17 11:55Z · 5f813c36+ |
| Db2 | 147 pass, 0 fail, 0 skip · 2026-10-17 11:21Z · 5f813c36+ | 147 pass, 0 fail, 0 skip · 2026-10-17 11:43Z · 5f813c36+ |
| Informix | 147 pass, 0 fail, 0 skip · 2026-10-17 11:33Z · 5f813c36+ | 147 pass, 0 fail, 0 skip · 2026-10-17 11:55Z · 5f813c36+ |
| SybaseASE | 147 pass, 0 fail, 0 skip · 2026-10-17 11:30Z · 5f813c36+ | 147 pass, 0 fail, 0 skip · 2026-10-17 11:52Z · 5f813c36+ |
| Spanner | 147 pass, 0 fail, 0 skip · 2026-10-17 11:29Z · 5f813c36+ | 147 pass, 0 fail, 0 skip · 2026-10-17 11:51Z · 5f813c36+ |
| SingleStore | 154 pass, 0 fail, 0 skip · 2026-10-17 11:31Z · 5f813c36+ | 154 pass, 0 fail, 0 skip · 2026-10-17 11:53Z · 5f813c36+ |
| Snowflake | 146 pass, 0 fail, 0 skip · 2026-10-12 14:58Z · 0d031db1 | 146 pass, 0 fail, 0 skip · 2026-10-12 15:45Z · 0d031db1 |
| SapHana | 147 pass, 0 fail, 0 skip · 2026-10-17 11:16Z · 5f813c36+ | 147 pass, 0 fail, 0 skip · 2026-10-17 11:38Z · 5f813c36+ |
| InterBase | 147 pass, 0 fail, 0 skip · 2026-10-17 11:33Z · 5f813c36+ | 147 pass, 0 fail, 0 skip · 2026-10-17 11:55Z · 5f813c36+ |
| Access | 144 pass, 0 fail, 0 skip · 2026-10-12 19:05Z · e0137a99+ | 144 pass, 0 fail, 0 skip · 2026-10-12 19:07Z · e0137a99+ |
| AuroraPostgreSql | 144 pass, 0 fail, 0 skip · 2026-10-12 15:21Z · 0d031db1 | 144 pass, 0 fail, 0 skip · 2026-10-13 19:30Z · 6a127448+ |
| AuroraMySql | 144 pass, 0 fail, 0 skip · 2026-10-12 15:25Z · 0d031db1 | 144 pass, 0 fail, 0 skip · 2026-10-13 19:35Z · 6a127448+ |

Failing now: none.
