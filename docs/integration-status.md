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
| Sqlite | 158 pass, 0 fail, 0 skip · 2026-11-23 16:48Z · fc4a1a01+ | 158 pass, 0 fail, 0 skip · 2026-11-23 17:11Z · fc4a1a01+ |
| PostgreSql | 191 pass, 0 fail, 0 skip · 2026-11-23 16:48Z · fc4a1a01+ | 191 pass, 0 fail, 0 skip · 2026-11-23 17:11Z · fc4a1a01+ |
| SqlServer | 161 pass, 0 fail, 0 skip · 2026-11-23 16:42Z · fc4a1a01+ | 161 pass, 0 fail, 0 skip · 2026-11-23 17:05Z · fc4a1a01+ |
| MySql | 169 pass, 0 fail, 0 skip · 2026-11-23 16:47Z · fc4a1a01+ | 169 pass, 0 fail, 0 skip · 2026-11-23 17:10Z · fc4a1a01+ |
| MariaDb | 169 pass, 0 fail, 0 skip · 2026-11-23 16:47Z · fc4a1a01+ | 169 pass, 0 fail, 0 skip · 2026-11-23 17:10Z · fc4a1a01+ |
| Firebird | 157 pass, 0 fail, 0 skip · 2026-11-23 16:38Z · fc4a1a01+ | 157 pass, 0 fail, 0 skip · 2026-11-23 17:01Z · fc4a1a01+ |
| CockroachDb | 187 pass, 0 fail, 0 skip · 2026-11-23 16:43Z · fc4a1a01+ | 187 pass, 0 fail, 0 skip · 2026-11-23 17:06Z · fc4a1a01+ |
| DuckDB | 157 pass, 0 fail, 0 skip · 2026-11-23 16:47Z · fc4a1a01+ | 157 pass, 0 fail, 0 skip · 2026-11-23 17:09Z · fc4a1a01+ |
| Oracle | 159 pass, 0 fail, 0 skip · 2026-11-23 16:44Z · fc4a1a01+ | 159 pass, 0 fail, 0 skip · 2026-11-23 17:07Z · fc4a1a01+ |
| YugabyteDb | 191 pass, 0 fail, 0 skip · 2026-11-23 16:41Z · fc4a1a01+ | 191 pass, 0 fail, 0 skip · 2026-11-23 17:03Z · fc4a1a01+ |
| TiDb | 169 pass, 0 fail, 0 skip · 2026-11-23 16:49Z · fc4a1a01+ | 169 pass, 0 fail, 0 skip · 2026-11-23 17:12Z · fc4a1a01+ |
| FlatFile | 157 pass, 0 fail, 0 skip · 2026-11-23 16:53Z · fc4a1a01+ | 157 pass, 0 fail, 0 skip · 2026-11-23 17:15Z · fc4a1a01+ |
| Db2 | 157 pass, 0 fail, 0 skip · 2026-11-23 16:36Z · fc4a1a01+ | 157 pass, 0 fail, 0 skip · 2026-11-23 16:59Z · fc4a1a01+ |
| Informix | 158 pass, 0 fail, 0 skip · 2026-11-23 16:49Z · fc4a1a01+ | 158 pass, 0 fail, 0 skip · 2026-11-23 17:12Z · fc4a1a01+ |
| SybaseASE | 157 pass, 0 fail, 0 skip · 2026-11-23 16:44Z · fc4a1a01+ | 157 pass, 0 fail, 0 skip · 2026-11-23 17:07Z · fc4a1a01+ |
| Spanner | 157 pass, 0 fail, 0 skip · 2026-11-23 18:10Z · fc4a1a01+ | 156 pass, 1 fail, 0 skip · 2026-11-23 17:19Z · fc4a1a01+ |
| SingleStore | 169 pass, 0 fail, 0 skip · 2026-11-23 16:45Z · fc4a1a01+ | 169 pass, 0 fail, 0 skip · 2026-11-23 17:08Z · fc4a1a01+ |
| Snowflake | 119 pass, 1 fail, 0 skip · 2026-11-23 14:12Z · fc4a1a01+ | 1 pass, 1 fail, 0 skip · 2026-11-22 21:57Z · fc4a1a01+ |
| SapHana | 157 pass, 0 fail, 0 skip · 2026-11-24 10:58Z · fb05263e+ | 157 pass, 0 fail, 0 skip · 2026-11-24 11:01Z · fb05263e+ |
| InterBase | 157 pass, 0 fail, 0 skip · 2026-11-23 16:52Z · fc4a1a01+ | 157 pass, 0 fail, 0 skip · 2026-11-23 17:15Z · fc4a1a01+ |
| Access | 156 pass, 0 fail, 0 skip · 2026-11-21 21:21Z · 83c33667+ | 156 pass, 0 fail, 0 skip · 2026-11-21 21:23Z · 83c33667+ |
| AuroraPostgreSql | 174 pass, 0 fail, 0 skip · 2026-11-13 16:22Z · e11ef215 | 174 pass, 0 fail, 0 skip · 2026-11-13 16:47Z · e11ef215 |
| AuroraMySql | 164 pass, 0 fail, 0 skip · 2026-11-13 16:12Z · e11ef215 | 164 pass, 0 fail, 0 skip · 2026-11-13 16:37Z · e11ef215 |

Failing now: Snowflake net10.0, Snowflake net8.0, Spanner net10.0.
