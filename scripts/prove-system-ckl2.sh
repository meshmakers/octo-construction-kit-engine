#!/usr/bin/env bash
# AB#6928 / Epic AB#5584 F4.1: proof that raising the System model to ckLanguage 2 (System 2.6.0) is a MINOR for its dependents.
#
# What it shows, offline and in PRIVATE catalogs under --work (octo-ckc runs with a throw-away HOME whose settings switch both GitHub
# catalogs off and every call gets -lcr <private catalog>; nothing shared is read or written, nothing is published anywhere):
#   1. System BASE (git ref, default origin/main = ckLanguage 1) and every dependent CK model of the monorepo are compiled into the "before" catalog.
#   2. The System of the working tree (candidate) is checked: ValidateVersion against the baseline (expected: not Major), ValidateCascade
#      (expected: every dependent Compatible, 0 Breaks, 0 NeedsRepin), minEngineVersion / ckLanguage of the compiled model, the catalog path
#      (ck-models/v3 only), the derivable value of every type and record, and the SHA-256 list of the compiled System before and after.
#   3. The candidate is published into a COPY of the catalog and every dependent is recompiled WITHOUT editing its ckModel.yaml; the output is
#      compared with the "before" build (identical, or equivalent = only the resolved dependency closure line moved, SemVer row D8).
# Exit code != 0 when a claim is not met (a FINDING is never hidden). Rerunnable: the work directory is recreated.
#
# Usage: scripts/prove-system-ckl2.sh --monorepo DIR [--work DIR] [--ckc DLL] [--base-ref REF]
set -u -o pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO="$(cd "$SCRIPT_DIR/.." && pwd)"
LIB="$SCRIPT_DIR/ab6676/proof_lib.py"
MONO="${OCTO_MONOREPO:-}"
WORK="${TMPDIR:-/tmp}/ab6928-proof"
CKC="$REPO/bin/DebugL/net10.0/octo-ckc.dll"
BASE_REF="origin/main"
while [ $# -gt 0 ]; do
  case "$1" in
    --monorepo) MONO="$2"; shift 2;;
    --work) WORK="$2"; shift 2;;
    --ckc) CKC="$2"; shift 2;;
    --base-ref) BASE_REF="$2"; shift 2;;
    -h|--help) sed -n '2,16p' "$0"; exit 0;;
    *) echo "unknown option $1"; exit 64;;
  esac
done
[ -n "$MONO" ] || { echo "--monorepo (or OCTO_MONOREPO) is required"; exit 64; }
[ -f "$CKC" ] || { echo "ckc not found: $CKC (build the engine solution in DebugL first)"; exit 66; }
rm -rf "$WORK"; mkdir -p "$WORK/home/.octo-ckc" "$WORK/src" "$WORK/out"; WORK="$(cd "$WORK" && pwd)"
cat > "$WORK/home/.octo-ckc/settings.json" <<'JSON'
{
  "LocalFileSystemCatalog": { "IsEnabled": true, "CacheFileName": "local-catalog-cache.json" },
  "PublicOctoGitHub": { "IsEnabled": false },
  "PrivateOctoGitHub": { "IsEnabled": false }
}
JSON
REPORT="$WORK/report.md"; : > "$REPORT"
FAILS=0
say() { echo "$*" | tee -a "$REPORT"; }
finding() { FAILS=$((FAILS + 1)); say "FINDING: $*"; }
ckc() { HOME="$WORK/home" dotnet "$CKC" "$@"; }
json_get() { python3 -c 'import json,sys; d=json.loads(sys.argv[1]); print(d.get(sys.argv[2],""))' "$1" "$2"; }

# the CK models that depend on System (same list as scripts/prove-additive-system-releases.sh): <repo>/<path of the project>
SOURCES=(
  octo-construction-kit-engine/src/SystemFilesCkModel
  octo-construction-kit-engine/src/StreamDataCkModel
  octo-bot-services/src/SystemBotCkModel
  octo-communication-controller-services/src/SystemCommunicationCkModel
  octo-identity-services/src/Persistence.IdentityCkModel
  octo-common-services/src/Notifications
  octo-platform-services/src/SystemUiCkModel
  octo-report-services/src/ReportingCkModel
  octo-ai-services/src/SystemAiCkModel
  octo-construction-kit/src/ConstructionKits/Octo.Sdk.Packages.Basic
  octo-construction-kit/src/ConstructionKits/Octo.Sdk.Packages.Basic.Accounting
  octo-construction-kit/src/ConstructionKits/Octo.Sdk.Packages.Basic.Energy
  octo-construction-kit/src/ConstructionKits/Octo.Sdk.Packages.EnergyCommunity
  octo-construction-kit/src/ConstructionKits/Octo.Sdk.Packages.Environment
  octo-construction-kit/src/ConstructionKits/Octo.Sdk.Packages.Industry.Basic
  octo-construction-kit/src/ConstructionKits/Octo.Sdk.Packages.Industry.Energy
  octo-construction-kit/src/ConstructionKits/Octo.Sdk.Packages.Industry.Fluid
  octo-construction-kit/src/ConstructionKits/Octo.Sdk.Packages.Industry.Logistics
  octo-construction-kit/src/ConstructionKits/Octo.Sdk.Packages.Industry.Maintenance
  octo-construction-kit/src/ConstructionKits/Octo.Sdk.Packages.Industry.Manufacturing
  octo-construction-kit/src/ConstructionKits/Octo.Sdk.Demo
)

# sources are taken from the committed HEAD of each repository (git archive), never from a working tree
extract() { # <repo> <path> <dest>
  mkdir -p "$3"; git -C "$MONO/$1" archive HEAD -- "$2/ConstructionKit" | tar -x -C "$3" || return 1
}
compile_publish() { # <catalog> <out-dir> <source dir containing ConstructionKit>; prints the compiled file
  local cat="$1" out="$2" src="$3" name; name="$(basename "$src")"
  rm -rf "$out/$name"; mkdir -p "$out/$name"
  ckc -c Compile -p "$src/ConstructionKit" -o "$out/$name" -lce true -lcr "$cat" > "$out/$name.compile.log" 2>&1 || return 1
  local f; f="$(ls "$out/$name"/ck-*.yaml 2>/dev/null | head -1)"; [ -n "$f" ] || return 1
  ckc -c Publish -f "$f" -c LocalFileSystemCatalog -lcr "$cat" > "$out/$name.publish.log" 2>&1 || return 1
  echo "$f"
}

say "## AB#6928 proof: System ckLanguage 2 as a MINOR (work: $WORK)"
say "engine: $(git -C "$REPO" rev-parse --short HEAD) (+ working tree), base ref for System: $BASE_REF ($(git -C "$REPO" rev-parse --short "$BASE_REF"))"

# ---------------------------------------------------------------- sources
mkdir -p "$WORK/src/base"
git -C "$REPO" archive "$BASE_REF" -- src/SystemCkModel/ConstructionKit | tar -x -C "$WORK/src/base"
mv "$WORK/src/base/src/SystemCkModel" "$WORK/src/system-base"; rm -rf "$WORK/src/base"
mkdir -p "$WORK/src/system-candidate"; cp -R "$REPO/src/SystemCkModel/ConstructionKit" "$WORK/src/system-candidate/ConstructionKit"
BASE_ID="$(grep -m1 '^modelId' "$WORK/src/system-base/ConstructionKit/ckModel.yaml" | tr -d '"' | awk '{print $2}')"
CAND_ID="$(grep -m1 '^modelId' "$WORK/src/system-candidate/ConstructionKit/ckModel.yaml" | tr -d '"' | awk '{print $2}')"
say "System base: $BASE_ID, candidate: $CAND_ID"
NAMES=()
for s in "${SOURCES[@]}"; do
  repo="${s%%/*}"; rel="${s#*/}"; name="$(basename "$s")"
  if [ "$repo" = octo-construction-kit-engine ]; then
    mkdir -p "$WORK/src/dep/$name"; git -C "$REPO" archive "$BASE_REF" -- "$rel/ConstructionKit" | tar -x -C "$WORK/src/dep/$name"
    mv "$WORK/src/dep/$name/$rel/ConstructionKit" "$WORK/src/dep/$name/ConstructionKit"; rm -rf "$WORK/src/dep/$name/src"
  else
    extract "$repo" "$rel" "$WORK/src/dep/$name.x" || { finding "cannot extract $s"; continue; }
    mkdir -p "$WORK/src/dep/$name"; mv "$WORK/src/dep/$name.x/$rel/ConstructionKit" "$WORK/src/dep/$name/ConstructionKit"; rm -rf "$WORK/src/dep/$name.x"
  fi
  NAMES+=("$name")
done
say "dependent sources taken from the committed HEAD of: $(for r in $(printf '%s\n' "${SOURCES[@]}" | cut -d/ -f1 | sort -u); do
  [ "$r" = octo-construction-kit-engine ] && echo -n "$r@$BASE_REF " || echo -n "$r@$(git -C "$MONO/$r" rev-parse --short HEAD) "; done)"

# ---------------------------------------------------------------- 1. before catalog
CAT="$WORK/catalog-before"; OUT="$WORK/out/before"; mkdir -p "$CAT" "$OUT"
compile_publish "$CAT" "$OUT" "$WORK/src/system-base" > /dev/null || { finding "System base does not compile"; cat "$OUT/system-base.compile.log"; exit 1; }
: > "$OUT/order.txt"
pending=("${NAMES[@]}"); progress=1
while [ ${#pending[@]} -gt 0 ] && [ $progress -eq 1 ]; do
  progress=0; still=()
  for n in "${pending[@]}"; do
    if compile_publish "$CAT" "$OUT" "$WORK/src/dep/$n" > /dev/null; then echo "$n" >> "$OUT/order.txt"; progress=1; else still+=("$n"); fi
  done
  pending=("${still[@]+"${still[@]}"}")
done
[ ${#pending[@]} -eq 0 ] || finding "dependents not compilable against the base System (see $OUT/<name>.compile.log): ${pending[*]}"
say "before catalog: System $BASE_ID + $(wc -l < "$OUT/order.txt" | tr -d ' ') dependents compiled and published"

# ---------------------------------------------------------------- 2. candidate checks
CO="$WORK/out/candidate"; mkdir -p "$CO"
cp -R "$CAT" "$WORK/vv"; ckc -c ValidateVersion -p "$WORK/src/system-candidate/ConstructionKit" -lce true -lcr "$WORK/vv" > "$CO/validate-version.txt" 2>&1
say; say "### ValidateVersion $CAND_ID against $BASE_ID"
grep -E "Required:|Result:|MAJOR |OCTO-CK" "$CO/validate-version.txt" | cut -c1-200 | sed 's/^/    /' | tee -a "$REPORT"
grep -q "OCTO-CK100\|OCTO-CK200" "$CO/validate-version.txt" && finding "ValidateVersion reports a Major (OCTO-CK100/200)"
grep -qi "valid" "$CO/validate-version.txt" || finding "ValidateVersion produced no verdict"

ckc -c ValidateCascade -p "$WORK/src/system-candidate/ConstructionKit" -lce true -lcr "$CAT" > "$CO/cascade.txt" 2>&1
C="$(python3 "$LIB" cascade-count "$CO/cascade.txt")"
say; say "### ValidateCascade: $C"
if [ "$(json_get "$C" parsed)" != True ]; then finding "ValidateCascade output not parsed ($CO/cascade.txt)"; else
  [ "$(json_get "$C" breaks)" = 0 ] && [ "$(json_get "$C" needsRepin)" = 0 ] || finding "ValidateCascade: Breaks or NeedsRepin present"
fi

# publish the candidate into a COPY of the before catalog
CAT2="$WORK/catalog-after"; cp -R "$CAT" "$CAT2"
f="$(compile_publish "$CAT2" "$CO" "$WORK/src/system-candidate")" || { finding "System candidate does not compile/publish"; exit 1; }
say; say "### Compiled System candidate"
python3 - "$f" <<'PY' | tee -a "$REPORT"; [ "${PIPESTATUS[0]}" = 0 ] || finding "derivable is not explicit Any on every type and record"
import re, sys
t = open(sys.argv[1], encoding="utf-8").read()
print("    ckLanguage:", (re.search(r"^ckLanguage:\s*(\d+)", t, re.M) or [None, "(absent = 1)"])[1])
print("    minEngineVersion:", (re.search(r"^minEngineVersion:\s*\"?([\d.]+)", t, re.M) or [None, "(absent)"])[1])
types = len(re.findall(r"^[- ] typeId:", t, re.M))
records = len(re.findall(r"^[- ] recordId:", t, re.M))
print(f"    types: {types}, records: {records}, derivable: Any: {len(re.findall(r'derivable: Any', t))}, derivable: Model: {len(re.findall(r'derivable: Model', t))} (every type and record must carry Any)")
if len(re.findall(r"derivable: Any", t)) != types + records or "derivable: Model" in t:
    sys.exit(3)
PY
cat_v2="$(find "$CAT2/ck-models/v2" -name 'ck-system-2.*.json' 2>/dev/null | sort | tail -1)"
cat_v3="$(find "$CAT2/ck-models/v3" -path '*/System/*' -name 'ck-system-2.*.json' 2>/dev/null | sort | tail -3 | tr '\n' ' ')"
say "    catalog: v3 = ${cat_v3##*/}; newest System file under v2: ${cat_v2##*/}"
echo "$cat_v2" | grep -q "2\.6\.0" && finding "System 2.6.0 was written to ck-models/v2 (must be v3 only)"
echo "$cat_v3" | grep -q "2\.6\.0" || finding "System 2.6.0 is missing under ck-models/v3"

# hash list and surface comparison, System before / after
say; say "### SHA-256 of the compiled System (before / after) and the surface diff"
bf="$(ls "$OUT/system-base"/ck-*.yaml | head -1)"
say "    before $(shasum -a 256 "$bf" | cut -c1-64)  $(basename "$bf")"
say "    after  $(shasum -a 256 "$f" | cut -c1-64)  $(basename "$f")"
# the public surface is identical when the version is normalised and the explicit modifiers / language keys are removed
norm() { sed -e 's/System-2\.[56]\.0/System-X/g' -e '/^ *derivable: Any$/d' -e '/^ckLanguage:/d' -e '/^minEngineVersion:/d' "$1"; }
if diff <(norm "$bf") <(norm "$f") > "$CO/surface.diff"; then
  say "    surface identical: after normalising the version and removing the explicit 'derivable: Any', ckLanguage and minEngineVersion lines the compiled System 2.5.0 and 2.6.0 are byte-identical"
else
  finding "compiled System surface differs beyond the explicit modifiers ($CO/surface.diff)"; head -20 "$CO/surface.diff" | sed 's/^/    /' | tee -a "$REPORT"
fi

# ---------------------------------------------------------------- 3. dependents recompiled untouched
say; say "### Dependents recompiled against $CAND_ID without editing their ckModel.yaml"
say "| model | before (sha256, 12) | after (sha256, 12) | result |"; say "|---|---|---|---|"
total=0; changed=0; identical=0; closure=0
RO="$WORK/out/recompile"; mkdir -p "$RO"
while read -r n; do
  total=$((total + 1)); rm -rf "$RO/$n"; mkdir -p "$RO/$n"
  if ckc -c Compile -p "$WORK/src/dep/$n/ConstructionKit" -o "$RO/$n" -lce true -lcr "$CAT2" > "$RO/$n.log" 2>&1; then
    nf="$(ls "$RO/$n"/ck-*.yaml | head -1)"; of="$(ls "$OUT/$n"/ck-*.yaml | head -1)"
    res="$(python3 "$LIB" compare "$of" "$nf" 2>&1)"; rc=$?
    if [ $rc -ne 0 ]; then changed=$((changed + 1)); finding "output of $n changed: $(echo "$res" | head -3 | tr '\n' ' ')"; r="CHANGED"
    elif [ "$res" = identical ]; then identical=$((identical + 1)); r="identical"
    else closure=$((closure + 1)); r="equivalent (closure line only)"; fi
    say "| $n | $(shasum -a 256 "$of" | cut -c1-12) | $(shasum -a 256 "$nf" | cut -c1-12) | $r |"
  else changed=$((changed + 1)); finding "$n does not compile against $CAND_ID ($RO/$n.log)"; say "| $n | | | COMPILE ERROR |"; fi
done < "$OUT/order.txt"
say; say "RESULT: $total dependents compiled against $CAND_ID, output changes: $changed (identical: $identical, closure line only: $closure)"
say "dependent repositories touched: 0 (sources are read with git archive from the committed HEAD)"
[ $FAILS -eq 0 ] && say "PROOF OK" || say "PROOF FAILED: $FAILS finding(s)"
exit $((FAILS > 0))
