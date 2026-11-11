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
| Sqlite | 157 pass, 0 fail, 0 skip · 2026-11-11 20:39Z · 2d68e8d8 | 157 pass, 0 fail, 0 skip · 2026-11-11 21:10Z · 2d68e8d8 |
| PostgreSql | 173 pass, 0 fail, 0 skip · 2026-11-11 20:39Z · 2d68e8d8 | 173 pass, 0 fail, 0 skip · 2026-11-11 21:10Z · 2d68e8d8 |
| SqlServer | 160 pass, 0 fail, 0 skip · 2026-11-11 20:35Z · 2d68e8d8 | 160 pass, 0 fail, 0 skip · 2026-11-11 21:07Z · 2d68e8d8 |
| MySql | 163 pass, 0 fail, 0 skip · 2026-11-11 20:38Z · 2d68e8d8 | 163 pass, 0 fail, 0 skip · 2026-11-11 21:09Z · 2d68e8d8 |
| MariaDb | 163 pass, 0 fail, 0 skip · 2026-11-11 20:38Z · 2d68e8d8 | 163 pass, 0 fail, 0 skip · 2026-11-11 21:09Z · 2d68e8d8 |
| Firebird | 156 pass, 0 fail, 0 skip · 2026-11-11 20:33Z · 2d68e8d8 | 156 pass, 0 fail, 0 skip · 2026-11-11 21:04Z · 2d68e8d8 |
| CockroachDb | 169 pass, 0 fail, 0 skip · 2026-11-11 20:36Z · 2d68e8d8 | 169 pass, 0 fail, 0 skip · 2026-11-11 21:07Z · 2d68e8d8 |
| DuckDB | 156 pass, 0 fail, 0 skip · 2026-11-11 20:38Z · 2d68e8d8 | 156 pass, 0 fail, 0 skip · 2026-11-11 21:09Z · 2d68e8d8 |
| Oracle | 158 pass, 0 fail, 0 skip · 2026-11-11 20:37Z · 2d68e8d8 | 158 pass, 0 fail, 0 skip · 2026-11-11 21:08Z · 2d68e8d8 |
| YugabyteDb | 173 pass, 0 fail, 0 skip · 2026-11-11 20:34Z · 2d68e8d8 | 173 pass, 0 fail, 0 skip · 2026-11-11 21:05Z · 2d68e8d8 |
| TiDb | 163 pass, 0 fail, 0 skip · 2026-11-11 20:39Z · 2d68e8d8 | 163 pass, 0 fail, 0 skip · 2026-11-11 21:10Z · 2d68e8d8 |
| FlatFile | 156 pass, 0 fail, 0 skip · 2026-11-11 20:39Z · 2d68e8d8 | 156 pass, 0 fail, 0 skip · 2026-11-11 21:10Z · 2d68e8d8 |
| Db2 | 156 pass, 0 fail, 0 skip · 2026-11-11 20:29Z · 2d68e8d8 | 156 pass, 0 fail, 0 skip · 2026-11-11 21:15Z · 2d68e8d8+ |
| Informix | 156 pass, 0 fail, 0 skip · 2026-11-11 20:39Z · 2d68e8d8 | 156 pass, 0 fail, 0 skip · 2026-11-11 21:10Z · 2d68e8d8 |
| SybaseASE | 156 pass, 0 fail, 0 skip · 2026-11-11 20:36Z · 2d68e8d8 | 156 pass, 0 fail, 0 skip · 2026-11-11 21:08Z · 2d68e8d8 |
| Spanner | 156 pass, 0 fail, 0 skip · 2026-11-11 20:32Z · 2d68e8d8 | 156 pass, 0 fail, 0 skip · 2026-11-11 21:03Z · 2d68e8d8 |
| SingleStore | 163 pass, 0 fail, 0 skip · 2026-11-11 20:37Z · 2d68e8d8 | 163 pass, 0 fail, 0 skip · 2026-11-11 21:09Z · 2d68e8d8 |
| Snowflake | 156 pass, 0 fail, 0 skip · 2026-11-11 20:25Z · 2d68e8d8 | 156 pass, 0 fail, 0 skip · 2026-11-11 20:57Z · 2d68e8d8 |
| SapHana | 156 pass, 0 fail, 0 skip · 2026-11-11 23:19Z · 2d68e8d8+ | 156 pass, 0 fail, 0 skip · 2026-11-11 23:24Z · 2d68e8d8+ |
| InterBase | 156 pass, 0 fail, 0 skip · 2026-11-11 22:33Z · 2d68e8d8+ | 156 pass, 0 fail, 0 skip · 2026-11-11 22:47Z · 2d68e8d8+ |
| Access | 154 pass, 0 fail, 0 skip · 2026-11-11 19:46Z · ae6ba38c | 154 pass, 0 fail, 0 skip · 2026-11-11 19:48Z · ae6ba38c |
| AuroraPostgreSql | 174 pass, 0 fail, 0 skip · 2026-11-11 22:43Z · 2d68e8d8+ | 174 pass, 0 fail, 0 skip · 2026-11-11 22:57Z · 2d68e8d8+ |
| AuroraMySql | 164 pass, 0 fail, 0 skip · 2026-11-11 22:47Z · 2d68e8d8+ | 164 pass, 0 fail, 0 skip · 2026-11-11 23:01Z · 2d68e8d8+ |

Failing now: none.
