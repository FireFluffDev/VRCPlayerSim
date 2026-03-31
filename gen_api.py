"""Auto-generate API.md from VRCSim C# source files.

Parses public static members + XML doc comments from Runtime/*.cs
and produces a markdown API reference that agents can read at runtime.

Usage:
    uv run gen_api.py          # manual
    (or via pre-commit hook)   # automatic
"""
import re
from pathlib import Path

ROOT = Path(__file__).parent
RUNTIME = ROOT / "Runtime"
OUTPUT = ROOT / "API.md"

SOURCE_FILES = ["VRCSim.cs", "VRCSim.Testing.cs", "SimProxy.cs", "SimProxy.GameObject.cs", "SimNetwork.cs", "SimReflection.cs", "SimSnapshot.cs", "VarState.cs", "BotControlWindow.cs"]

SECTION_RE = re.compile(r"^//\s*\u2500\u2500\s*(.+?)\s*\u2500")
SUMMARY_RE = re.compile(r"<summary>(.*?)</summary>", re.DOTALL)


def collect_doc_comment(lines, member_line):
    """Walk backwards from a member to extract its /// <summary> block."""
    doc_lines = []
    j = member_line - 1
    while j >= 0 and lines[j].strip().startswith("///"):
        doc_lines.insert(0, lines[j].strip().lstrip("/").strip())
        j -= 1
    if not doc_lines:
        return ""
    text = " ".join(doc_lines)
    match = SUMMARY_RE.search(text)
    if not match:
        return ""
    return re.sub(r"\s+", " ", match.group(1)).strip()


def collect_full_line(lines, start):
    """Collect a logical line that may be split across physical lines."""
    parts = []
    paren_depth = 0
    for k in range(start, min(start + 8, len(lines))):
        s = lines[k].strip()
        parts.append(s)
        paren_depth += s.count("(") - s.count(")")
        if paren_depth <= 0 and any(c in s for c in ["{", "=>", ";"]):
            break
    return " ".join(parts)


def parse_signature(full_line):
    """Extract name, return type, params from a public member declaration."""
    sig = re.split(r"\s*\{", full_line)[0]
    sig = re.split(r"\s*=>", sig)[0]
    sig = sig.rstrip(";").strip()

    sig = re.sub(r"^public\s+", "", sig)
    sig = re.sub(r"^(static|override)\s+", "", sig)
    sig = re.sub(r"^(static|override)\s+", "", sig)

    if "(" in sig:
        name_match = re.match(r"(.+?)\s+(\w+)\s*\(", sig)
        if not name_match:
            return None
        ret_type = name_match.group(1).strip()
        name = name_match.group(2)
        first_paren = sig.index("(")
        last_paren = sig.rindex(")")
        params = sig[first_paren + 1 : last_paren].strip()
        return {
            "return_type": ret_type,
            "name": name,
            "params": params,
            "is_property": False,
        }
    else:
        m = re.match(r"(.+?)\s+(\w+)\s*$", sig)
        if not m:
            return None
        return {
            "return_type": m.group(1).strip(),
            "name": m.group(2),
            "params": None,
            "is_property": True,
        }



def parse_nested_types(lines):
    """Extract public structs (with fields) and enums from a class body."""
    types = []
    i = 0
    while i < len(lines):
        stripped = lines[i].strip()

        # Match public struct
        sm = re.match(r"public\s+struct\s+(\w+)", stripped)
        if sm:
            name = sm.group(1)
            summary = collect_doc_comment(lines, i)
            fields = []
            depth = 0
            for j in range(i, min(i + 50, len(lines))):
                s = lines[j].strip()
                depth += s.count("{") - s.count("}")
                fm = re.match(r"public\s+(?!override)(\S+)\s+(\w+)\s*;", s)
                if fm:
                    fields.append((fm.group(1), fm.group(2)))
                if depth <= 0 and j > i:
                    break
            types.append(
                {"kind": "struct", "name": name, "summary": summary,
                 "fields": fields}
            )

        # Match public enum
        em = re.match(r"public\s+enum\s+(\w+)", stripped)
        if em:
            name = em.group(1)
            summary = collect_doc_comment(lines, i)
            # Collect all lines of the enum body
            body_lines = []
            depth = 0
            for j in range(i, min(i + 30, len(lines))):
                s = lines[j].strip()
                depth += s.count("{") - s.count("}")
                body_lines.append(s)
                if depth <= 0 and j > i:
                    break
            # Extract values from between braces
            body = " ".join(body_lines)
            brace_content = re.search(r"\{(.+?)\}", body)
            values = []
            if brace_content:
                for v in brace_content.group(1).split(","):
                    v = v.strip().split("=")[0].strip()
                    if v and v.isidentifier():
                        values.append(v)
            types.append(
                {"kind": "enum", "name": name, "summary": summary,
                 "values": values}
            )
        i += 1
    return types


def parse_class(path):
    """Parse a C# file -> (class_name, class_summary, members, nested_types)."""
    text = path.read_text(encoding="utf-8")
    lines = text.split("\n")

    cm = re.search(r"(?:public\s+)?(?:static\s+)?class\s+(\w+)", text)
    class_name = cm.group(1) if cm else path.stem

    class_summary = ""
    for i, line in enumerate(lines):
        if f"class {class_name}" in line:
            class_summary = collect_doc_comment(lines, i)
            break

    members = []
    nested_types = parse_nested_types(lines)
    current_section = "General"
    brace_depth = 0
    in_class = False
    class_body_depth = 99

    for i, line in enumerate(lines):
        stripped = line.strip()
        brace_depth += stripped.count("{") - stripped.count("}")

        if not in_class and f"class {class_name}" in stripped:
            in_class = True
            # The opening { for this class is on the next line,
            # so class body depth will be current + 1
            class_body_depth = brace_depth + 1
            continue

        if not in_class:
            continue

        # Section headers only at class body level (not inside methods)
        sec = SECTION_RE.match(stripped)
        if sec and brace_depth <= class_body_depth:
            name = sec.group(1).strip()
            if "Private" in name or name == "Helpers":
                current_section = "_private"
            else:
                current_section = name
            continue

        if not stripped.startswith("public static"):
            continue
        if re.match(r"public\s+(static\s+)?class\s", stripped):
            continue
        if re.match(r"public\s+(enum|struct|interface)\s", stripped):
            continue
        if current_section == "_private":
            continue

        full = collect_full_line(lines, i)
        parsed = parse_signature(full)
        if not parsed:
            continue

        summary = collect_doc_comment(lines, i)
        members.append({"section": current_section, "summary": summary, **parsed})

    return class_name, class_summary, members, nested_types


def format_sig(m):
    """Format a member as a readable signature string."""
    if m["is_property"]:
        return f"{m['return_type']} {m['name']}"
    return f"{m['return_type']} {m['name']}({m['params'] or ''})"


def generate_md():
    """Generate the full API.md content."""
    out = [
        "# VRCSim API Reference",
        "",
        "> **Auto-generated from source code.** Do not edit manually.",
        "> Regenerate: `uv run gen_api.py` (or commit — pre-commit hook does it).",
        "",
    ]

    for filename in SOURCE_FILES:
        path = RUNTIME / filename
        if not path.exists():
            continue

        class_name, class_summary, members, nested_types = parse_class(path)
        if not members:
            continue

        out.append(f"## `VRCSim.{class_name}`")
        if class_summary:
            out.append("")
            out.append(class_summary)
        out.append("")

        sections = {}
        for m in members:
            sections.setdefault(m["section"], []).append(m)

        for section, mems in sections.items():
            out.append(f"### {section}")
            out.append("")
            out.append("| Signature | Description |")
            out.append("|-----------|-------------|")
            for m in mems:
                sig = format_sig(m).replace("|", "\\|")
                desc = (m["summary"] or "\u2014").replace("|", "\\|")
                out.append(f"| `{sig}` | {desc} |")
            out.append("")

        # Render nested types (structs, enums)
        if nested_types:
            out.append("### Types")
            out.append("")
            for nt in nested_types:
                if nt["kind"] == "struct":
                    out.append(f"**`{nt['name']}`** (struct)")
                    if nt["summary"]:
                        out.append(f" \u2014 {nt['summary']}")
                    out.append("")
                    out.append("| Field | Type |")
                    out.append("|-------|------|")
                    for ftype, fname in nt["fields"]:
                        out.append(f"| `{fname}` | `{ftype}` |")
                    out.append("")
                elif nt["kind"] == "enum":
                    out.append(f"**`{nt['name']}`** (enum)")
                    if nt["summary"]:
                        out.append(f" \u2014 {nt['summary']}")
                    out.append("")
                    out.append(f"Values: {', '.join(f'`{v}`' for v in nt['values'])}")
                    out.append("")

    return "\n".join(out)


def main():
    md = generate_md()
    old = OUTPUT.read_text(encoding="utf-8") if OUTPUT.exists() else ""
    if md == old:
        print("API.md is up to date.")
        return
    OUTPUT.write_text(md, encoding="utf-8")
    print(f"API.md updated ({len(md)} bytes).")


if __name__ == "__main__":
    main()
