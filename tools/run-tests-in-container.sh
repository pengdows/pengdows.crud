#!/usr/bin/env bash
set -euo pipefail

IMAGE="mcr.microsoft.com/dotnet/sdk:8.0"
WORKDIR="/workspace"
DOTNET_CMD=("dotnet" "test" "-c" "Release" "--results-directory" "TestResults" "--logger" "trx")

print_usage() {
    cat <<'USAGE'
Usage: tools/run-tests-in-container.sh [--image <docker-image>] [--] [dotnet args...]

Runs the dotnet test suite inside a disposable Docker container using the specified
SDK image (default: mcr.microsoft.com/dotnet/sdk:8.0). Additional arguments after
"--" are forwarded to dotnet.
USAGE
}

while [[ $# -gt 0 ]]; do
    case "$1" in
        -h|--help)
            print_usage
            exit 0
            ;;
        --image)
            if [[ $# -lt 2 ]]; then
                echo "error: --image requires a value" >&2
                exit 1
            fi
            IMAGE="$2"
            shift 2
            ;;
        --)
            shift
            DOTNET_CMD=("dotnet" "$@")
            break
            ;;
        *)
            echo "error: unknown argument: $1" >&2
            print_usage >&2
            exit 1
            ;;
    esac
done

if ! command -v docker >/dev/null 2>&1; then
    echo "error: docker is required to run this script" >&2
    exit 1
fi

docker run --rm \
    -v "${PWD}:${WORKDIR}" \
    -v "/var/run/docker.sock:/var/run/docker.sock" \
    -w "${WORKDIR}" \
    --env "DOTNET_CLI_TELEMETRY_OPTOUT=1" \
    --env "DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1" \
    ${TESTBED_ONLY:+--env "TESTBED_ONLY=${TESTBED_ONLY}"} \
    "$IMAGE" \
    "${DOTNET_CMD[@]}"
