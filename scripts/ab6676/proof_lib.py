#!/usr/bin/env python3
"""Helpers of scripts/prove-additive-system-releases.sh (AB#6676). Standard library only.

Sub-commands:
  make-release   derive a System CK source tree for an additive or a breaking proof release
  compare        compare two compiled model files (the "does a dependent need a rebuild" test)
  cascade-count  read the summary line of an `octo-ckc -c ValidateCascade` report
  status-count   count the STATE column of an `octo-cli -c LibraryStatus` report
  scan-logs      count log lines that match failure patterns since a UTC time stamp
"""
import argparse
import json
import re
import shutil
import sys
from pathlib import Path


# --------------------------------------------------------------------------------------------- make-release
def _replace_model_id(ck_dir: Path, version: str, model: str = "System") -> None:
    path = ck_dir / "ckModel.yaml"
    text = path.read_text(encoding="utf-8")
    new, n = re.subn(r'(modelId:\s*"?)' + re.escape(model) + r'-[0-9]+\.[0-9]+\.[0-9]+("?)', rf"\g<1>{model}-{version}\g<2>", text, count=1)
    if n != 1:
        sys.exit(f"cannot find the modelId line in {path}")
    path.write_text(new, encoding="utf-8")


def _add_optional_attribute(ck_dir: Path, name: str) -> None:
    """An additive release: one new optional attribute on the base type Entity (every dependent derives from it)."""
    attributes = ck_dir / "attributes" / "blueprint.yaml"
    attributes.write_text(
        attributes.read_text(encoding="utf-8").rstrip("\n")
        + f'\n  - id: {name}\n    description: "AB#6676 proof: optional attribute added by an additive System release."\n    valueType: String\n',
        encoding="utf-8")
    entity = ck_dir / "types" / "entity.yaml"
    text = entity.read_text(encoding="utf-8")
    anchor = "      - id: ${this}/RtBlueprintAppliedAt\n        name: RtBlueprintAppliedAt\n        isOptional: true\n"
    if anchor not in text:
        sys.exit("the Entity anchor (RtBlueprintAppliedAt) is not in types/entity.yaml: update proof_lib.py")
    entity.write_text(text.replace(anchor, anchor + f"      - id: ${{this}}/{name}\n        name: {name}\n        isOptional: true\n", 1),
                      encoding="utf-8")


def _add_enum(ck_dir: Path, name: str) -> None:
    (ck_dir / "enums" / f"{name[0].lower()}{name[1:]}.yaml").write_text(
        '$schema: https://schemas.meshmakers.cloud/construction-kit-elements.schema.json\n'
        'enums:\n'
        f'- enumId: {name}\n'
        '  description: "AB#6676 proof: a new enum added by an additive System release."\n'
        '  values:\n'
        '  - key: 0\n    name: None\n    description: None\n'
        '  - key: 1\n    name: Proof\n    description: Proof\n', encoding="utf-8")


def _remove_attribute(ck_dir: Path, attribute_id: str) -> None:
    """A breaking release: remove an attribute definition that dependents use (usedSurface hit)."""
    path = ck_dir / "attributes" / "base.yaml"
    lines = path.read_text(encoding="utf-8").splitlines(keepends=True)
    out, skip = [], False
    removed = False
    for line in lines:
        if line.strip() == f"- id: {attribute_id}":
            skip, removed = True, True
            continue
        if skip and line.startswith("    ") and not line.lstrip().startswith("- id:"):
            continue
        skip = False
        out.append(line)
    if not removed:
        sys.exit(f"attribute {attribute_id} is not defined in {path}")
    path.write_text("".join(out), encoding="utf-8")


def _remove_attribute_everywhere(ck_dir: Path, attribute_id: str) -> int:
    """Remove an attribute definition and every `${this}/<id>` assignment (type/record attribute lists) of a model."""
    removed = 0
    for path in sorted(ck_dir.rglob("*.yaml")):
        if path.name == "ckModel.yaml":
            continue
        lines = path.read_text(encoding="utf-8").splitlines(keepends=True)
        out, i = [], 0
        while i < len(lines):
            line = lines[i]
            m = re.match(r"^(\s*)- id: (\$\{this\}/)?" + re.escape(attribute_id) + r"\s*$", line)
            if m:
                indent = len(m.group(1))
                i += 1
                while i < len(lines) and (len(lines[i]) - len(lines[i].lstrip())) > indent and lines[i].strip():
                    i += 1
                removed += 1
                continue
            out.append(line)
            i += 1
        path.write_text("".join(out), encoding="utf-8")
    return removed


def make_dependent_break(args) -> None:
    """Breaking release of a non-System base model (declared as a MINOR on purpose)."""
    src, dest = Path(args.src), Path(args.dest)
    if dest.exists():
        shutil.rmtree(dest)
    shutil.copytree(src, dest)
    path = dest / "ckModel.yaml"
    text = path.read_text(encoding="utf-8")
    new, n = re.subn(r"(modelId:\s*\"?)" + re.escape(args.model) + r"-[0-9]+\.[0-9]+\.[0-9]+", rf"\g<1>{args.model}-{args.version}", text, count=1)
    if n != 1:
        sys.exit(f"cannot find the modelId line of {args.model} in {path}")
    path.write_text(new, encoding="utf-8")
    removed = _remove_attribute_everywhere(dest, args.remove_attribute)
    if removed < 1:
        sys.exit(f"attribute {args.remove_attribute} not found in {dest}")
    print(f"{args.model}-{args.version} (breaking: removed {args.remove_attribute}, {removed} occurrence(s)) -> {dest}")


def next_minor(args) -> None:
    """Print the next minor version of the model declared in <ck-dir>/ckModel.yaml (X.Y.Z -> X.Y+1.0)."""
    text = (Path(args.ck_dir) / "ckModel.yaml").read_text(encoding="utf-8")
    m = re.search(r'modelId:\s*"?[A-Za-z0-9.]+-([0-9]+)\.([0-9]+)\.([0-9]+)', text)
    if not m:
        sys.exit("no modelId")
    print(f"{m.group(1)}.{int(m.group(2)) + 1}.0")


def make_release(args) -> None:
    src, dest = Path(args.src), Path(args.dest)
    if dest.exists():
        shutil.rmtree(dest)
    shutil.copytree(src, dest)
    _replace_model_id(dest, args.version, args.model)
    if args.kind == "additive-enum":
        _add_enum(dest, "ProofKind")
    elif args.kind == "additive":
        _add_optional_attribute(dest, f"RtProofNote{args.index}")
        if args.index >= 2:
            _add_enum(dest, f"ProofKind{args.index}")
    elif args.kind == "same":
        pass  # no content change: a patch-level number on the unchanged base content
    elif args.kind == "breaking":
        _remove_attribute(dest, args.remove_attribute)
    else:
        sys.exit(f"unknown kind {args.kind}")
    print(f"{args.model}-{args.version} ({args.kind}) -> {dest}")


# --------------------------------------------------------------------------------------------- compare
def _load(path: Path):
    text = path.read_text(encoding="utf-8")
    if path.suffix == ".json":
        return json.loads(text)
    # compiled yaml: compare textually, the structure is irrelevant for an equality test
    return text


def _strip_closure(text: str):
    """Split a compiled model into (the text without the resolved `dependencies:` closure, the closure lines).
    The closure is the exact version list the dependency resolver chose; with range retention it is informational
    (SemVer row D8 / AB#5435: a resolved dependency moving up inside its major is level None)."""
    kept, closure, inside = [], [], False
    for line in text.splitlines():
        if re.match(r"^dependencies:\s*$", line):
            inside = True
            kept.append(line)
            continue
        if inside and (line.startswith("- ") or line.startswith("  - ")):
            closure.append(line.strip())
            continue
        inside = False
        kept.append(line)
    return kept, closure


def compare(args) -> None:
    ta = Path(args.before).read_text(encoding="utf-8")
    tb = Path(args.after).read_text(encoding="utf-8")
    if ta == tb:
        print("identical")
        return
    ka, ca = _strip_closure(ta)
    kb, cb = _strip_closure(tb)
    if ka == kb:
        print(f"equivalent: only the resolved dependency closure moved ({', '.join(ca)} -> {', '.join(cb)}); retained ranges and references are byte-identical")
        return
    sa, sb = set(ka), set(kb)
    only_a = sorted(sa - sb)
    only_b = sorted(sb - sa)
    print(f"DIFFERENT (-{len(only_a)} +{len(only_b)} lines)")
    for line in only_a[:args.max_lines]:
        print(f"  - {line.strip()[:160]}")
    for line in only_b[:args.max_lines]:
        print(f"  + {line.strip()[:160]}")
    sys.exit(1)


# --------------------------------------------------------------------------------------------- cascade-count
def cascade_count(args) -> None:
    text = Path(args.report).read_text(encoding="utf-8", errors="replace")
    m = re.search(r"(\d+) dependent\(s\): (\d+) Breaks, (\d+) NeedsRepin, (\d+) Compatible, (\d+) NotInRange", text)
    if not m:
        print(json.dumps({"parsed": False}))
        sys.exit(2)
    total, breaks, repin, compat, notin = map(int, m.groups())
    names = {k: re.findall(rf"^\s+{k}\s+(\S+)", text, re.M) for k in ("Breaks", "NeedsRepin")}
    print(json.dumps({"parsed": True, "dependents": total, "breaks": breaks, "needsRepin": repin, "compatible": compat,
                      "notInRange": notin, "breaksNames": names["Breaks"], "needsRepinNames": names["NeedsRepin"]}))


# --------------------------------------------------------------------------------------------- status-count
def status_count(args) -> None:
    rows = []
    for line in Path(args.report).read_text(encoding="utf-8", errors="replace").splitlines():
        if re.match(r"^[A-Za-z][A-Za-z0-9.]*\s+(\S+)\s+(\S+)\s+(Available|Not Installed|Resolve ?Failed|\S+)", line):
            rows.append(line)
    installed = [r for r in rows if not re.match(r"^\S+\s+-\s", r)]
    failed = [r for r in installed if re.search(r"Resolve ?Failed", r, re.I)]
    available = [r for r in installed if re.search(r"\bAvailable\b", r)]
    print(json.dumps({"installed": len(installed), "available": len(available), "resolveFailed": len(failed),
                      "resolveFailedRows": [f.strip()[:140] for f in failed]}))


# --------------------------------------------------------------------------------------------- scan-logs
PATTERNS = [
    "not found in CkCache",
    "has missing dependencies",
    "marked as ResolveFailed",
    "too old for the tenant",
]
INFO_PATTERNS = ["downgrade prevented"]


def scan_logs(args) -> None:
    since = args.since
    result = {"since": since, "failures": 0, "failureLines": [], "info": {}}
    for log in args.logs:
        p = Path(log)
        if not p.exists():
            continue
        for line in p.read_text(encoding="utf-8", errors="replace").splitlines():
            m = re.search(r'"timestamp":"([^"]+)"', line)
            if not m or m.group(1) < since:
                continue
            if any(x in line for x in PATTERNS):
                result["failures"] += 1
                if len(result["failureLines"]) < 5:
                    result["failureLines"].append(f"{p.name}: {line[:240]}")
            for info in INFO_PATTERNS:
                if info in line:
                    result["info"].setdefault(info, []).append(line[:400])
    result["info"] = {k: {"count": len(v), "first": v[0]} for k, v in result["info"].items()}
    print(json.dumps(result))


def make_flow(args) -> None:
    """Runtime model with one DataFlow and one pipeline (the pipeline YAML is embedded as an attribute value)."""
    yaml_text = Path(args.pipeline).read_text(encoding="utf-8").rstrip("\n")
    indented = "".join(f"          {line}\n" for line in yaml_text.splitlines())
    Path(args.out).write_text(
        "$schema: https://schemas.meshmakers.cloud/runtime-model.schema.json\n"
        "dependencies:\n  - System.Communication-[4.0,5.0)\nentities:\n"
        f"  - rtId: {args.flow_id}\n    ckTypeId: System.Communication/DataFlow-1\n    attributes:\n"
        "      - id: System/Name-1\n        value: AB6676 proof flow\n"
        f"  - rtId: {args.pipeline_id}\n    ckTypeId: System.Communication/Pipeline-1\n    associations:\n"
        f"      - roleId: System/ParentChild-1\n        targetRtId: {args.flow_id}\n        targetCkTypeId: System.Communication/DataFlow-1\n"
        f"      - roleId: System.Communication/Executes-1\n        targetRtId: \"{args.adapter_id}\"\n        targetCkTypeId: System.Communication/Adapter-1\n"
        "    attributes:\n      - id: System.Communication/DeploymentState-1\n        value: 0\n"
        "      - id: System/Name-1\n        value: AB6676 proof pipeline\n"
        "      - id: System/Enabled-1\n        value: true\n"
        "      - id: System.Communication/PipelineDefinition-1\n        value: |-\n" + indented, encoding="utf-8")

# --------------------------------------------------------------------------------------------- adapter-online
def _json_documents(text: str):
    """Yield every JSON document found in the text: the whole text, then each line, then any value starting at a
    '[' or '{' (octo-cli may print log lines before the JSON; compact and pretty-printed output both work)."""
    dec = json.JSONDecoder()
    try:
        yield json.loads(text)
        return
    except ValueError:
        pass
    pos = 0
    while pos < len(text):
        m = re.search(r"[\[{]", text[pos:])
        if not m:
            return
        start = pos + m.start()
        try:
            obj, end = dec.raw_decode(text, start)
            yield obj
            pos = end
        except ValueError:
            pos = start + 1


def _any_adapter_online(node) -> bool:
    if isinstance(node, dict):
        state = node.get("CommunicationState", node.get("communicationState"))
        if isinstance(state, str) and state.strip().lower() == "online":
            return True
        return any(_any_adapter_online(v) for v in node.values())
    if isinstance(node, list):
        return any(_any_adapter_online(v) for v in node)
    return False


def adapter_online_text(text: str) -> bool:
    """True when the `GetAdapters -j` output lists an adapter whose CommunicationState is Online, independent of
    the JSON whitespace (AB#6973). Empty, non-JSON or Offline output is False."""
    return any(_any_adapter_online(doc) for doc in _json_documents(text))


def adapter_online(args) -> None:
    sys.exit(0 if adapter_online_text(sys.stdin.read()) else 1)


# --------------------------------------------------------------------------------------------- selftest
SELFTEST_ONLINE_COMPACT = '[{"AdapterId":"670000000000000000000002","CommunicationState":"Online"}]'
SELFTEST_ONLINE_PRETTY = json.dumps(json.loads(SELFTEST_ONLINE_COMPACT), indent=2)  # spaced form, as a pretty-printing CLI would print it
SELFTEST_ONLINE_LOGGED = 'info: connecting\n' + SELFTEST_ONLINE_PRETTY
SELFTEST_OFFLINE = '[{"AdapterId":"670000000000000000000002","CommunicationState":"Offline"}]'


def selftest(args) -> None:
    cases = [
        ("compact Online", SELFTEST_ONLINE_COMPACT, True),
        ("pretty-printed Online", SELFTEST_ONLINE_PRETTY, True),
        ("log line before pretty JSON", SELFTEST_ONLINE_LOGGED, True),
        ("single object, compact", '{"CommunicationState":"Online"}', True),
        ("Offline compact", SELFTEST_OFFLINE, False),
        ("Offline pretty", json.dumps(json.loads(SELFTEST_OFFLINE), indent=2), False),
        ("empty output", "", False),
        ("error text", "Error: connection refused", False),
        ("empty list", "[]", False),
    ]
    bad = 0
    for name, text, expected in cases:
        got = adapter_online_text(text)
        print(("ok   " if got == expected else "FAIL ") + name + f" (expected {expected}, got {got})")
        bad += got != expected
    print("selftest: " + ("FAILED" if bad else "all passed"))
    sys.exit(1 if bad else 0)


def main() -> None:
    ap = argparse.ArgumentParser()
    sub = ap.add_subparsers(dest="cmd", required=True)
    p = sub.add_parser("make-release")
    p.add_argument("--src", required=True)
    p.add_argument("--dest", required=True)
    p.add_argument("--version", required=True)
    p.add_argument("--kind", choices=["additive", "additive-enum", "breaking", "same"], required=True)
    p.add_argument("--index", type=int, default=1)
    p.add_argument("--model", default="System")
    p.add_argument("--remove-attribute", default="DisplayName")
    p.set_defaults(fn=make_release)
    p = sub.add_parser("next-minor")
    p.add_argument("ck_dir")
    p.set_defaults(fn=next_minor)
    p = sub.add_parser("make-dependent-break")
    p.add_argument("--src", required=True)
    p.add_argument("--dest", required=True)
    p.add_argument("--model", required=True)
    p.add_argument("--version", required=True)
    p.add_argument("--remove-attribute", required=True)
    p.set_defaults(fn=make_dependent_break)
    p = sub.add_parser("make-flow")
    p.add_argument("--pipeline", required=True)
    p.add_argument("--out", required=True)
    p.add_argument("--flow-id", default="aaa000000000000000006761")
    p.add_argument("--pipeline-id", default="aaa000000000000000006762")
    p.add_argument("--adapter-id", default="670000000000000000000002")
    p.set_defaults(fn=make_flow)
    p = sub.add_parser("compare")
    p.add_argument("before")
    p.add_argument("after")
    p.add_argument("--max-lines", type=int, default=6)
    p.set_defaults(fn=compare)
    p = sub.add_parser("cascade-count")
    p.add_argument("report")
    p.set_defaults(fn=cascade_count)
    p = sub.add_parser("status-count")
    p.add_argument("report")
    p.set_defaults(fn=status_count)
    p = sub.add_parser("scan-logs")
    p.add_argument("--since", required=True)
    p.add_argument("logs", nargs="+")
    p.set_defaults(fn=scan_logs)
    p = sub.add_parser("adapter-online", help="exit 0 when stdin (GetAdapters -j) lists an Online adapter")
    p.set_defaults(fn=adapter_online)
    p = sub.add_parser("selftest", help="check adapter-online against compact and pretty-printed fixtures (no stack needed)")
    p.set_defaults(fn=selftest)
    args = ap.parse_args()
    args.fn(args)


if __name__ == "__main__":
    main()
