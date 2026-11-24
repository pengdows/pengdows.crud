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
| Sqlite | 158 pass, 0 fail, 0 skip · 2026-11-24 19:05Z · 0c482bca+ | 158 pass, 0 fail, 0 skip · 2026-11-24 20:04Z · 0c482bca+ |
| PostgreSql | 191 pass, 0 fail, 0 skip · 2026-11-24 19:05Z · 0c482bca+ | 191 pass, 0 fail, 0 skip · 2026-11-24 20:04Z · 0c482bca+ |
| SqlServer | 161 pass, 0 fail, 0 skip · 2026-11-24 18:59Z · 0c482bca+ | 161 pass, 0 fail, 0 skip · 2026-11-24 19:59Z · 0c482bca+ |
| MySql | 169 pass, 0 fail, 0 skip · 2026-11-24 19:04Z · 0c482bca+ | 169 pass, 0 fail, 0 skip · 2026-11-24 20:03Z · 0c482bca+ |
| MariaDb | 169 pass, 0 fail, 0 skip · 2026-11-24 19:04Z · 0c482bca+ | 169 pass, 0 fail, 0 skip · 2026-11-24 20:03Z · 0c482bca+ |
| Firebird | 157 pass, 0 fail, 0 skip · 2026-11-24 18:58Z · 0c482bca+ | 157 pass, 0 fail, 0 skip · 2026-11-24 19:57Z · 0c482bca+ |
| CockroachDb | 187 pass, 0 fail, 0 skip · 2026-11-24 19:01Z · 0c482bca+ | 187 pass, 0 fail, 0 skip · 2026-11-24 20:00Z · 0c482bca+ |
| DuckDB | 157 pass, 0 fail, 0 skip · 2026-11-24 19:04Z · 0c482bca+ | 157 pass, 0 fail, 0 skip · 2026-11-24 20:03Z · 0c482bca+ |
| Oracle | 159 pass, 0 fail, 0 skip · 2026-11-24 19:02Z · 0c482bca+ | 159 pass, 0 fail, 0 skip · 2026-11-24 20:01Z · 0c482bca+ |
| YugabyteDb | 1 pass, 0 fail, 0 skip · 2026-11-24 17:07Z · 0c482bca+ | 191 pass, 0 fail, 0 skip · 2026-11-24 12:18Z · 0c482bca |
| TiDb | 169 pass, 0 fail, 0 skip · 2026-11-24 19:06Z · 0c482bca+ | 169 pass, 0 fail, 0 skip · 2026-11-24 20:05Z · 0c482bca+ |
| FlatFile | 157 pass, 0 fail, 0 skip · 2026-11-24 19:10Z · 0c482bca+ | 157 pass, 0 fail, 0 skip · 2026-11-24 20:08Z · 0c482bca+ |
| Db2 | 157 pass, 0 fail, 0 skip · 2026-11-24 18:55Z · 0c482bca+ | 157 pass, 0 fail, 0 skip · 2026-11-24 19:55Z · 0c482bca+ |
| Informix | 158 pass, 0 fail, 0 skip · 2026-11-24 19:06Z · 0c482bca+ | 158 pass, 0 fail, 0 skip · 2026-11-24 20:05Z · 0c482bca+ |
| SybaseASE | 157 pass, 0 fail, 0 skip · 2026-11-24 19:01Z · 0c482bca+ | 157 pass, 0 fail, 0 skip · 2026-11-24 20:00Z · 0c482bca+ |
| Spanner | 157 pass, 0 fail, 0 skip · 2026-11-24 18:53Z · 0c482bca+ | 157 pass, 0 fail, 0 skip · 2026-11-24 19:52Z · 0c482bca+ |
| SingleStore | 169 pass, 0 fail, 0 skip · 2026-11-24 11:59Z · 0c482bca | 169 pass, 0 fail, 0 skip · 2026-11-24 12:25Z · 0c482bca |
| Snowflake | 119 pass, 1 fail, 0 skip · 2026-11-23 14:12Z · fc4a1a01+ | 1 pass, 1 fail, 0 skip · 2026-11-22 21:57Z · fc4a1a01+ |
| SapHana | 157 pass, 0 fail, 0 skip · 2026-11-24 10:58Z · fb05263e+ | 157 pass, 0 fail, 0 skip · 2026-11-24 11:01Z · fb05263e+ |
| InterBase | 157 pass, 0 fail, 0 skip · 2026-11-24 19:09Z · 0c482bca+ | 157 pass, 0 fail, 0 skip · 2026-11-24 20:08Z · 0c482bca+ |
| Access | 156 pass, 0 fail, 0 skip · 2026-11-21 21:21Z · 83c33667+ | 156 pass, 0 fail, 0 skip · 2026-11-21 21:23Z · 83c33667+ |
| AuroraPostgreSql | 174 pass, 0 fail, 0 skip · 2026-11-13 16:22Z · e11ef215 | 174 pass, 0 fail, 0 skip · 2026-11-13 16:47Z · e11ef215 |
| AuroraMySql | 164 pass, 0 fail, 0 skip · 2026-11-13 16:12Z · e11ef215 | 164 pass, 0 fail, 0 skip · 2026-11-13 16:37Z · e11ef215 |

Failing now: Snowflake net10.0, Snowflake net8.0.
