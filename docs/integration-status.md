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
| Sqlite | 153 pass, 0 fail, 0 skip · 2026-10-17 21:00Z · fb83f22f | 153 pass, 0 fail, 0 skip · 2026-10-17 21:20Z · fb83f22f |
| PostgreSql | 169 pass, 0 fail, 0 skip · 2026-10-17 21:00Z · fb83f22f | 169 pass, 0 fail, 0 skip · 2026-10-17 21:19Z · fb83f22f |
| SqlServer | 156 pass, 0 fail, 0 skip · 2026-10-17 20:53Z · fb83f22f | 156 pass, 0 fail, 0 skip · 2026-10-17 21:13Z · fb83f22f |
| MySql | 159 pass, 0 fail, 0 skip · 2026-10-17 20:59Z · fb83f22f | 159 pass, 0 fail, 0 skip · 2026-10-17 21:18Z · fb83f22f |
| MariaDb | 159 pass, 0 fail, 0 skip · 2026-10-17 20:59Z · fb83f22f | 159 pass, 0 fail, 0 skip · 2026-10-17 21:19Z · fb83f22f |
| Firebird | 152 pass, 0 fail, 0 skip · 2026-10-17 20:49Z · fb83f22f | 152 pass, 0 fail, 0 skip · 2026-10-17 21:08Z · fb83f22f |
| CockroachDb | 165 pass, 0 fail, 0 skip · 2026-10-17 20:55Z · fb83f22f | 165 pass, 0 fail, 0 skip · 2026-10-17 21:14Z · fb83f22f |
| DuckDB | 152 pass, 0 fail, 0 skip · 2026-10-17 20:58Z · fb83f22f | 152 pass, 0 fail, 0 skip · 2026-10-17 21:18Z · fb83f22f |
| Oracle | 153 pass, 0 fail, 0 skip · 2026-10-17 20:57Z · fb83f22f | 153 pass, 0 fail, 0 skip · 2026-10-17 21:16Z · fb83f22f |
| YugabyteDb | 169 pass, 0 fail, 0 skip · 2026-10-17 20:51Z · fb83f22f | 169 pass, 0 fail, 0 skip · 2026-10-17 21:10Z · fb83f22f |
| TiDb | 159 pass, 0 fail, 0 skip · 2026-10-17 21:00Z · fb83f22f | 159 pass, 0 fail, 0 skip · 2026-10-17 21:20Z · fb83f22f |
| FlatFile | 152 pass, 0 fail, 0 skip · 2026-10-17 21:01Z · fb83f22f | 152 pass, 0 fail, 0 skip · 2026-10-17 21:20Z · fb83f22f |
| Db2 | 152 pass, 0 fail, 0 skip · 2026-10-17 20:45Z · fb83f22f | 152 pass, 0 fail, 0 skip · 2026-10-17 21:05Z · fb83f22f |
| Informix | 152 pass, 0 fail, 0 skip · 2026-10-17 21:01Z · fb83f22f | 152 pass, 0 fail, 0 skip · 2026-10-17 21:20Z · fb83f22f |
| SybaseASE | 152 pass, 0 fail, 0 skip · 2026-10-17 20:55Z · fb83f22f | 152 pass, 0 fail, 0 skip · 2026-10-17 21:15Z · fb83f22f |
| Spanner | 152 pass, 0 fail, 0 skip · 2026-10-17 21:00Z · fb83f22f | 152 pass, 0 fail, 0 skip · 2026-10-17 21:20Z · fb83f22f |
| SingleStore | 159 pass, 0 fail, 0 skip · 2026-10-17 20:57Z · fb83f22f | 159 pass, 0 fail, 0 skip · 2026-10-17 21:17Z · fb83f22f |
| Snowflake | 146 pass, 0 fail, 0 skip · 2026-10-12 14:58Z · 0d031db1 | 146 pass, 0 fail, 0 skip · 2026-10-12 15:45Z · 0d031db1 |
| SapHana | 147 pass, 0 fail, 0 skip · 2026-10-17 11:16Z · 5f813c36+ | 147 pass, 0 fail, 0 skip · 2026-10-17 11:38Z · 5f813c36+ |
| InterBase | 147 pass, 0 fail, 0 skip · 2026-10-17 11:33Z · 5f813c36+ | 147 pass, 0 fail, 0 skip · 2026-10-17 11:55Z · 5f813c36+ |
| Access | 144 pass, 0 fail, 0 skip · 2026-10-12 19:05Z · e0137a99+ | 144 pass, 0 fail, 0 skip · 2026-10-12 19:07Z · e0137a99+ |
| AuroraPostgreSql | 144 pass, 0 fail, 0 skip · 2026-10-12 15:21Z · 0d031db1 | 144 pass, 0 fail, 0 skip · 2026-10-13 19:30Z · 6a127448+ |
| AuroraMySql | 144 pass, 0 fail, 0 skip · 2026-10-12 15:25Z · 0d031db1 | 144 pass, 0 fail, 0 skip · 2026-10-13 19:35Z · 6a127448+ |

Failing now: none.
