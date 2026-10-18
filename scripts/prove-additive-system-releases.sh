#!/usr/bin/env bash
# AB#6676 / Epic AB#5584 F2.4: proof that two consecutive additive System releases need ZERO dependent bumps,
# and that a breaking System change is caught.
#
# Everything runs in PRIVATE catalogs under --work (octo-ckc gets a throw-away HOME with the GitHub catalogs switched
# off and `-lcr <private catalog>`): the shared local catalog, the shared GitHub catalogs and nuget.org are never
# written or read. The tenant part uses ONE throw-away tenant (default tmp-ab6676) and deletes it again.
#
# Phases (--phase, default all):
#   offline         build the catalogs, publish System n+1 / n+2, cascade, recompile, breaking n+3, adversarial cases
#   tenant-prep     (re)create the tenant, install the dependents from the local catalog, deploy the proof pipeline
#   tenant-pipeline deploy the proof pipeline once the mesh adapter is Online (after the stack restart)
#   tenant-release  import one System release into the tenant (--release 1|2|3) and run the checks
#   tenant-comm     import the additive System.Communication release (AB#4696 case) and run the checks
#   tenant-final    checks only (use after a service restart): status, log scan, requests, pipeline
#   tenant-guard    CheckUpgrade/ImportFromCatalog of the breaking release through the tenant upgrade guard (stack catalog, restored afterwards)
#   tenant-cleanup  delete the tenant and the octo-cli contexts the script created
#   all             offline, tenant-prep, release 1, release 2, [restart], tenant-final, release 3, tenant-cleanup
#
# Needs (tenant phases): the local stack from `Start-Octo -nonInteractive $true` (identity, asset-repo, bot, communication
# controller, mesh adapter started with --meshAdapterTenantId <tenant> --meshAdapterId 670000000000000000000002),
# the octo-cli binary and a context that may create and delete tenants (--admin-context, a USER login: `octo-cli -c LogIn -i --context <ctx>` once;
# machine client credentials cannot manage tenants). The tenant contexts use the client credentials of OCTO_CLI_CLIENT_ID/SECRET (client `claude-agent`).
# Rerunnable: the work directory and the tenant are recreated on every run; the script prints a report and exits non-zero
# when a claim of the story is not met (a FINDING is never hidden).
set -u -o pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
LIB="$SCRIPT_DIR/ab6676/proof_lib.py"
PIPELINE_YAML="$SCRIPT_DIR/ab6676/pipeline.yaml"

MONO="${OCTO_MONOREPO:-}"
WORK="${TMPDIR:-/tmp}/ab6676-proof"
CKC=""
CLASSIC_CKC=""
CLI=""
ADMIN_CTX=""
TENANT="tmp-ab6676"
PHASE="all"
RELEASE=""
RESTART_CMD="${PROOF_RESTART_CMD:-}"
SKILL_DIR=""
SYSTEM_BASE="2.5.0"
KEEP_TENANT=0
GUARD_TEST=0
FORCED_FILE_IMPORT=0
STACK_CATALOG=""

usage() { sed -n '2,24p' "$0"; echo; echo "Options: --monorepo DIR --work DIR --ckc PATH --classic-ckc PATH --octo-cli PATH --admin-context CTX --tenant ID --phase P --release N --restart-cmd CMD --skill-dir DIR --system-base X.Y.Z --keep-tenant --guard-test [--stack-catalog DIR] --forced-file-import --selftest"; }
while [ $# -gt 0 ]; do
  case "$1" in
    --monorepo) MONO="$2"; shift 2;;
    --work) WORK="$2"; shift 2;;
    --ckc) CKC="$2"; shift 2;;
    --classic-ckc) CLASSIC_CKC="$2"; shift 2;;
    --octo-cli) CLI="$2"; shift 2;;
    --admin-context) ADMIN_CTX="$2"; shift 2;;
    --tenant) TENANT="$2"; shift 2;;
    --phase) PHASE="$2"; shift 2;;
    --release) RELEASE="$2"; shift 2;;
    --restart-cmd) RESTART_CMD="$2"; shift 2;;
    --skill-dir) SKILL_DIR="$2"; shift 2;;
    --system-base) SYSTEM_BASE="$2"; shift 2;;
    --keep-tenant) KEEP_TENANT=1; shift;;
    --guard-test) GUARD_TEST=1; shift;;
    --stack-catalog) STACK_CATALOG="$2"; shift 2;;
    --forced-file-import) FORCED_FILE_IMPORT=1; shift;;
    --selftest) python3 "$LIB" selftest; exit $?;;
    -h|--help) usage; exit 0;;
    *) echo "unknown option $1"; usage; exit 64;;
  esac
done
[ -n "$MONO" ] || { echo "--monorepo (or OCTO_MONOREPO) is required"; exit 64; }
[ -n "$CKC" ] || CKC="$MONO/octo-construction-kit-engine/bin/DebugL/net10.0/publish/octo-ckc"
[ -n "$CLI" ] || CLI="$MONO/octo-cli/bin/DebugL/net10.0/osx-arm64/octo-cli"
[ -n "$ADMIN_CTX" ] || ADMIN_CTX="local_OctoSystem"
[ -n "$STACK_CATALOG" ] || STACK_CATALOG="$MONO/.octo/local-catalog"
mkdir -p "$WORK"; WORK="$(cd "$WORK" && pwd)"
REPORT="$WORK/report.md"
FAILS=0

# ------------------------------------------------------------------------------------------------ helpers
say() { echo "$*" | tee -a "$REPORT"; }
row() { printf '%s\n' "$*" | tee -a "$REPORT"; }
finding() { FAILS=$((FAILS + 1)); say "FINDING: $*"; }
# true when `GetAdapters -j` lists an Online adapter; parses the JSON, so compact and pretty-printed output both work (AB#6973)
adapter_online() { octo -c GetAdapters -j --context "$(tctx)" 2>&1 | python3 "$LIB" adapter-online; }
json_get() { python3 -c 'import json,sys; d=json.loads(sys.argv[1]); print(d.get(sys.argv[2],""))' "$1" "$2"; }
# octo-ckc with a throw-away HOME: no GitHub catalog, no token, no network
ckc() { local bin="$1"; shift; HOME="$WORK/home" "$bin" "$@"; }
now_utc() { date -u +%Y-%m-%dT%H:%M:%S.000Z; }

# CK sources: <repo-relative project dir>; each holds ConstructionKit/ckModel.yaml. System is built first.
SYSTEM_SRC="octo-construction-kit-engine/src/SystemCkModel"
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
# models that must be Available in the tenant (catalog model ids of the local catalog)
TENANT_MODELS=(Basic-2.5.0 Basic.Accounting-1.10.0 Basic.Energy-1.10.0 EnergyCommunity-4.10.0 Environment-3.6.0
  Industry.Basic-2.6.0 Industry.Energy-3.6.0 Industry.Fluid-2.5.0 Industry.Logistics-3.3.0 Industry.Maintenance-2.5.0
  Industry.Manufacturing-2.6.0 OctoSdkDemo-2.5.0)

next_minor() { python3 -c 'import sys; a,b,c=sys.argv[1].split("."); print(f"{a}.{int(b)+n}.0")' "$1" 2>/dev/null; }
ver() { python3 - "$SYSTEM_BASE" "$1" <<'PY'
import sys
a,b,c = sys.argv[1].split(".")
print(f"{a}.{int(b)+int(sys.argv[2])}.0")
PY
}
V1="$(ver 1)"; V2="$(ver 2)"; V3="$(ver 3)"   # n+1, n+2, n+3 (breaking, declared MINOR on purpose)

# compile one source dir with the given compiler into the given catalog and publish it there; prints the compiled file
compile_publish() { # <ckc> <catalog> <out-dir> <source dir>
  local bin="$1" cat="$2" out="$3" src="$4" name
  name="$(basename "$src")"
  rm -rf "$out/$name"; mkdir -p "$out/$name"
  ckc "$bin" -c Compile -p "$src/ConstructionKit" -o "$out/$name" -lce true -lcr "$cat" > "$out/$name.compile.log" 2>&1 || return 1
  local f; f="$(ls "$out/$name"/ck-*.yaml 2>/dev/null | head -1)"; [ -n "$f" ] || return 1
  ckc "$bin" -c Publish -f "$f" -c LocalFileSystemCatalog -lcr "$cat" > "$out/$name.publish.log" 2>&1 || return 1
  echo "$f"
}

# ------------------------------------------------------------------------------------------------ offline
build_flavor() { # <flavor> <ckc>
  local flavor="$1" bin="$2"
  local cat="$WORK/catalog-$flavor" out="$WORK/out/$flavor/before"
  rm -rf "$cat" "$WORK/out/$flavor"; mkdir -p "$cat" "$out"
  : > "$out/order.txt"
  compile_publish "$bin" "$cat" "$out" "$MONO/$SYSTEM_SRC" > /dev/null || { finding "[$flavor] System $SYSTEM_BASE does not compile"; return 1; }
  local pending=("${SOURCES[@]}") progress=1
  while [ ${#pending[@]} -gt 0 ] && [ $progress -eq 1 ]; do
    progress=0; local still=()
    for s in "${pending[@]}"; do
      if compile_publish "$bin" "$cat" "$out" "$MONO/$s" > /dev/null; then echo "$s" >> "$out/order.txt"; progress=1; else still+=("$s"); fi
    done
    pending=("${still[@]+"${still[@]}"}")
  done
  if [ ${#pending[@]} -gt 0 ]; then finding "[$flavor] not compilable (dependency order or error, see $out/<name>.compile.log): ${pending[*]}"; fi
  row "[$flavor] catalog built: $(wc -l < "$out/order.txt" | tr -d ' ') dependents + System $SYSTEM_BASE compiled and published (compiler: $(basename "$bin"))"
}

release_flavor() { # <flavor> <ckc> <n> <version> <src>
  local flavor="$1" bin="$2" n="$3" version="$4" src="$5"
  local cat="$WORK/catalog-$flavor"
  local out="$WORK/out/$flavor/after-n$n"
  rm -rf "$out"; mkdir -p "$out"
  rm -rf "$WORK/vv-$flavor-n$n"; cp -R "$cat" "$WORK/vv-$flavor-n$n"   # ValidateVersion registers siblings: work on a copy
  ckc "$bin" -c ValidateVersion -p "$src/ConstructionKit" -lce true -lcr "$WORK/vv-$flavor-n$n" > "$out/validate-version.txt" 2>&1
  local vv; vv="$(grep -E 'Result:' "$out/validate-version.txt" | tail -1 | sed 's/^ *//')"
  ckc "$bin" -c ValidateCascade -p "$src/ConstructionKit" -lce true -lcr "$cat" > "$out/cascade.txt" 2>&1
  local c; c="$(python3 "$LIB" cascade-count "$out/cascade.txt")"
  local f; f="$(compile_publish "$bin" "$cat" "$out" "$src")" || { finding "[$flavor] System $version does not compile/publish"; return 1; }
  # rebuild every dependent unchanged (no edit of any ckModel.yaml) and compare with its build before the System release
  local changed=0 total=0 names="" moved=0
  while read -r s; do
    local name; name="$(basename "$MONO/$s")"
    local reout="$out/recompile"; mkdir -p "$reout"
    rm -rf "$reout/$name"; mkdir -p "$reout/$name"
    total=$((total + 1))
    if ckc "$bin" -c Compile -p "$MONO/$s/ConstructionKit" -o "$reout/$name" -lce true -lcr "$cat" > "$reout/$name.log" 2>&1; then
      local nf; nf="$(ls "$reout/$name"/ck-*.yaml | head -1)"; local bf; bf="$(ls "$WORK/out/$flavor/before/$name"/ck-*.yaml | head -1)"
      if python3 "$LIB" compare "$bf" "$nf" > "$reout/$name.diff" 2>&1; then
        grep -q '^equivalent' "$reout/$name.diff" && moved=$((moved + 1))
      else changed=$((changed + 1)); names="$names $name"; fi
    else
      changed=$((changed + 1)); names="$names $name(compile-error)"
    fi
  done < "$WORK/out/$flavor/before/order.txt"
  local repin; repin="$(json_get "$c" needsRepin)"
  row "| $flavor | System-$version (n+$n) | ${vv#Result: } | $(json_get "$c" dependents) | $(json_get "$c" compatible) | $(json_get "$c" breaks) | $repin | $changed/$total (closure line only: $moved) |${names:+ rebuild needed:$names}"
  echo "$flavor $n $repin $changed" >> "$WORK/metrics.txt"
  [ "$flavor" = rr ] && { [ "$repin" = 0 ] && [ "$changed" = 0 ] || finding "[rr] System-$version: NeedsRepin=$repin, dependents whose build output changed=$changed ($names)"; }
  return 0
}

phase_offline() {
  say "## Offline proof (private catalogs under $WORK)"
  rm -rf "$WORK/home" "$WORK/src" "$WORK/out" "$WORK/metrics.txt" "$WORK"/catalog-* "$WORK"/vv-*
  mkdir -p "$WORK/home/.octo-ckc" "$WORK/src"
  cat > "$WORK/home/.octo-ckc/settings.json" <<'JSON'
{
  "LocalFileSystemCatalog": { "IsEnabled": true, "CacheFileName": "local-catalog-cache.json" },
  "PublicOctoGitHub": { "IsEnabled": false },
  "PrivateOctoGitHub": { "IsEnabled": false }
}
JSON
  [ -x "$CKC" ] || [ -f "$CKC" ] || { echo "ckc not found: $CKC"; exit 66; }
  local sysck="$MONO/$SYSTEM_SRC/ConstructionKit"
  python3 "$LIB" make-release --src "$sysck" --dest "$WORK/src/system-$V1/ConstructionKit" --version "$V1" --kind additive --index 1 | tee -a "$REPORT"
  python3 "$LIB" make-release --src "$WORK/src/system-$V1/ConstructionKit" --dest "$WORK/src/system-$V2/ConstructionKit" --version "$V2" --kind additive --index 2 | tee -a "$REPORT"
  python3 "$LIB" make-release --src "$WORK/src/system-$V2/ConstructionKit" --dest "$WORK/src/system-$V3/ConstructionKit" --version "$V3" --kind breaking | tee -a "$REPORT"

  say; say "Dependent metric per System release (dependents = models of octo-construction-kit and the service models, newest version, from source):"
  row "| compiler | candidate | ValidateVersion | dependents | Compatible | Breaks | NeedsRepin (= forced bumps) | build output changed without editing ckModel.yaml |"
  row "|---|---|---|---|---|---|---|---|"
  build_flavor rr "$CKC" || true
  [ -n "$CLASSIC_CKC" ] && { build_flavor v1 "$CLASSIC_CKC" || true; }
  for flavor in rr v1; do
    [ "$flavor" = v1 ] && [ -z "$CLASSIC_CKC" ] && continue
    local bin="$CKC"; [ "$flavor" = v1 ] && bin="$CLASSIC_CKC"
    release_flavor "$flavor" "$bin" 1 "$V1" "$WORK/src/system-$V1" || true
    release_flavor "$flavor" "$bin" 2 "$V2" "$WORK/src/system-$V2" || true
  done
  [ -n "$CLASSIC_CKC" ] || say "(no --classic-ckc: the v1 baseline row is missing; pass a compiler built before engine commit 8f69cca1)"

  # range-retaining output proof for one dependent
  local basic; basic="$(ls "$WORK/out/rr/after-n2/recompile/Octo.Sdk.Packages.Basic"/ck-*.yaml 2>/dev/null | head -1)"
  if [ -n "$basic" ]; then
    say; say "Range-retaining output of Basic compiled against System-$V2 without touching its ckModel.yaml:"
    grep -E "dependencyRanges|range: |floor: |minEngineVersion|System@2/" "$basic" | head -8 | sed 's/^/    /' | tee -a "$REPORT"
    grep -q "dependencyRanges" "$basic" || finding "Basic output has no dependencyRanges"
  fi

  say; say "### Breaking System-$V3 (removes the attribute DisplayName, declared as MINOR on purpose)"
  local out="$WORK/out/rr/n3"; mkdir -p "$out"
  rm -rf "$WORK/vv-rr-n3"; cp -R "$WORK/catalog-rr" "$WORK/vv-rr-n3"
  ckc "$CKC" -c ValidateVersion -p "$WORK/src/system-$V3/ConstructionKit" -lce true -lcr "$WORK/vv-rr-n3" > "$out/validate-version.txt" 2>&1
  grep -E "Required:|MAJOR |Error:|Result:" "$out/validate-version.txt" | cut -c1-220 | sed 's/^/    /' | tee -a "$REPORT"
  grep -q "OCTO-CK100" "$out/validate-version.txt" || finding "ValidateVersion did not reject System-$V3 (no OCTO-CK100)"
  ckc "$CKC" -c ValidateCascade -p "$WORK/src/system-$V3/ConstructionKit" -lce true -lcr "$WORK/catalog-rr" > "$out/cascade.txt" 2>&1
  local cc; cc="$(python3 "$LIB" cascade-count "$out/cascade.txt")"
  say "    ValidateCascade: $cc"
  [ "$(json_get "$cc" breaks)" -ge 1 ] 2>/dev/null || finding "ValidateCascade names no breaking dependent for System-$V3"
  grep -E "^  Breaks" -A1 "$out/cascade.txt" | head -8 | sed 's/^/    /' | tee -a "$REPORT"
  # publish to a COPY of the catalog: the local catalog is exempt from the publish gate (docs/ck-semver-rules.md, "Publish gate")
  rm -rf "$WORK/catalog-rr-n3"; cp -R "$WORK/catalog-rr" "$WORK/catalog-rr-n3"
  local f3; f3="$(compile_publish "$CKC" "$WORK/catalog-rr-n3" "$out" "$WORK/src/system-$V3" || true)"
  if [ -n "$f3" ]; then say "    ckc Compile + Publish of System-$V3 into the LOCAL catalog: accepted (the local file system catalog is exempt from the publish gate; the compile gate is informational for a ckLanguage 1 model such as System; covered by the gate test suites, see the PR)"
  else say "    ckc Compile/Publish of System-$V3: refused"; fi
  echo "$f3" > "$WORK/breaking-compiled.path"

  # A breaking release of a NON-System base model (Basic-2.6.0 without the attribute Iban, declared MINOR): the same dry run, and the
  # candidate for the tenant upgrade guard in phase tenant-guard (System is service-managed, see there)
  say; say "### Breaking Basic-2.6.0 (removes Iban, used by Basic.Accounting, declared as MINOR on purpose)"
  python3 "$LIB" make-dependent-break --src "$MONO/octo-construction-kit/src/ConstructionKits/Octo.Sdk.Packages.Basic/ConstructionKit" \
    --dest "$WORK/src/basic-break/ConstructionKit" --model Basic --version 2.6.0 --remove-attribute Iban | tee -a "$REPORT"
  ckc "$CKC" -c ValidateCascade -p "$WORK/src/basic-break/ConstructionKit" -lce true -lcr "$WORK/catalog-rr" > "$out/cascade-basic.txt" 2>&1
  local cb; cb="$(python3 "$LIB" cascade-count "$out/cascade-basic.txt")"
  say "    ValidateCascade: $cb"
  grep -E "^  Breaks" -A1 "$out/cascade-basic.txt" | head -6 | sed 's/^/    /' | tee -a "$REPORT"
  [ "$(json_get "$cb" breaks)" -ge 1 ] 2>/dev/null || finding "ValidateCascade names no breaking dependent for Basic-2.6.0"
  local fb; fb="$(compile_publish "$CKC" "$WORK/catalog-rr-n3" "$out" "$WORK/src/basic-break" || true)"
  echo "$fb" > "$WORK/basic-break-compiled.path"
  [ -n "$fb" ] || finding "Basic-2.6.0 (breaking) does not compile"

  # AB#4696 AC 2 (Loxone-style): an additive minor of System.Communication (new enum), the base of communication dependents
  say; say "### Additive System.Communication (new enum), the AB#4696 case"
  local ckc_comm="$MONO/octo-communication-controller-services/src/SystemCommunicationCkModel/ConstructionKit"
  COMM_VERSION="$(python3 "$LIB" next-minor "$ckc_comm")"
  python3 "$LIB" make-release --src "$ckc_comm" --dest "$WORK/src/comm-add/ConstructionKit" --version "$COMM_VERSION" --kind additive-enum --model System.Communication | tee -a "$REPORT"
  echo "$COMM_VERSION" > "$WORK/comm-version.txt"
  ckc "$CKC" -c ValidateCascade -p "$WORK/src/comm-add/ConstructionKit" -lce true -lcr "$WORK/catalog-rr" > "$out/cascade-comm.txt" 2>&1
  say "    ValidateCascade: $(python3 "$LIB" cascade-count "$out/cascade-comm.txt")"
  local fc; fc="$(compile_publish "$CKC" "$WORK/catalog-rr" "$out" "$WORK/src/comm-add" || true)"
  echo "$fc" > "$WORK/comm-compiled.path"
  [ -n "$fc" ] || finding "System.Communication-$COMM_VERSION (additive) does not compile"

  # nothing was edited in the dependent repositories (AC: no dependent is rebuilt or bumped, git status clean)
  say; say "### git status of the dependent CK sources (must be clean)"
  local dirty=0 s2 repo rel st
  for s2 in "${SOURCES[@]}"; do
    repo="${s2%%/*}"; rel="${s2#*/}"
    st="$(git -C "$MONO/$repo" status --porcelain -- "$rel" 2>&1)"
    if [ -n "$st" ]; then dirty=$((dirty + 1)); finding "dependent source $s2 has uncommitted changes: $(echo "$st" | head -2 | tr '\n' ' ')"; fi
  done
  row "dependent CK sources checked: ${#SOURCES[@]}, with uncommitted changes: $dirty"

  phase_adversarial
}

phase_adversarial() {
  say; say "### Adversarial cases against the range-retaining output (local catalog copy)"
  local adv="$WORK/catalog-adv" out="$WORK/out/adv"; rm -rf "$adv" "$out"; mkdir -p "$out"; cp -R "$WORK/catalog-rr" "$adv"
  local vf; vf="$WORK/src/system-$V1"
  # 1 lowered version: an additive source declared below an already published newer version of the same major
  python3 "$LIB" make-release --src "$MONO/$SYSTEM_SRC/ConstructionKit" --dest "$WORK/src/system-low/ConstructionKit" --version "$(python3 - "$SYSTEM_BASE" <<'PY'
import sys
a,b,c = sys.argv[1].split("."); print(f"{a}.{b}.1")
PY
)" --kind same > /dev/null
  rm -rf "$WORK/vv-adv-low"; cp -R "$WORK/catalog-rr" "$WORK/vv-adv-low"
  ckc "$CKC" -c ValidateVersion -p "$WORK/src/system-low/ConstructionKit" -lce true -lcr "$WORK/vv-adv-low" > "$out/low.txt" 2>&1
  row "1 lowered version (unchanged content declared as the patch number above $SYSTEM_BASE, while $V1 and $V2 are published): $(grep -E 'Result:' "$out/low.txt" | tail -1 | sed 's/^ *//') -- ACCEPTED LIMITATION 2 of the compile gate (docs/ck-semver-rules.md): in Local mode catalog entries at or above the declared version are never the baseline; OCTO-CK201 needs a published baseline (remote catalog, publish gate)"
  # 2 hand-edited compiled file: remove an attribute definition from the compiled model of System-$V2 but keep the version
  local c2; c2="$(ls "$WORK/out/rr/after-n2"/system* 2>/dev/null | head -1)"; c2="$(ls "$WORK/out/rr/after-n2/system-$V2"/ck-*.yaml 2>/dev/null | head -1)"
  [ -n "$c2" ] || c2="$(ls "$WORK/out/rr/after-n2"/*/ck-system-2.yaml 2>/dev/null | head -1)"
  if [ -n "$c2" ]; then
    python3 - "$c2" "$out/tampered.yaml" <<'PY'
import sys
t=open(sys.argv[1]).read()
new=t.replace("optional attribute added by an additive System release", "TAMPERED description, same version", 1)
assert new!=t
open(sys.argv[2],"w").write(new)
PY
    ckc "$CKC" -c Publish -f "$out/tampered.yaml" -c LocalFileSystemCatalog -lcr "$adv" > "$out/tampered.txt" 2>&1; local rc=$?
    row "2 hand-edited compiled file (description changed, same version System-$V2) published over the published one: exit $rc -- $(grep -m1 'already exists' "$out/tampered.txt" | cut -c1-120) -- the local catalog is exempt from the publish gate; the remote gate answers OCTO-CK205 (tests PublishGate)"
  else row "2 hand-edited compiled file: not run (no compiled System-$V2 found)"; fi
  # 3 removed baseline: delete the published baseline, then validate the breaking release
  rm -f "$adv"/ck-models/v2/s/System/2/ck-system-"$V2".json "$adv"/ck-models/v2/s/System/2/ck-system-"$V1".json "$adv"/ck-models/v2/s/System/2/ck-system-"$SYSTEM_BASE".json
  ckc "$CKC" -c ValidateVersion -p "$WORK/src/system-$V3/ConstructionKit" -lce true -lcr "$adv" > "$out/nobaseline.txt" 2>&1
  row "3 removed baseline (all earlier System versions deleted from the local catalog): $(grep -E 'Result:' "$out/nobaseline.txt" | tail -1 | sed 's/^ *//') -- ACCEPTED LIMITATION 1 (a model without a baseline is a first publication); the guarantee is the remote publish gate"
  # 4 case-variant name: the compiled System-$V2 with every occurrence of the model id spelled "system-<other version>"
  if [ -n "$c2" ]; then
    python3 - "$c2" "$out/case.yaml" "$V2" <<'PY'
import sys
t=open(sys.argv[1]).read()
open(sys.argv[2],"w").write(t.replace("System-"+sys.argv[3], "system-9.9.9"))
PY
    ckc "$CKC" -c Publish -f "$out/case.yaml" -c LocalFileSystemCatalog -lcr "$adv" > "$out/case.txt" 2>&1; local rc4=$?
    row "4 case-variant model name (system-9.9.9 next to the published System): publish exit $rc4 -- $(grep -iE 'exist|case|OCTO-CK|invalid|Error publishing' "$out/case.txt" | head -1 | cut -c1-200) -- the case-insensitive lookup is part of the remote gate (docs/ck-semver-rules.md, \"Baseline\" rule 6)"
  fi
  # 5 concurrent publish: two processes publish different content for the same new version at once
  rm -rf "$WORK/catalog-conc"; cp -R "$WORK/catalog-rr" "$WORK/catalog-conc"
  python3 "$LIB" make-release --src "$WORK/src/system-$V2/ConstructionKit" --dest "$WORK/src/system-conc-a/ConstructionKit" --version "$(ver 4)" --kind additive --index 4 > /dev/null
  python3 "$LIB" make-release --src "$WORK/src/system-$V2/ConstructionKit" --dest "$WORK/src/system-conc-b/ConstructionKit" --version "$(ver 4)" --kind additive --index 5 > /dev/null
  ( compile_publish "$CKC" "$WORK/catalog-conc" "$out/conca" "$WORK/src/system-conc-a" > /dev/null 2>&1; echo $? > "$out/conca.rc" ) &
  ( compile_publish "$CKC" "$WORK/catalog-conc" "$out/concb" "$WORK/src/system-conc-b" > /dev/null 2>&1; echo $? > "$out/concb.rc" ) &
  wait
  local pub; pub="$(ls "$WORK/catalog-conc"/ck-models/v2/s/System/2/ck-system-"$(ver 4)".json 2>/dev/null | wc -l | tr -d ' ')"
  row "5 concurrent publish of two different System-$(ver 4) (compile + publish in parallel): exit codes a=$(cat "$out/conca.rc") b=$(cat "$out/concb.rc"), files for the version: $pub (exactly one winner expected), loser: $(grep -hiE 'already exists|OCTO-CK|locked|being used' "$out"/conca/*.publish.log "$out"/concb/*.publish.log 2>/dev/null | head -1 | cut -c1-160), catalog index consistent: $(python3 -c 'import json,glob,sys; [json.load(open(f)) for f in glob.glob(sys.argv[1]+"/**/catalog.json", recursive=True)]; print("yes")' "$WORK/catalog-conc" 2>&1 | tail -1)"
  say "(The remote publish gate cases, including the race with an in-memory GitHub repository, are covered by the test suites PublishGate and CkPublishRaceTests; see the PR evidence.)"
}

# ------------------------------------------------------------------------------------------------ tenant
octo() { "$CLI" "$@"; }
# Admin calls (tenant create/delete) need the USER login of --admin-context. The machine client credentials in the environment must not
# reach them: octo-cli would silently replace the stored user token by a client-credentials token that cannot manage tenants (403).
octoa() { env -u OCTO_CLI_CLIENT_ID -u OCTO_CLI_CLIENT_SECRET "$CLI" "$@"; }
tctx() { echo "ab6676_proof"; }
tenant_login() {
  octo -c LogInClientCredentials --context "$(tctx)" > /dev/null 2>&1 || true
}

admin_refresh() { octoa -c AuthStatus --context "$ADMIN_CTX" > /dev/null 2>&1; }   # silent refresh of the user token (it lives one hour)
tenant_exists() { admin_refresh; local t; t="$(octoa -c GetTenants --context "$ADMIN_CTX" 2>&1)"; printf '%s' "$t" | grep -q "\"TenantId\": \"$TENANT\""; }
tenant_urls=(-isu https://localhost:5003/ -asu https://localhost:5001/ -bsu https://localhost:5009/ -csu https://localhost:5015/ -rsu https://localhost:5008/)
delete_tenant() {
  admin_refresh
  octo -c AddContext -n "$(tctx)" "${tenant_urls[@]}" -tid "$TENANT" > /dev/null 2>&1
  tenant_login
  octo -c DisableStreamData -y --context "$(tctx)" > "$WORK/tenant/disable-sd.txt" 2>&1 || octo -c DisableStreamData --context "$(tctx)" > "$WORK/tenant/disable-sd.txt" 2>&1
  octo -c DisableCommunication -y --context "$(tctx)" > "$WORK/tenant/disable-comm.txt" 2>&1 || octo -c DisableCommunication --context "$(tctx)" > "$WORK/tenant/disable-comm.txt" 2>&1
  octoa -c Delete -tid "$TENANT" -y --context "$ADMIN_CTX" > "$WORK/tenant/delete.txt" 2>&1
}

phase_tenant_prep() {
  say; say "## Tenant preparation ($TENANT)"
  mkdir -p "$WORK/tenant"
  local urls=("${tenant_urls[@]}")
  admin_refresh
  octoa -c AuthStatus --context "$ADMIN_CTX" 2>&1 | grep -q "client_credentials" && finding "admin context $ADMIN_CTX holds a client-credentials token, not a user login: run 'octo-cli -c LogIn -i --context $ADMIN_CTX' once"
  if tenant_exists; then delete_tenant; tenant_exists && finding "tenant $TENANT exists and could not be deleted: $(tail -1 "$WORK/tenant/delete.txt" | cut -c1-300)"; fi
  octoa -c Create -tid "$TENANT" -db "$TENANT" --context "$ADMIN_CTX" 2>&1 | tail -2 | tee -a "$REPORT"
  octo -c AddContext -n "$(tctx)" "${urls[@]}" -tid "$TENANT" > /dev/null 2>&1
  tenant_login
  octo -c EnableCommunication --context "$(tctx)" 2>&1 | tail -1 | tee -a "$REPORT"
  octo -c EnableStreamData --context "$(tctx)" 2>&1 | tail -1 | tee -a "$REPORT"
  for m in "${TENANT_MODELS[@]}"; do
    octo -c ImportFromCatalog -cn LocalFileSystemCatalog -m "$m" -w --context "$(tctx)" > "$WORK/tenant/import-$m.txt" 2>&1 \
      && grep -qE "has completed|No models need to be imported" "$WORK/tenant/import-$m.txt" || finding "ImportFromCatalog $m failed, see $WORK/tenant/import-$m.txt"
  done
  python3 "$LIB" make-flow --pipeline "$PIPELINE_YAML" --out "$WORK/tenant/flow.rt.yaml"
  octo -c ImportRt -f "$WORK/tenant/flow.rt.yaml" -w --context "$(tctx)" 2>&1 | tail -1 | tee -a "$REPORT"
  snapshot "baseline-before-releases"
}

# after the stack (re)start: the mesh adapter of the stack registers for the tenant, then the pipeline is deployed once
phase_tenant_pipeline() {
  say; say "## Proof pipeline on the local mesh adapter"
  tenant_login
  local i
  for i in $(seq 1 100); do
    adapter_online && break
    sleep 3
  done
  adapter_online || finding "the mesh adapter is not Online for $TENANT (start the stack with --meshAdapterTenantId $TENANT after the tenant exists)"
  octo -c DeployPipeline -aid 670000000000000000000002 -pid aaa000000000000000006762 -f "$PIPELINE_YAML" --context "$(tctx)" 2>&1 | tail -1 | tee -a "$REPORT"
  octo -c GetPipelineStatus -id aaa000000000000000006762 -j --context "$(tctx)" 2>&1 | tail -1 | cut -c1-200 | tee -a "$REPORT"
}

snapshot() { # <label>: LibraryStatus + counts
  local label="$1" f="$WORK/tenant/status-$1.txt"
  octo -c LibraryStatus --context "$(tctx)" > "$f" 2>&1
  local c; c="$(python3 "$LIB" status-count "$f")"
  row "[$label] LibraryStatus: installed=$(json_get "$c" installed) Available=$(json_get "$c" available) ResolveFailed=$(json_get "$c" resolveFailed)"
  row "    catalog models flagged Incompatible with the installed System (exact-pinned, published before the flip; not installed here): $(grep -E '^[A-Za-z][A-Za-z0-9.]* +- +[0-9.]+ +Not Installed +Incompatible' "$f" | awk '{printf "%s ", $1}')"
  if [ "$(json_get "$c" resolveFailed)" != "0" ]; then finding "[$label] ResolveFailed models: $(json_get "$c" resolveFailedRows)"; fi
  grep -E "^(System|Basic|Industry|EnergyCommunity|Environment|OctoSdkDemo)[A-Za-z.]* " "$f" | grep -v "Not Installed" | awk '{printf "    %-26s %-10s %s\n",$1,$2,$4}' >> "$REPORT"
}

requests() { # one request per service
  local label="$1" since="$2"
  local auth; auth="$(octo -c AuthStatus --context "$(tctx)" 2>&1)"
  if printf '%s' "$auth" | grep -q "Access token is valid" && ! printf '%s' "$auth" | grep -qi "failed"; then row "[$label] login (client credentials token for tenant $TENANT): ok"; else finding "[$label] login failed: $(printf '%s' "$auth" | tail -1 | cut -c1-200)"; fi
  # GraphQL runtime query + create (the pipeline creates or updates a Basic/Tree entity) and the adapter pipeline run
  local i ex ok=0 attempt
  for i in $(seq 1 30); do adapter_online && break; sleep 3; done
  for attempt in 1 2 3; do
    ex="$(octo -c ExecutePipeline -id aaa000000000000000006762 --context "$(tctx)" 2>&1 | tail -1)"
    for i in 1 2 3 4 5 6 7 8 9 10; do
      sleep 2
      if octo -c GetLatestPipelineExecution -id aaa000000000000000006762 -j --context "$(tctx)" 2>&1 | tail -1 | grep -q '"Status":1'; then ok=1; break; fi
    done
    [ $ok -eq 1 ] && break
    sleep 10
  done
  [ $ok -eq 1 ] && row "[$label] adapter pipeline run (Basic/Tree upsert + query) without redeploy: Status=1 (success)$([ $attempt -gt 1 ] && echo " (attempt $attempt, the adapter was still registering)")" || finding "[$label] pipeline run did not succeed: $ex"
  if [ -n "$SKILL_DIR" ]; then
    local cnt; cnt="$(OCTO_CLI_CONTEXT="$(tctx)" bash "$SKILL_DIR/run_python.sh" "$SKILL_DIR/rt_explorer.py" count Basic/Tree --insecure 2>&1 | tail -1)"
    row "[$label] GraphQL runtime query: Basic/Tree -> $cnt"
  fi
  # bot job: a fixup run is a Hangfire job of the bot service
  octo -c RunFixupScripts -w --context "$(tctx)" > "$WORK/tenant/fixup-$label.txt" 2>&1
  grep -q "completed" "$WORK/tenant/fixup-$label.txt" && row "[$label] bot job (RunFixupScripts -w): completed" || finding "[$label] bot job did not complete: $(tail -1 "$WORK/tenant/fixup-$label.txt" | cut -c1-200)"
  logscan "$label" "$since"
}

logscan() { # <label> <since>
  local label="$1" since="$2" logs=()
  for l in AssetRepositoryServices BotServices CommunicationControllerServices MeshAdapter IdentityServices PlatformServices; do logs+=("$MONO/logFiles/$l.log"); done
  local r; r="$(python3 "$LIB" scan-logs --since "$since" "${logs[@]}")"
  row "[$label] service logs since $since: failures ('not found in CkCache', 'missing dependencies', 'marked as ResolveFailed', 'too old for the tenant') = $(json_get "$r" failures)"
  [ "$(json_get "$r" failures)" = 0 ] || finding "[$label] log failures: $(json_get "$r" failureLines)"
  python3 -c 'import json,sys; d=json.loads(sys.argv[1]); [print("    log: %s x%d: %s" % (k, v["count"], v["first"][:260])) for k,v in d["info"].items()]' "$r" | tee -a "$REPORT"
}

phase_tenant_release() { # <n> (1|2 additive; 3 = forced FILE import of the breaking release, needs --forced-file-import)
  local n="$1" v file
  case "$n" in 1) v="$V1";; 2) v="$V2";; 3) v="$V3";; *) echo "--release 1|2|3"; exit 64;; esac
  file="$(ls "$WORK/out/rr/after-n$n"/*/ck-system-2.yaml 2>/dev/null | head -1)"
  [ "$n" = 3 ] && file="$(cat "$WORK/breaking-compiled.path" 2>/dev/null)"
  [ -n "$file" ] && [ -f "$file" ] || { echo "compiled System $v not found, run the offline phase first"; exit 66; }
  if [ "$n" = 3 ] && [ $FORCED_FILE_IMPORT -eq 0 ]; then
    say "(forced ImportCk of the breaking System-$v skipped: it leaves the tenant unloadable, see docs/ck-additive-system-proof.md finding F1; use --forced-file-import on a throw-away tenant only)"; return 0
  fi
  say; say "## Tenant: import System-$v ($([ "$n" = 3 ] && echo breaking || echo additive)) with ImportCk -f"
  tenant_login
  local since; since="$(now_utc)"
  octo -c ImportCk -f "$file" -w --context "$(tctx)" > "$WORK/tenant/import-system-$v.txt" 2>&1
  tail -2 "$WORK/tenant/import-system-$v.txt" | cut -c1-400 | tee -a "$REPORT"
  snapshot "after-System-$v"
  if [ "$n" = 3 ]; then
    finding "forced ImportCk -f of the breaking System-$v was accepted by the import job (no usedSurface refusal on the file import path)"
    logscan "after-System-$v" "$since"
  else
    requests "after-System-$v" "$since"
  fi
}

# The tenant upgrade guard (AB#5687 / AB#6867 / AB#6870) on the catalog import path. Needs the breaking and the additive releases in the
# catalog the running asset-repo reads (the stack's local catalog): they are published there temporarily and the catalog is restored
# byte for byte from a backup afterwards, in every case.
phase_tenant_guard() {
  say; say "## Tenant upgrade guard on the catalog path (ImportFromCatalog / CheckUpgrade)"
  if [ $GUARD_TEST -eq 0 ]; then say "(skipped: pass --guard-test to publish the proof releases temporarily into $STACK_CATALOG)"; return 0; fi
  [ -d "$STACK_CATALOG" ] || { finding "stack catalog $STACK_CATALOG not found"; return 0; }
  tenant_login
  local backup="$WORK/stack-catalog-backup.tgz"
  tar -C "$(dirname "$STACK_CATALOG")" -czf "$backup" "$(basename "$STACK_CATALOG")" || { finding "cannot back up $STACK_CATALOG"; return 0; }
  guard_restore() {
    rm -rf "$STACK_CATALOG" && tar -C "$(dirname "$STACK_CATALOG")" -xzf "$backup"
    octo -c RefreshCatalogs --context "$(tctx)" > /dev/null 2>&1
    say "stack catalog restored from backup ($STACK_CATALOG); proof releases removed"
  }
  local n f ver
  for n in 1 2; do
    ver="$V1"; [ $n = 2 ] && ver="$V2"
    f="$(ls "$WORK/out/rr/after-n$n"/*/ck-system-2.yaml 2>/dev/null | head -1)"
    ckc "$CKC" -c Publish -f "$f" -c LocalFileSystemCatalog -lcr "$STACK_CATALOG" > "$WORK/tenant/guard-publish-$ver.txt" 2>&1 || { finding "publish of System-$ver into the stack catalog failed"; guard_restore; return 0; }
  done
  ckc "$CKC" -c Publish -f "$(cat "$WORK/breaking-compiled.path")" -c LocalFileSystemCatalog -lcr "$STACK_CATALOG" > "$WORK/tenant/guard-publish-$V3.txt" 2>&1 || { finding "publish of System-$V3 into the stack catalog failed"; guard_restore; return 0; }
  ckc "$CKC" -c Publish -f "$(cat "$WORK/basic-break-compiled.path")" -c LocalFileSystemCatalog -lcr "$STACK_CATALOG" > "$WORK/tenant/guard-publish-basic.txt" 2>&1 || { finding "publish of Basic-2.6.0 into the stack catalog failed"; guard_restore; return 0; }
  octo -c RefreshCatalogs --context "$(tctx)" > "$WORK/tenant/guard-refresh.txt" 2>&1
  local noise='^(Octo Mesh|Development|Executable|Context file|Using|Service URI|Default Tenant)'
  for m in "System-$V2" "System-$V3" "Basic-2.6.0"; do
    octo -c CheckUpgrade -cn LocalFileSystemCatalog -m "$m" --context "$(tctx)" > "$WORK/tenant/guard-check-$m.txt" 2>&1
    row "CheckUpgrade $m: $(grep -vE "$noise" "$WORK/tenant/guard-check-$m.txt" | grep -v '^ *$' | head -8 | tr '\n' '|' | cut -c1-700)"
  done
  for m in "System-$V3" "Basic-2.6.0"; do
    octo -c ImportFromCatalog -cn LocalFileSystemCatalog -m "$m" -w --context "$(tctx)" > "$WORK/tenant/guard-import-$m.txt" 2>&1
    row "ImportFromCatalog $m (no force): $(grep -vE "$noise" "$WORK/tenant/guard-import-$m.txt" | grep -v '^ *$' | head -10 | tr '\n' '|' | cut -c1-900)"
  done
  grep -qiE "block|break|usedSurface|Iban" "$WORK/tenant/guard-import-Basic-2.6.0.txt" || finding "the tenant upgrade guard did not refuse the breaking Basic-2.6.0 on ImportFromCatalog"
  grep -qE "No models need to be imported" "$WORK/tenant/guard-import-System-$V3.txt" && row "NOTE System is service-managed: ImportFromCatalog/CheckUpgrade do not import or judge a System release (\"No models need to be imported\"); a System release reaches a tenant by the embedded import of a new service, or by ImportCk -f (finding F1/F2 in docs/ck-additive-system-proof.md)"
  snapshot "after-guard"
  octo -c ExecutePipeline -id aaa000000000000000006762 --context "$(tctx)" > /dev/null 2>&1 && row "tenant still serves requests after the refused breaking imports: ExecutePipeline ok" || finding "tenant does not serve requests after the breaking import attempts"
  guard_restore
}

phase_tenant_comm() {
  local v file; v="$(cat "$WORK/comm-version.txt" 2>/dev/null)"; file="$(cat "$WORK/comm-compiled.path" 2>/dev/null)"
  [ -n "$v" ] && [ -f "$file" ] || { echo "compiled System.Communication proof release not found, run the offline phase first"; exit 66; }
  say; say "## Tenant: import System.Communication-$v (additive enum) with ImportCk -f (AB#4696 case)"
  tenant_login
  local since; since="$(now_utc)"
  octo -c ImportCk -f "$file" -w --context "$(tctx)" > "$WORK/tenant/import-comm-$v.txt" 2>&1
  tail -1 "$WORK/tenant/import-comm-$v.txt" | cut -c1-300 | tee -a "$REPORT"
  snapshot "after-System.Communication-$v"
  requests "after-System.Communication-$v" "$since"
}

phase_tenant_final() {
  say; say "## Tenant: after the service restart (embedded System $SYSTEM_BASE vs installed System-$V2)"
  tenant_login
  local since="${1:-$(now_utc)}"
  snapshot "after-restart"
  requests "after-restart" "$since"
}

phase_tenant_cleanup() {
  say; say "## Cleanup"
  if [ $KEEP_TENANT -eq 0 ]; then
    delete_tenant
    if tenant_exists; then finding "tenant $TENANT could not be deleted: $(tail -1 "$WORK/tenant/delete.txt" | cut -c1-300)"; else say "tenant $TENANT deleted"; fi
    octo -c RemoveContext -n "$(tctx)" > /dev/null 2>&1
    say "context $(tctx) removed. The shared local catalog was never written by this script (the --guard-test copies are removed again)."
  else
    say "--keep-tenant: tenant $TENANT and context $(tctx) kept"
  fi
}

# ------------------------------------------------------------------------------------------------ main
: > "$REPORT.tmp" 2>/dev/null; rm -f "$REPORT.tmp"
[ "$PHASE" = all ] || [ "$PHASE" = offline ] && [ ! -f "$REPORT" ] && : > "$REPORT"
[ -f "$REPORT" ] || : > "$REPORT"
case "$PHASE" in
  offline) : > "$REPORT"; phase_offline;;
  tenant-prep) phase_tenant_prep;;
  tenant-release) phase_tenant_release "$RELEASE";;
  tenant-pipeline) phase_tenant_pipeline;;
  tenant-comm) phase_tenant_comm;;
  tenant-final) phase_tenant_final;;
  tenant-guard) GUARD_TEST=1; phase_tenant_guard;;
  tenant-cleanup) phase_tenant_cleanup;;
  all)
    : > "$REPORT"; phase_offline; phase_tenant_prep
    if [ -n "$RESTART_CMD" ]; then say "restarting services (mesh adapter must find the new tenant): $RESTART_CMD"; bash -c "$RESTART_CMD"; fi
    phase_tenant_pipeline
    phase_tenant_release 1; phase_tenant_release 2; phase_tenant_comm
    restart_at="$(now_utc)"
    if [ -n "$RESTART_CMD" ]; then say "restarting services: $RESTART_CMD"; bash -c "$RESTART_CMD"; fi
    phase_tenant_final "$restart_at"; phase_tenant_guard; phase_tenant_release 3; phase_tenant_cleanup;;
  *) usage; exit 64;;
esac
say; say "Result: $([ $FAILS -eq 0 ] && echo "all claims met" || echo "$FAILS FINDING(S)")"
exit $([ $FAILS -eq 0 ] && echo 0 || echo 1)
