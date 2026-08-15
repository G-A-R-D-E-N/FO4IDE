import dataclasses
import pathlib
import re
import subprocess
import sys


EXCLUDED_PARTS = {
    ".git",
    "Mutagen",
    "TES5Edit-dev-4.1.6",
    "bin",
    "dist",
    "node_modules",
    "obj",
}

C_STYLE_SUFFIXES = {
    ".cs",
    ".cpp",
    ".css",
    ".h",
    ".hpp",
    ".java",
    ".js",
    ".jsx",
    ".json",
    ".jsonc",
    ".scss",
    ".ts",
    ".tsx",
}
MARKUP_SUFFIXES = {".csproj", ".html", ".htm", ".props", ".svg", ".targets", ".xaml", ".xml"}
HASH_SUFFIXES = {".py", ".ps1", ".sh", ".yml", ".yaml"}
PAPYRUS_SUFFIXES = {".psc", ".pas"}
TEXT_SUFFIXES = C_STYLE_SUFFIXES | MARKUP_SUFFIXES | HASH_SUFFIXES | PAPYRUS_SUFFIXES | {
    ".bat",
    ".cmd",
    ".csproj",
    ".md",
    ".props",
    ".targets",
    ".txt",
}

ACTOR = r"(?:ai|artificial\s+intelligence|chatgpt|gpt[-\w.]*|claude|copilot|gemini|openai|anthropic)"
VERB = r"(?:generated|authored|written)"
AI_PATTERNS = (
    re.compile(r"\b" + ACTOR + r"[-\s]*generated\b", re.IGNORECASE),
    re.compile(r"\b" + VERB + r"\s+(?:by|with)\s+" + ACTOR + r"\b", re.IGNORECASE),
    re.compile(r"\bco-authored-by:\s*" + ACTOR + r"\b", re.IGNORECASE),
)


@dataclasses.dataclass(frozen=True)
class Violation:
    path: pathlib.Path
    line: int
    kind: str


def tracked_files(root):
    result = subprocess.run(
        ["git", "ls-files", "-z"],
        cwd=root,
        check=True,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
    )
    for item in result.stdout.split(b"\0"):
        if item:
            yield pathlib.Path(item.decode("utf-8"))


def is_first_party(path):
    return not any(part in EXCLUDED_PARTS for part in path.parts)


def scan_c_style(path, text):
    violations = []
    slash = "/"
    line_comment = slash + slash
    block_start = slash + "*"
    block_end = "*" + slash
    triple_quote = '"' * 3
    block = False
    raw = False
    for line_number, line in enumerate(text.splitlines(), start=1):
        index = 0
        quote = ""
        while index < len(line):
            if raw:
                end = line.find(triple_quote, index)
                if end < 0:
                    break
                raw = False
                index = end + len(triple_quote)
                continue
            if block:
                end = line.find(block_end, index)
                if end < 0:
                    break
                block = False
                index = end + len(block_end)
                continue
            char = line[index]
            pair = line[index:index + 2]
            if quote:
                if quote == "@":
                    if char == '"' and line[index + 1:index + 2] == '"':
                        index += 2
                        continue
                    if char == '"':
                        quote = ""
                elif char == "\\":
                    index += 2
                    continue
                elif char == quote:
                    quote = ""
                index += 1
                continue
            if line.startswith(triple_quote, index):
                raw = True
                index += len(triple_quote)
                continue
            if char == "@" and line[index + 1:index + 2] == '"':
                quote = "@"
                index += 2
                continue
            if char in {'"', "'", "`"}:
                quote = char
                index += 1
                continue
            if pair == line_comment:
                violations.append(Violation(path, line_number, "comment"))
                break
            if pair == block_start:
                violations.append(Violation(path, line_number, "comment"))
                block = True
                index += 2
                continue
            index += 1
    return violations


def scan_markup(path, text):
    marker = "<" + "!--"
    return [
        Violation(path, line_number, "comment")
        for line_number, line in enumerate(text.splitlines(), start=1)
        if marker in line
    ]


def scan_hash(path, text):
    violations = []
    for line_number, line in enumerate(text.splitlines(), start=1):
        stripped = line.lstrip()
        if stripped.startswith("#") and not stripped.startswith("#!"):
            violations.append(Violation(path, line_number, "comment"))
    return violations


def scan_papyrus(path, text):
    return [
        Violation(path, line_number, "comment")
        for line_number, line in enumerate(text.splitlines(), start=1)
        if line.lstrip().startswith(";")
    ]


def scan_ai_attribution(path, text):
    violations = []
    for line_number, line in enumerate(text.splitlines(), start=1):
        if any(pattern.search(line) for pattern in AI_PATTERNS):
            violations.append(Violation(path, line_number, "ai-attribution"))
    return violations


def collect_violations(root):
    root = pathlib.Path(root).resolve()
    violations = []
    for relative_path in tracked_files(root):
        if not is_first_party(relative_path) or relative_path.suffix.lower() not in TEXT_SUFFIXES:
            continue
        text = (root / relative_path).read_text(encoding="utf-8", errors="ignore")
        suffix = relative_path.suffix.lower()
        if suffix in C_STYLE_SUFFIXES:
            violations.extend(scan_c_style(relative_path, text))
        elif suffix in MARKUP_SUFFIXES:
            violations.extend(scan_markup(relative_path, text))
        elif suffix in HASH_SUFFIXES:
            violations.extend(scan_hash(relative_path, text))
        elif suffix in PAPYRUS_SUFFIXES:
            violations.extend(scan_papyrus(relative_path, text))
        violations.extend(scan_ai_attribution(relative_path, text))
    return sorted(violations, key=lambda item: (str(item.path), item.line, item.kind))


def main(root):
    violations = collect_violations(root)
    for violation in violations:
        print(f"{violation.path}:{violation.line}: {violation.kind}")
    return 1 if violations else 0


if __name__ == "__main__":
    target = pathlib.Path(sys.argv[1]) if len(sys.argv) == 2 else pathlib.Path.cwd()
    sys.exit(main(target))
