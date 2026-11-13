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
| Sqlite | 157 pass, 0 fail, 0 skip · 2026-11-13 10:34Z · 63891bba | 157 pass, 0 fail, 0 skip · 2026-11-13 10:55Z · 63891bba |
| PostgreSql | 173 pass, 0 fail, 0 skip · 2026-11-13 10:34Z · 63891bba | 173 pass, 0 fail, 0 skip · 2026-11-13 10:55Z · 63891bba |
| SqlServer | 160 pass, 0 fail, 0 skip · 2026-11-13 00:01Z · e9c7d8d8 | 160 pass, 0 fail, 0 skip · 2026-11-13 10:47Z · 63891bba |
| MySql | 163 pass, 0 fail, 0 skip · 2026-11-13 10:34Z · 63891bba | 163 pass, 0 fail, 0 skip · 2026-11-13 10:53Z · 63891bba |
| MariaDb | 163 pass, 0 fail, 0 skip · 2026-11-13 10:34Z · 63891bba | 163 pass, 0 fail, 0 skip · 2026-11-13 10:54Z · 63891bba |
| Firebird | 156 pass, 0 fail, 0 skip · 2026-11-12 23:56Z · e9c7d8d8 | 156 pass, 0 fail, 0 skip · 2026-11-13 10:43Z · 63891bba |
| CockroachDb | 169 pass, 0 fail, 0 skip · 2026-11-13 00:03Z · e9c7d8d8 | 169 pass, 0 fail, 0 skip · 2026-11-13 10:49Z · 63891bba |
| DuckDB | 156 pass, 0 fail, 0 skip · 2026-11-13 10:33Z · 63891bba | 156 pass, 0 fail, 0 skip · 2026-11-13 10:53Z · 63891bba |
| Oracle | 158 pass, 0 fail, 0 skip · 2026-11-13 00:05Z · e9c7d8d8 | 158 pass, 0 fail, 0 skip · 2026-11-13 10:51Z · 63891bba |
| YugabyteDb | 173 pass, 0 fail, 0 skip · 2026-11-12 23:59Z · e9c7d8d8 | 173 pass, 0 fail, 0 skip · 2026-11-13 10:45Z · 63891bba |
| TiDb | 163 pass, 0 fail, 0 skip · 2026-11-13 10:35Z · 63891bba | 163 pass, 0 fail, 0 skip · 2026-11-13 10:55Z · 63891bba |
| FlatFile | 156 pass, 0 fail, 0 skip · 2026-11-13 10:35Z · 63891bba | 156 pass, 0 fail, 0 skip · 2026-11-13 10:56Z · 63891bba |
| Db2 | 156 pass, 0 fail, 0 skip · 2026-11-12 23:53Z · e9c7d8d8 | 156 pass, 0 fail, 0 skip · 2026-11-13 11:37Z · 63891bba+ |
| Informix | 156 pass, 0 fail, 0 skip · 2026-11-13 10:35Z · 63891bba | 156 pass, 0 fail, 0 skip · 2026-11-13 10:56Z · 63891bba |
| SybaseASE | 156 pass, 0 fail, 0 skip · 2026-11-13 00:04Z · e9c7d8d8 | 156 pass, 0 fail, 0 skip · 2026-11-13 10:50Z · 63891bba |
| Spanner | 156 pass, 0 fail, 0 skip · 2026-11-13 10:33Z · 63891bba | 156 pass, 0 fail, 0 skip · 2026-11-13 10:58Z · 63891bba |
| SingleStore | 163 pass, 0 fail, 0 skip · 2026-11-13 00:06Z · e9c7d8d8 | 163 pass, 0 fail, 0 skip · 2026-11-13 10:52Z · 63891bba |
| Snowflake | 156 pass, 0 fail, 0 skip · 2026-11-11 20:25Z · 2d68e8d8 | 156 pass, 0 fail, 0 skip · 2026-11-11 20:57Z · 2d68e8d8 |
| SapHana | 156 pass, 0 fail, 0 skip · 2026-11-13 12:35Z · 63891bba+ | 156 pass, 0 fail, 0 skip · 2026-11-13 12:40Z · 63891bba+ |
| InterBase | 156 pass, 0 fail, 0 skip · 2026-11-13 10:09Z · 015a4059+ | 156 pass, 0 fail, 0 skip · 2026-11-13 10:09Z · 015a4059+ |
| Access | 154 pass, 0 fail, 0 skip · 2026-11-11 19:46Z · ae6ba38c | 154 pass, 0 fail, 0 skip · 2026-11-11 19:48Z · ae6ba38c |
| AuroraPostgreSql | 174 pass, 0 fail, 0 skip · 2026-11-11 22:43Z · 2d68e8d8+ | 174 pass, 0 fail, 0 skip · 2026-11-11 22:57Z · 2d68e8d8+ |
| AuroraMySql | 164 pass, 0 fail, 0 skip · 2026-11-11 22:47Z · 2d68e8d8+ | 164 pass, 0 fail, 0 skip · 2026-11-11 23:01Z · 2d68e8d8+ |

Failing now: none.
