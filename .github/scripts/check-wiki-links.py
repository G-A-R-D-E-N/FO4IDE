#!/usr/bin/env python3
"""Link and manifest check for the FO4IDE GitHub wiki.

Every relative markdown link on every page must resolve to another page in
the wiki. GitHub resolves wiki page names case- and separator-insensitively
(spaces, dashes and underscores are interchangeable in practice), so the
check matches the same way. Exits 1 when any page contains a dead link, so
a scheduled/manual pipeline run can fail on wiki drift.

When given a repository checkout as a second argument, this also verifies the
wiki page set against docs/internal/WIKI_MAP.md.

Usage: python3 check-wiki-links.py <path-to-wiki-checkout> [<repo-checkout>]
"""
import os
import re
import sys
import urllib.parse
from pathlib import Path

def fail_usage() -> None:
    print("Usage: check-wiki-links.py <path-to-wiki-checkout> [<repo-checkout>]")
    sys.exit(2)

if len(sys.argv) < 2:
    fail_usage()

ROOT = Path(sys.argv[1])
REPO_ROOT = Path(sys.argv[2]) if len(sys.argv) > 2 else None

pages = {p.name for p in ROOT.iterdir() if p.is_file() and p.suffix.lower() in {".md", ".markdown"}}
pages_lower = {p.lower() for p in pages}

def norm(name: str) -> str:
    """GitHub-wiki-like normalization: strip .md, fold case and separators."""
    name = os.path.splitext(os.path.basename(name))[0]
    return re.sub(r"[^a-z0-9]+", " ", name.lower()).strip()

LINK_RE = re.compile(r"!?\[[^\]]*\]\(([^)]+)\)")
broken = []

for f in sorted(pages):
    with (ROOT / f).open(encoding="utf-8", errors="replace") as fh:
        for lineno, line in enumerate(fh, 1):
            for target in LINK_RE.findall(line):
                target = target.strip()
                if not target:
                    continue
                if target.startswith(("http://", "https://", "#", "//", "mailto:", "<", ">", "data:")):
                    continue

                target = target.split(" ")[0]
                target = re.split(r"[|#]", target)[0]
                dec = urllib.parse.unquote(target).replace("\\", "/")
                if dec.endswith("/") or dec.startswith("../"):
                    broken.append((f, lineno, target, "directory or escape target"))
                    continue
                name = os.path.splitext(os.path.basename(dec))[0]
                hit = dec in pages or dec.lower() in pages_lower
                if not hit:
                    hit = any(norm(p) == norm(name) for p in pages)
                if not hit:
                    broken.append((f, lineno, target, "no such page"))

def map_section(contents: str, heading_prefix: str) -> str:
    match = re.search(
        rf"^## +{re.escape(heading_prefix)}[^\n]*\n(.*?)(?=^## |\Z)",
        contents,
        flags=re.IGNORECASE | re.MULTILINE | re.DOTALL,
    )
    if not match:
        raise ValueError(f"missing '{heading_prefix}' section")
    return match.group(1)

def parse_wiki_map(map_path: Path) -> tuple[dict[str, str], dict[str, str | None], set[str]]:
    contents = map_path.read_text(encoding="utf-8")
    paired_section = map_section(contents, "Paired")
    wiki_only_section = map_section(contents, "Wiki-only")
    internal_only_section = map_section(contents, "Internal-only")

    paired = {}
    for document, page in re.findall(r"^\|\s*`([^`]+\.md)`\s*\|\s*`?([^|`]+?)`?\s*\|", paired_section, re.MULTILINE):
        paired[document] = page.strip()
    if not paired:
        raise ValueError("paired table has no entries")

    wiki_only = {}
    for page, source in re.findall(r"^\|\s*([^|]+?)\s*\|\s*([^|]+?)\s*\|", wiki_only_section, re.MULTILINE):
        page = page.strip().strip("`")
        if page.lower() == "wiki page" or set(page) == {"-"}:
            continue
        source_match = re.search(r"`([^`]+)`", source)
        wiki_only[page] = source_match.group(1) if source_match else None
    if not wiki_only:
        raise ValueError("wiki-only table has no entries")

    internal_only = set(re.findall(r"`([^`]+\.md)`", internal_only_section))
    if not internal_only:
        raise ValueError("internal-only section has no entries")

    return paired, wiki_only, internal_only

def check_wiki_map(repo_root: Path, wiki_pages: set[str]) -> list[str]:
    map_path = repo_root / "docs" / "internal" / "WIKI_MAP.md"
    if not map_path.is_file():
        return [f"WIKI_MAP.md is missing: {map_path}"]

    try:
        paired, wiki_only, internal_only = parse_wiki_map(map_path)
    except ValueError as error:
        return [f"WIKI_MAP.md is invalid: {error}"]

    errors = []
    internal_dir = map_path.parent
    actual_internal = {path.name for path in internal_dir.glob("*.md")}
    declared_internal = set(paired) | internal_only | {"README.md", "WIKI_MAP.md"}
    actual_wiki = {norm(page): page for page in wiki_pages}
    declared_wiki = {norm(page) for page in paired.values()} | {norm(page) for page in wiki_only}

    for document, page in sorted(paired.items()):
        if document not in actual_internal:
            errors.append(f"paired docs/internal file is missing: {document}")
        if norm(page) not in actual_wiki:
            errors.append(f"paired wiki page is missing: {page}")

    for document in sorted(actual_internal - declared_internal):
        errors.append(f"docs/internal file is not declared: {document}")
    for page in sorted(actual_wiki):
        if page not in declared_wiki:
            errors.append(f"wiki page is not declared: {actual_wiki[page]}")

    for page, source in sorted(wiki_only.items()):
        if norm(page) not in actual_wiki:
            errors.append(f"wiki-only page is missing: {page}")
        if source and not (repo_root / source).is_file():
            errors.append(f"wiki-only source is missing: {source} (for {page})")

    return errors

manifest_errors = check_wiki_map(REPO_ROOT, pages) if REPO_ROOT else []

if broken or manifest_errors:
    if broken:
        print(f"BROKEN WIKI LINKS: {len(broken)}")
        for f, lineno, target, why in broken:
            print(f"  {f}:{lineno}  [{why}] -> {target}")
    if manifest_errors:
        print(f"WIKI MAP MISMATCH: {len(manifest_errors)}")
        for error in manifest_errors:
            print(f"  {error}")
    sys.exit(1)

print(f"Wiki link check passed: {len(pages)} pages, 0 broken links.")
if REPO_ROOT:
    print("Wiki map check passed.")
