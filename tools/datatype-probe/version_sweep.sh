#!/bin/bash
# Probes every older image in the testbed's version matrix (ParallelTestOrchestrator.cs, TestbedImageMatrix),
# one output file per image: results-versions/<Database>__<tag>.jsonl. The default (latest) image is in results/.
cd "$(dirname "$0")"
mkdir -p results-versions
run() { # db tag image
  echo "START $1 $2 $(date +%T)"
  timeout 1500 dotnet bin/Release/net10.0/datatype-probe.dll "$1" "results-versions/$1__$2.jsonl" "$3" 2>&1 | tail -2
  echo "END $1 $2 $(date +%T)"
}
run PostgreSql 15.0 postgres:15.0
run PostgreSql 9.5 postgres:9.5-alpine
run MySql 8.0.36 mysql:8.0.36
run MySql 5.7 mysql:5.7
run MariaDb 11.10 mariadb:11.4.12
run MariaDb 10.11.11 mariadb:10.11.11
run MariaDb 10.4 mariadb:10.4
run MariaDb 10.2 mariadb:10.2
run SqlServer 2022 mcr.microsoft.com/mssql/server:2022-CU25-GDR2-ubuntu-22.04
run SqlServer 2019 mcr.microsoft.com/mssql/server:2019-latest
run SqlServer 2017 mcr.microsoft.com/mssql/server:2017-latest
run CockroachDb v24.3.0 cockroachdb/cockroach:v24.3.0
run CockroachDb v23.2.14 cockroachdb/cockroach:v23.2.14
run Firebird 4.0.5 firebirdsql/firebird:4.0.5
run Firebird 3.0.9 firebirdsql/firebird:3.0.9
run TiDb v7.5.7 pingcap/tidb:v7.5.7
run YugabyteDb 2.25.2.0-b359 yugabytedb/yugabyte:2.25.2.0-b359
run Oracle 21c gvenzl/oracle-xe:21-slim-faststart
run Oracle 18c gvenzl/oracle-xe:18.4.0-slim-faststart
echo SWEEPDONE
