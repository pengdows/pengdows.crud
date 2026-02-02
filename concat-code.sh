#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
output_file="/tmp/consolidated-code.txt"

declare -a source_dirs=(
  "pengdows.crud"
  "pengdows.crud.abstractions"
  "testbed"
)

declare -a code_exts=(cs sql csproj props targets config)

rm -f "${output_file}"

echo "Concatenating code into ${output_file}"

for dir in "${source_dirs[@]}"; do
  full_dir="${root}/${dir}"
  if [[ ! -d "${full_dir}" ]]; then
    echo "Warning: ${full_dir} not found" >&2
    continue
  fi

  printf "### Directory: %s\n\n" "${dir}" >> "${output_file}"

  mapfile -t files < <(find "${full_dir}" -type f | sort)
  for file in "${files[@]}"; do
    extension="${file##*.}"
    match=false
    for ext in "${code_exts[@]}"; do
      if [[ "${extension,,}" == "${ext}" ]]; then
        match=true
        break
      fi
    done
    if [[ "${match}" != true ]]; then
      continue
    fi

    printf "===== %s =====\n" "${file}" >> "${output_file}"
    cat "${file}" >> "${output_file}"
    printf "\n\n" >> "${output_file}"
  done

done

echo "Done. Combined file is ${output_file}"
