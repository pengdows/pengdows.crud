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
| Sqlite | 157 pass, 0 fail, 0 skip · 2026-11-21 02:17Z · 9e81cc53+ | 157 pass, 0 fail, 0 skip · 2026-11-21 02:38Z · 9e81cc53+ |
| PostgreSql | 173 pass, 0 fail, 0 skip · 2026-11-21 02:16Z · 9e81cc53+ | 173 pass, 0 fail, 0 skip · 2026-11-21 02:38Z · 9e81cc53+ |
| SqlServer | 160 pass, 0 fail, 0 skip · 2026-11-21 02:11Z · 9e81cc53+ | 160 pass, 0 fail, 0 skip · 2026-11-21 02:32Z · 9e81cc53+ |
| MySql | 163 pass, 0 fail, 0 skip · 2026-11-21 02:15Z · 9e81cc53+ | 163 pass, 0 fail, 0 skip · 2026-11-21 02:37Z · 9e81cc53+ |
| MariaDb | 163 pass, 0 fail, 0 skip · 2026-11-21 02:16Z · 9e81cc53+ | 163 pass, 0 fail, 0 skip · 2026-11-21 02:37Z · 9e81cc53+ |
| Firebird | 156 pass, 0 fail, 0 skip · 2026-11-21 02:07Z · 9e81cc53+ | 156 pass, 0 fail, 0 skip · 2026-11-21 02:29Z · 9e81cc53+ |
| CockroachDb | 169 pass, 0 fail, 0 skip · 2026-11-21 02:12Z · 9e81cc53+ | 169 pass, 0 fail, 0 skip · 2026-11-21 02:33Z · 9e81cc53+ |
| DuckDB | 156 pass, 0 fail, 0 skip · 2026-11-21 02:15Z · 9e81cc53+ | 156 pass, 0 fail, 0 skip · 2026-11-21 02:37Z · 9e81cc53+ |
| Oracle | 158 pass, 0 fail, 0 skip · 2026-11-21 02:13Z · 9e81cc53+ | 158 pass, 0 fail, 0 skip · 2026-11-21 02:35Z · 9e81cc53+ |
| YugabyteDb | 173 pass, 0 fail, 0 skip · 2026-11-21 02:09Z · 9e81cc53+ | 173 pass, 0 fail, 0 skip · 2026-11-21 02:31Z · 9e81cc53+ |
| TiDb | 163 pass, 0 fail, 0 skip · 2026-11-21 02:17Z · 9e81cc53+ | 163 pass, 0 fail, 0 skip · 2026-11-21 02:39Z · 9e81cc53+ |
| FlatFile | 156 pass, 0 fail, 0 skip · 2026-11-21 02:21Z · 9e81cc53+ | 156 pass, 0 fail, 0 skip · 2026-11-21 02:42Z · 9e81cc53+ |
| Db2 | 156 pass, 0 fail, 0 skip · 2026-11-21 02:05Z · 9e81cc53+ | 156 pass, 0 fail, 0 skip · 2026-11-21 02:27Z · 9e81cc53+ |
| Informix | 156 pass, 0 fail, 0 skip · 2026-11-21 02:17Z · 9e81cc53+ | 156 pass, 0 fail, 0 skip · 2026-11-21 02:39Z · 9e81cc53+ |
| SybaseASE | 156 pass, 0 fail, 0 skip · 2026-11-21 02:12Z · 9e81cc53+ | 156 pass, 0 fail, 0 skip · 2026-11-21 02:34Z · 9e81cc53+ |
| Spanner | 156 pass, 0 fail, 0 skip · 2026-11-21 02:21Z · 9e81cc53+ | 156 pass, 0 fail, 0 skip · 2026-11-21 02:43Z · 9e81cc53+ |
| SingleStore | 163 pass, 0 fail, 0 skip · 2026-11-21 02:14Z · 9e81cc53+ | 163 pass, 0 fail, 0 skip · 2026-11-21 02:35Z · 9e81cc53+ |
| Snowflake | 156 pass, 0 fail, 0 skip · 2026-11-13 16:19Z · e11ef215 | 156 pass, 0 fail, 0 skip · 2026-11-13 16:43Z · e11ef215 |
| SapHana | 156 pass, 0 fail, 0 skip · 2026-11-21 02:02Z · 9e81cc53+ | 156 pass, 0 fail, 0 skip · 2026-11-21 02:24Z · 9e81cc53+ |
| InterBase | 156 pass, 0 fail, 0 skip · 2026-11-21 02:20Z · 9e81cc53+ | 156 pass, 0 fail, 0 skip · 2026-11-21 02:42Z · 9e81cc53+ |
| Access | 154 pass, 0 fail, 0 skip · 2026-11-21 20:22Z · 141212e8+ | 154 pass, 0 fail, 0 skip · 2026-11-21 20:24Z · 141212e8+ |
| AuroraPostgreSql | 174 pass, 0 fail, 0 skip · 2026-11-13 16:22Z · e11ef215 | 174 pass, 0 fail, 0 skip · 2026-11-13 16:47Z · e11ef215 |
| AuroraMySql | 164 pass, 0 fail, 0 skip · 2026-11-13 16:12Z · e11ef215 | 164 pass, 0 fail, 0 skip · 2026-11-13 16:37Z · e11ef215 |

Failing now: none.
