#!/usr/bin/env bash
# AB#6683 / AB#6675: audit the dependency ranges of every CK producer repository for ranges that the
# one-major-per-range rule of range retention rejects (the rule the compiler enforces; see
# docs/ck-semver-rules.md, "Range audit").
#
# Usage:  scripts/audit-ck-ranges.sh <monorepo-root> [path-to-octo-ckc.dll|octo-ckc]
#
# <monorepo-root> holds one directory per repository (octo-construction-kit, octo-adapter-loxone, ...). Every
# repository directory that contains a ckModel.yaml is audited; worktrees (wt*, wt-*) are skipped. The script only reads
# files. It prints the rejected declarations per repository and a summary; exit code 1 when any range is rejected.
# Run it after `git fetch` + checkout of origin/main in every repository: the audit sees the checked-out files.
set -u
ROOT="${1:?usage: audit-ck-ranges.sh <monorepo-root> [octo-ckc]}"
CKC="${2:-octo-ckc}"
case "$CKC" in *.dll) RUN=(dotnet "$CKC") ;; *) RUN=("$CKC") ;; esac

total=0; rejected_repos=()
for dir in "$ROOT"/*/; do
  name="$(basename "$dir")"
  case "$name" in wt|wt-*) continue ;; esac
  # only repositories that contain a ckModel.yaml (not below bin/obj/node_modules)
  if ! find "$dir" \( -name bin -o -name obj -o -name node_modules -o -name .git \) -prune -o -name ckModel.yaml -print -quit | grep -q .; then
    continue
  fi
  total=$((total + 1))
  out="$("${RUN[@]}" -c AuditRanges -p "$dir" 2>&1)"; rc=$?
  echo "== $name"
  echo "$out" | grep -E "REJECTED" || true
  [ $rc -ne 0 ] && rejected_repos+=("$name")
done
echo
echo "$total repositories with CK sources audited; ${#rejected_repos[@]} with rejected ranges${rejected_repos:+: ${rejected_repos[*]}}"
[ ${#rejected_repos[@]} -eq 0 ]
