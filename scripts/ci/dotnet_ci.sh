#!/usr/bin/env bash
set -euo pipefail

repo_root="$(git rev-parse --show-toplevel)"
solution="$repo_root/dotnet/Ocb.slnx"
inventory="$repo_root/dotnet/contracts/migration-inventory.json"
generated_inventory="$repo_root/dotnet/contracts/migration-inventory.generated.json"
test_results="$repo_root/scripts/.dependencies/dotnet/test-results"
configuration="Release"

python_cmd=""
if command -v python3 >/dev/null 2>&1 && python3 -c 'import sys' >/dev/null 2>&1; then
  python_cmd="python3"
elif command -v python >/dev/null 2>&1 && python -c 'import sys' >/dev/null 2>&1; then
  python_cmd="python"
else
  echo "error: python3 or python is required" >&2
  exit 1
fi

while [[ "$#" -gt 0 ]]; do
  case "$1" in
    --configuration)
      if [[ "$#" -lt 2 ]]; then
        echo "error: --configuration requires a value" >&2
        exit 2
      fi
      configuration="$2"
      shift 2
      ;;
    *)
      echo "unknown argument: $1" >&2
      exit 2
      ;;
  esac
done

cleanup() {
  rm -f "$generated_inventory"
  rm -rf "$test_results"
}
trap cleanup EXIT

rm -rf "$test_results"
mkdir -p "$test_results"

echo "[dotnet-ci] restore"
dotnet restore "$solution" --locked-mode

echo "[dotnet-ci] format"
dotnet format "$solution" --verify-no-changes --no-restore

echo "[dotnet-ci] build ($configuration)"
dotnet build "$solution" --configuration "$configuration" --no-restore

echo "[dotnet-ci] test ($configuration)"
dotnet test "$solution" --configuration "$configuration" --no-build \
  --results-directory "$test_results" --collect:"XPlat Code Coverage"

echo "[dotnet-ci] verify migration inventory"
"$python_cmd" "$repo_root/scripts/dotnet/export_contract_inventory.py" --output "$generated_inventory"
diff -u "$inventory" "$generated_inventory"

echo "[dotnet-ci] parity corpus checks"
"$python_cmd" -m pytest "$repo_root/scripts/dotnet/tests" -v