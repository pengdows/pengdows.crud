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
| Sqlite | 157 pass, 0 fail, 0 skip · 2026-11-10 18:04Z · 2bb5c46c+ | 157 pass, 0 fail, 0 skip · 2026-11-10 18:30Z · 2bb5c46c+ |
| PostgreSql | 173 pass, 0 fail, 0 skip · 2026-11-10 18:05Z · 2bb5c46c+ | 173 pass, 0 fail, 0 skip · 2026-11-10 18:30Z · 2bb5c46c+ |
| SqlServer | 160 pass, 0 fail, 0 skip · 2026-11-10 17:58Z · 2bb5c46c+ | 160 pass, 0 fail, 0 skip · 2026-11-10 18:23Z · 2bb5c46c+ |
| MySql | 163 pass, 0 fail, 0 skip · 2026-11-10 18:04Z · 2bb5c46c+ | 163 pass, 0 fail, 0 skip · 2026-11-10 18:29Z · 2bb5c46c+ |
| MariaDb | 163 pass, 0 fail, 0 skip · 2026-11-10 18:04Z · 2bb5c46c+ | 163 pass, 0 fail, 0 skip · 2026-11-10 18:30Z · 2bb5c46c+ |
| Firebird | 156 pass, 0 fail, 0 skip · 2026-11-10 17:53Z · 2bb5c46c+ | 156 pass, 0 fail, 0 skip · 2026-11-10 18:18Z · 2bb5c46c+ |
| CockroachDb | 169 pass, 0 fail, 0 skip · 2026-11-10 17:59Z · 2bb5c46c+ | 169 pass, 0 fail, 0 skip · 2026-11-10 18:25Z · 2bb5c46c+ |
| DuckDB | 156 pass, 0 fail, 0 skip · 2026-11-10 18:03Z · 2bb5c46c+ | 156 pass, 0 fail, 0 skip · 2026-11-10 18:29Z · 2bb5c46c+ |
| Oracle | 158 pass, 0 fail, 0 skip · 2026-11-10 22:52Z · dbc3a701+ | 158 pass, 0 fail, 0 skip · 2026-11-10 22:53Z · dbc3a701+ |
| YugabyteDb | 173 pass, 0 fail, 0 skip · 2026-11-10 17:55Z · 2bb5c46c+ | 173 pass, 0 fail, 0 skip · 2026-11-10 18:21Z · 2bb5c46c+ |
| TiDb | 163 pass, 0 fail, 0 skip · 2026-11-10 18:05Z · 2bb5c46c+ | 163 pass, 0 fail, 0 skip · 2026-11-10 18:30Z · 2bb5c46c+ |
| FlatFile | 156 pass, 0 fail, 0 skip · 2026-11-10 18:05Z · 2bb5c46c+ | 156 pass, 0 fail, 0 skip · 2026-11-10 18:31Z · 2bb5c46c+ |
| Db2 | 156 pass, 0 fail, 0 skip · 2026-11-10 17:49Z · 2bb5c46c+ | 156 pass, 0 fail, 0 skip · 2026-11-10 18:15Z · 2bb5c46c+ |
| Informix | 156 pass, 0 fail, 0 skip · 2026-11-10 18:05Z · 2bb5c46c+ | 156 pass, 0 fail, 0 skip · 2026-11-10 18:31Z · 2bb5c46c+ |
| SybaseASE | 156 pass, 0 fail, 0 skip · 2026-11-10 18:00Z · 2bb5c46c+ | 156 pass, 0 fail, 0 skip · 2026-11-10 18:26Z · 2bb5c46c+ |
| Spanner | 156 pass, 0 fail, 0 skip · 2026-11-10 18:03Z · 2bb5c46c+ | 156 pass, 0 fail, 0 skip · 2026-11-10 18:29Z · 2bb5c46c+ |
| SingleStore | 163 pass, 0 fail, 0 skip · 2026-11-10 18:02Z · 2bb5c46c+ | 163 pass, 0 fail, 0 skip · 2026-11-10 18:28Z · 2bb5c46c+ |
| Snowflake | 156 pass, 0 fail, 0 skip · 2026-11-11 12:31Z · eef76336+ | 156 pass, 0 fail, 0 skip · 2026-11-11 12:49Z · eef76336+ |
| SapHana | 156 pass, 0 fail, 0 skip · 2026-11-10 17:45Z · 2bb5c46c+ | 156 pass, 0 fail, 0 skip · 2026-11-10 18:11Z · 2bb5c46c+ |
| InterBase | 156 pass, 0 fail, 0 skip · 2026-11-10 18:05Z · 2bb5c46c+ | 156 pass, 0 fail, 0 skip · 2026-11-10 18:31Z · 2bb5c46c+ |
| Access | 154 pass, 0 fail, 0 skip · 2026-11-11 19:46Z · ae6ba38c | 154 pass, 0 fail, 0 skip · 2026-11-11 19:48Z · ae6ba38c |
| AuroraPostgreSql | 174 pass, 0 fail, 0 skip · 2026-11-11 01:18Z · dbc3a701+ | 174 pass, 0 fail, 0 skip · 2026-11-11 01:32Z · dbc3a701+ |
| AuroraMySql | 164 pass, 0 fail, 0 skip · 2026-11-11 01:23Z · dbc3a701+ | 164 pass, 0 fail, 0 skip · 2026-11-11 01:37Z · dbc3a701+ |

Failing now: none.
