#!/bin/bash
cd "$(dirname "$0")"/
for db in PostgreSql SqlServer MySql MariaDb CockroachDb YugabyteDb TiDb SingleStore DuckDB Firebird Oracle SybaseASE Spanner FlatFile; do
  echo "START $db $(date +%T)"
  timeout 1500 dotnet bin/Release/net10.0/datatype-probe.dll $db results/$db.jsonl 2>&1 | tail -3
  echo "END $db rc=$? $(date +%T)"
done
echo ALLDONE
