#!/usr/bin/env bash
# Verifies that skills/claude, skills/codex, and skills/gemini carry identical technical
# content for the pengdows-crud skill. Directory layout is allowed to differ — skills/claude
# keeps reference files flat, skills/codex and skills/gemini nest them under references/ — but
# the file content itself must match exactly. Run from the repo root.
set -euo pipefail

CLAUDE_DIR="skills/claude/pengdows-crud"
CODEX_DIR="skills/codex/pengdows-crud"
GEMINI_DIR="skills/gemini/pengdows-crud"

fail=0

compare_tree() {
  local other_dir="$1"
  local other_name="$2"

  if ! diff -q "$CLAUDE_DIR/SKILL.md" "$other_dir/SKILL.md" > /dev/null 2>&1; then
    echo "DRIFT: $CLAUDE_DIR/SKILL.md differs from $other_dir/SKILL.md"
    fail=1
  fi

  for f in "$CLAUDE_DIR"/*.md; do
    local name
    name=$(basename "$f")
    [ "$name" = "SKILL.md" ] && continue

    local other_file="$other_dir/references/$name"
    if [ ! -f "$other_file" ]; then
      echo "DRIFT: $f has no counterpart at $other_file"
      fail=1
      continue
    fi

    if ! diff -q "$f" "$other_file" > /dev/null 2>&1; then
      echo "DRIFT: $f differs from $other_file"
      fail=1
    fi
  done

  # Also catch a reference file added to $other_dir/references but never added to claude/.
  for f in "$other_dir"/references/*.md; do
    local name
    name=$(basename "$f")
    if [ ! -f "$CLAUDE_DIR/$name" ]; then
      echo "DRIFT: $f has no counterpart at $CLAUDE_DIR/$name"
      fail=1
    fi
  done
}

compare_tree "$CODEX_DIR" "codex"
compare_tree "$GEMINI_DIR" "gemini"

if [ "$fail" -ne 0 ]; then
  echo ""
  echo "skills/claude, skills/codex, and skills/gemini have diverged in content."
  echo "Directory layout may legitimately differ (claude/ is flat, codex/ and gemini/ nest"
  echo "reference files under references/), but the technical content must be identical."
  echo "Apply the same edit to all three trees, or make one authoritative and copy from it."
  exit 1
fi

echo "Skill trees are in sync."
