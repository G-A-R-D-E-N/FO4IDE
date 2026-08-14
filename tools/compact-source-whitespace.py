import pathlib
import re
import subprocess
import sys

CSHARP_SUFFIXES = {".cs"}
MARKUP_SUFFIXES = {".csproj", ".props", ".targets", ".xaml", ".xml"}
BACKTICK_SUFFIXES = {".js", ".jsx", ".ts", ".tsx"}
PYTHON_SUFFIXES = {".py"}
POWERSHELL_SUFFIXES = {".ps1"}
SHELL_SUFFIXES = {".sh"}
YAML_SUFFIXES = {".yml", ".yaml"}
PLAIN_SUFFIXES = {".css", ".scss"}

def quote_run(line, start):
    end = start
    while end < len(line) and line[end] == '"':
        end += 1
    return end - start

def raw_string_opens(line):
    index = 0
    state = "code"
    while index < len(line):
        char = line[index]
        next_char = line[index + 1] if index + 1 < len(line) else ""
        if state == "string":
            if char == "\\":
                index += 2
                continue
            if char == '"':
                state = "code"
            index += 1
            continue
        if state == "verbatim":
            if char == '"' and next_char == '"':
                index += 2
                continue
            if char == '"':
                state = "code"
            index += 1
            continue
        if char == "@" and next_char == '"':
            state = "verbatim"
            index += 2
            continue
        if char == '"':
            length = quote_run(line, index)
            if length >= 3:
                closing = index + length
                while closing < len(line):
                    if line[closing] == '"' and quote_run(line, closing) >= length:
                        break
                    closing += 1
                return length if closing == len(line) else 0
            state = "string"
        index += 1
    return 0

def compact_csharp(text):
    output = []
    blank_pending = False
    raw_delimiter = 0
    for line in text.splitlines(keepends=True):
        if raw_delimiter:
            output.append(line)
            if any(quote_run(line, index) >= raw_delimiter for index, char in enumerate(line) if char == '"'):
                raw_delimiter = 0
            continue
        trimmed = line.rstrip(" \t\r\n")
        ending = "\n" if line.endswith("\n") else ""
        normalized = trimmed + ending
        if not trimmed:
            if not blank_pending:
                output.append(ending)
            blank_pending = True
            continue
        blank_pending = False
        output.append(normalized)
        raw_delimiter = raw_string_opens(line)
    return "".join(output)

def compact_markup(text):
    output = []
    blank_pending = False
    for line in text.splitlines(keepends=True):
        trimmed = line.rstrip(" \t\r\n")
        ending = "\n" if line.endswith("\n") else ""
        if not trimmed:
            if not blank_pending:
                output.append(ending)
            blank_pending = True
            continue
        blank_pending = False
        output.append(trimmed + ending)
    return "".join(output)

def compact_plain(text):
    output = []
    blank_pending = False
    for line in text.splitlines(keepends=True):
        trimmed = line.rstrip(" \t\r\n")
        ending = "\n" if line.endswith("\n") else ""
        if not trimmed:
            if not blank_pending:
                output.append(ending)
            blank_pending = True
            continue
        blank_pending = False
        output.append(trimmed + ending)
    return "".join(output)

def compact_backtick_source(text):
    output = []
    blank_pending = False
    in_template = False
    for line in text.splitlines(keepends=True):
        if in_template:
            output.append(line)
            index = 0
        else:
            trimmed = line.rstrip(" \t\r\n")
            ending = "\n" if line.endswith("\n") else ""
            if not trimmed:
                if not blank_pending:
                    output.append(ending)
                blank_pending = True
                continue
            blank_pending = False
            output.append(trimmed + ending)
            index = 0
        while index < len(line):
            if line[index] == "\\":
                index += 2
                continue
            if line[index] == "`":
                in_template = not in_template
            index += 1
    return "".join(output)

def python_triple_opens(line):
    index = 0
    quote = ""
    while index < len(line):
        char = line[index]
        if quote:
            if char == "\\":
                index += 2
                continue
            if char == quote:
                quote = ""
            index += 1
            continue
        candidate = line[index:index + 3]
        if candidate in {"'''", '\"\"\"'}:
            return candidate if line.find(candidate, index + 3) < 0 else ""
        if char in {"'", '"'}:
            quote = char
        index += 1
    return ""

def compact_python(text):
    output = []
    blank_pending = False
    delimiter = ""
    for line in text.splitlines(keepends=True):
        if delimiter:
            output.append(line)
            if line.count(delimiter) % 2:
                delimiter = ""
            continue
        trimmed = line.rstrip(" \t\r\n")
        ending = "\n" if line.endswith("\n") else ""
        if not trimmed:
            if not blank_pending:
                output.append(ending)
            blank_pending = True
            continue
        blank_pending = False
        output.append(trimmed + ending)
        delimiter = python_triple_opens(line)
    return "".join(output)

def compact_powershell(text):
    output = []
    blank_pending = False
    delimiter = ""
    for line in text.splitlines(keepends=True):
        stripped = line.strip()
        if delimiter:
            output.append(line)
            if stripped == delimiter:
                delimiter = ""
            continue
        trimmed = line.rstrip(" \t\r\n")
        ending = "\n" if line.endswith("\n") else ""
        if not trimmed:
            if not blank_pending:
                output.append(ending)
            blank_pending = True
            continue
        blank_pending = False
        output.append(trimmed + ending)
        if stripped.startswith('@"'):
            delimiter = '"@'
        elif stripped.startswith("@'"):
            delimiter = "'@"
    return "".join(output)

def compact_shell(text):
    output = []
    blank_pending = False
    delimiter = ""
    for line in text.splitlines(keepends=True):
        stripped = line.strip()
        if delimiter:
            output.append(line)
            if stripped == delimiter:
                delimiter = ""
            continue
        trimmed = line.rstrip(" \t\r\n")
        ending = "\n" if line.endswith("\n") else ""
        if not trimmed:
            if not blank_pending:
                output.append(ending)
            blank_pending = True
            continue
        blank_pending = False
        output.append(trimmed + ending)
        match = re.search(r"<<-?\\s*['\"]?([A-Za-z_][A-Za-z0-9_]*)", line)
        if match:
            delimiter = match.group(1)
    return "".join(output)

def compact_yaml(text):
    output = []
    blank_pending = False
    block_indent = None
    marker = re.compile(r"^(?P<indent>\\s*)[^#\\s][^:]*:\\s*[>|][+-]?\\s*$")
    for line in text.splitlines(keepends=True):
        stripped = line.strip()
        indent = len(line) - len(line.lstrip(" "))
        if block_indent is not None:
            if not stripped or indent > block_indent:
                output.append(line)
                continue
            block_indent = None
        trimmed = line.rstrip(" \t\r\n")
        ending = "\n" if line.endswith("\n") else ""
        if not trimmed:
            if not blank_pending:
                output.append(ending)
            blank_pending = True
            continue
        blank_pending = False
        output.append(trimmed + ending)
        match = marker.match(trimmed)
        if match:
            block_indent = len(match.group("indent"))
    return "".join(output)

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

def compact_repository(root):
    root = pathlib.Path(root).resolve()
    changed = []
    for relative_path in tracked_files(root):
        suffix = relative_path.suffix.lower()
        supported = (
            CSHARP_SUFFIXES | MARKUP_SUFFIXES | BACKTICK_SUFFIXES | PYTHON_SUFFIXES |
            POWERSHELL_SUFFIXES | SHELL_SUFFIXES | YAML_SUFFIXES | PLAIN_SUFFIXES
        )
        if suffix not in supported:
            continue
        path = root / relative_path
        source = path.read_text(encoding="utf-8", errors="ignore")
        if suffix in CSHARP_SUFFIXES:
            updated = compact_csharp(source)
        elif suffix in MARKUP_SUFFIXES:
            updated = compact_markup(source)
        elif suffix in BACKTICK_SUFFIXES:
            updated = compact_backtick_source(source)
        elif suffix in PYTHON_SUFFIXES:
            updated = compact_python(source)
        elif suffix in POWERSHELL_SUFFIXES:
            updated = compact_powershell(source)
        elif suffix in SHELL_SUFFIXES:
            updated = compact_shell(source)
        elif suffix in YAML_SUFFIXES:
            updated = compact_yaml(source)
        else:
            updated = compact_plain(source)
        if updated != source:
            path.write_text(updated, encoding="utf-8")
            changed.append(relative_path)
    return changed

if __name__ == "__main__":
    target = pathlib.Path(sys.argv[1]) if len(sys.argv) == 2 else pathlib.Path.cwd()
    print(f"compacted_files={len(compact_repository(target))}")
