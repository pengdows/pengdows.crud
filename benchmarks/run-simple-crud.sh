#!/bin/bash
# Run simple CRUD benchmarks comparing pengdows.crud vs Dapper vs Entity Framework
# Uses SQLite in-memory, no Docker required

root="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

echo "======================================================================="
echo "Simple CRUD Benchmarks: pengdows.crud vs Dapper vs Entity Framework"
echo "======================================================================="
echo ""
echo "Running benchmarks with SQLite (in-memory)..."
echo "This will test:"
echo "  - Create (single & batch)"
echo "  - Read (single & list)"
echo "  - Update (single)"
echo "  - Delete (single)"
echo ""
echo "Note: First run will be slower due to JIT compilation warmup."
echo ""

dotnet run -c Release --project "${root}/CrudBenchmarks" --filter "*SimpleCrud*"
