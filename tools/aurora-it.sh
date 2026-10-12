#!/usr/bin/env bash
# Provision, and tear down, the AWS resources the Aurora integration tests run against.
#
#   tools/aurora-it.sh up      create a VPC, security group (this machine's IP only), an Aurora PostgreSQL and an
#                              Aurora MySQL Serverless v2 cluster; write the AURORA_* variables to the env file
#   tools/aurora-it.sh down    delete everything `up` created (instances, clusters, network) and list what is left
#   tools/aurora-it.sh env     print the path of the env file (source it to get the AURORA_* variables)
#
# run-integration-tests.sh calls `up` before the Aurora databases and `down` when the run ends, whatever the
# outcome (INCLUDE_AURORA=true, and no AURORA_*_HOST already set). Everything created carries the tag
# pengdows-aurora-it=<run id>, and every id is written to the state file as soon as it exists, so `down` can
# remove a partly created run. Needs the AWS CLI signed in (aws login) to an account that may create VPCs and RDS.
set -uo pipefail

REGION="${AURORA_IT_REGION:-us-east-1}"
STATE_DIR="${AURORA_IT_STATE_DIR:-${TMPDIR:-/tmp}/pengdows-aurora-it}"
STATE="${STATE_DIR}/state.env"
ENVFILE="${STATE_DIR}/aurora.env"

aws_() { aws --region "${REGION}" "$@"; }
save() { echo "$1=$2" >> "${STATE}"; }
log() { echo "[aurora-it] $*" >&2; }

up() {
  command -v aws >/dev/null || { log "the AWS CLI is not installed"; return 1; }
  aws_ sts get-caller-identity >/dev/null 2>&1 || { log "the AWS CLI is not signed in (run: aws login)"; return 1; }
  mkdir -p "${STATE_DIR}"; chmod 700 "${STATE_DIR}"
  if [[ -s "${STATE}" ]]; then log "a run is already recorded in ${STATE}; run '$0 down' first"; return 1; fi
  : > "${STATE}"; chmod 600 "${STATE}"

  local run="aurora-it-$(date +%Y%m%d-%H%M%S)" tag ip pgpass mypass
  save RUN "${run}"; tag="Key=pengdows-aurora-it,Value=${run}"
  ip="$(curl -fsS https://checkip.amazonaws.com | tr -d '[:space:]')" || { log "could not find this machine's public IP"; return 1; }
  pgpass="$(openssl rand -hex 16)"; mypass="$(openssl rand -hex 16)"

  local vpc igw rtb s1 s2 sg
  vpc="$(aws_ ec2 create-vpc --cidr-block 10.77.0.0/16 --tag-specifications "ResourceType=vpc,Tags=[{${tag}}]" --query Vpc.VpcId --output text)" || return 1
  save VPC "${vpc}"
  aws_ ec2 modify-vpc-attribute --vpc-id "${vpc}" --enable-dns-hostnames || return 1
  igw="$(aws_ ec2 create-internet-gateway --tag-specifications "ResourceType=internet-gateway,Tags=[{${tag}}]" --query InternetGateway.InternetGatewayId --output text)" || return 1
  save IGW "${igw}"
  aws_ ec2 attach-internet-gateway --internet-gateway-id "${igw}" --vpc-id "${vpc}" || return 1
  rtb="$(aws_ ec2 create-route-table --vpc-id "${vpc}" --tag-specifications "ResourceType=route-table,Tags=[{${tag}}]" --query RouteTable.RouteTableId --output text)" || return 1
  save RTB "${rtb}"
  aws_ ec2 create-route --route-table-id "${rtb}" --destination-cidr-block 0.0.0.0/0 --gateway-id "${igw}" >/dev/null || return 1
  local n=0 az sn
  for az in "${REGION}a" "${REGION}b"; do
    n=$((n + 1))
    sn="$(aws_ ec2 create-subnet --vpc-id "${vpc}" --cidr-block "10.77.${n}.0/24" --availability-zone "${az}" --tag-specifications "ResourceType=subnet,Tags=[{${tag}}]" --query Subnet.SubnetId --output text)" || return 1
    save "SUBNET${n}" "${sn}"
    aws_ ec2 associate-route-table --route-table-id "${rtb}" --subnet-id "${sn}" >/dev/null || return 1
  done
  s1="$(grep '^SUBNET1=' "${STATE}" | cut -d= -f2)"; s2="$(grep '^SUBNET2=' "${STATE}" | cut -d= -f2)"
  sg="$(aws_ ec2 create-security-group --group-name "${run}" --description "pengdows aurora integration tests, one machine only" --vpc-id "${vpc}" --tag-specifications "ResourceType=security-group,Tags=[{${tag}}]" --query GroupId --output text)" || return 1
  save SG "${sg}"
  local port
  for port in 5432 3306; do
    aws_ ec2 authorize-security-group-ingress --group-id "${sg}" --protocol tcp --port "${port}" --cidr "${ip}/32" >/dev/null || return 1
  done
  aws_ rds create-db-subnet-group --db-subnet-group-name "${run}" --db-subnet-group-description "pengdows aurora it" --subnet-ids "${s1}" "${s2}" --tags "${tag}" >/dev/null || return 1
  save SUBNETGROUP "${run}"

  # Serverless v2 scaling to 0 ACU idles at no compute charge; no final snapshot, one day of backups, no deletion protection.
  create_cluster() { # suffix engine user password
    aws_ rds create-db-cluster --db-cluster-identifier "${run}-$1" --engine "$2" --master-username "$3" --master-user-password "$4" \
      --db-subnet-group-name "${run}" --vpc-security-group-ids "${sg}" --database-name app --backup-retention-period 1 \
      --serverless-v2-scaling-configuration MinCapacity=0,MaxCapacity=2 --no-deletion-protection --tags "${tag}" >/dev/null || return 1
    save "CLUSTER_$1" "${run}-$1"
    aws_ rds create-db-instance --db-instance-identifier "${run}-$1-1" --db-cluster-identifier "${run}-$1" --db-instance-class db.serverless \
      --engine "$2" --publicly-accessible --no-auto-minor-version-upgrade --tags "${tag}" >/dev/null || return 1
    save "INSTANCE_$1" "${run}-$1-1"
  }
  create_cluster pg aurora-postgresql pgadmin "${pgpass}" || return 1
  create_cluster my aurora-mysql myadmin "${mypass}" || return 1

  log "waiting for the clusters (about 8 minutes)"
  aws_ rds wait db-instance-available --db-instance-identifier "${run}-pg-1" || return 1
  aws_ rds wait db-instance-available --db-instance-identifier "${run}-my-1" || return 1
  local pgh myh
  pgh="$(aws_ rds describe-db-clusters --db-cluster-identifier "${run}-pg" --query 'DBClusters[0].Endpoint' --output text)"
  myh="$(aws_ rds describe-db-clusters --db-cluster-identifier "${run}-my" --query 'DBClusters[0].Endpoint' --output text)"
  ( umask 077; cat > "${ENVFILE}" <<EOF
export AURORA_POSTGRES_HOST='${pgh}' AURORA_POSTGRES_USER='pgadmin' AURORA_POSTGRES_PASSWORD='${pgpass}' AURORA_POSTGRES_DATABASE='app' AURORA_POSTGRES_SSL_MODE='Require'
export AURORA_MYSQL_HOST='${myh}' AURORA_MYSQL_USER='myadmin' AURORA_MYSQL_PASSWORD='${mypass}' AURORA_MYSQL_DATABASE='app' AURORA_MYSQL_SSL_MODE='Required'
EOF
  )
  log "ready: ${run}"
}

down() {
  [[ -s "${STATE}" ]] || { log "nothing recorded in ${STATE}; nothing to delete"; return 0; }
  # shellcheck disable=SC1090
  source "${STATE}"
  local e id
  for e in pg my; do
    id="INSTANCE_${e}"; [[ -n "${!id:-}" ]] &&
      aws_ rds delete-db-instance --db-instance-identifier "${!id}" --skip-final-snapshot --delete-automated-backups >/dev/null 2>&1
  done
  for e in pg my; do
    id="INSTANCE_${e}"; [[ -n "${!id:-}" ]] && aws_ rds wait db-instance-deleted --db-instance-identifier "${!id}" 2>/dev/null
  done
  for e in pg my; do
    id="CLUSTER_${e}"; [[ -n "${!id:-}" ]] &&
      aws_ rds delete-db-cluster --db-cluster-identifier "${!id}" --skip-final-snapshot --delete-automated-backups >/dev/null 2>&1
  done
  for e in pg my; do
    id="CLUSTER_${e}"; [[ -n "${!id:-}" ]] && aws_ rds wait db-cluster-deleted --db-cluster-identifier "${!id}" 2>/dev/null
  done
  [[ -n "${SUBNETGROUP:-}" ]] && aws_ rds delete-db-subnet-group --db-subnet-group-name "${SUBNETGROUP}" 2>/dev/null
  [[ -n "${SG:-}" ]] && aws_ ec2 delete-security-group --group-id "${SG}" 2>/dev/null
  if [[ -n "${RTB:-}" ]]; then
    local a
    for a in $(aws_ ec2 describe-route-tables --route-table-ids "${RTB}" --query 'RouteTables[0].Associations[].RouteTableAssociationId' --output text 2>/dev/null); do
      aws_ ec2 disassociate-route-table --association-id "${a}" 2>/dev/null
    done
    aws_ ec2 delete-route-table --route-table-id "${RTB}" 2>/dev/null
  fi
  [[ -n "${SUBNET1:-}" ]] && aws_ ec2 delete-subnet --subnet-id "${SUBNET1}" 2>/dev/null
  [[ -n "${SUBNET2:-}" ]] && aws_ ec2 delete-subnet --subnet-id "${SUBNET2}" 2>/dev/null
  if [[ -n "${IGW:-}" && -n "${VPC:-}" ]]; then
    aws_ ec2 detach-internet-gateway --internet-gateway-id "${IGW}" --vpc-id "${VPC}" 2>/dev/null
    aws_ ec2 delete-internet-gateway --internet-gateway-id "${IGW}" 2>/dev/null
  fi
  [[ -n "${VPC:-}" ]] && aws_ ec2 delete-vpc --vpc-id "${VPC}" 2>/dev/null

  # Prove it: anything still carrying this run's tag, or any cluster/instance/snapshot of its name, is reported.
  local left
  left="$(aws_ resourcegroupstaggingapi get-resources --tag-filters "Key=pengdows-aurora-it,Values=${RUN}" \
      --query 'ResourceTagMappingList[].ResourceARN' --output text 2>/dev/null)"
  left="${left} $(aws_ rds describe-db-clusters --query "DBClusters[?starts_with(DBClusterIdentifier,'${RUN}')].DBClusterIdentifier" --output text 2>/dev/null)"
  left="${left} $(aws_ rds describe-db-cluster-snapshots --query "DBClusterSnapshots[?starts_with(DBClusterIdentifier,'${RUN}')].DBClusterSnapshotIdentifier" --output text 2>/dev/null)"
  left="$(echo "${left}" | xargs)"
  if [[ -n "${left}" ]]; then
    log "STILL PRESENT (may be billing): ${left}"
    return 1
  fi
  rm -f "${STATE}" "${ENVFILE}"
  log "torn down: nothing tagged ${RUN} remains"
}

case "${1:-}" in
  up) up; rc=$?; (( rc != 0 )) && log "up failed; run '$0 down' to remove what was created"; exit "${rc}" ;;
  down) down ;;
  env) echo "${ENVFILE}" ;;
  *) echo "usage: $0 up|down|env" >&2; exit 2 ;;
esac
