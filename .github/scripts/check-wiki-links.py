#!/usr/bin/env python3
"""Link check for the FO4-IDE GitHub wiki.

Every relative markdown link on every page must resolve to another page in
the wiki. GitHub resolves wiki page names case- and separator-insensitively
(spaces, dashes and underscores are interchangeable in practice), so the
check matches the same way. Exits 1 when any page contains a dead link, so
a scheduled/manual pipeline run can fail on wiki drift.

Usage: python3 check-wiki-links.py <path-to-wiki-checkout>
"""
import os
import re
import sys
import urllib.parse

ROOT = sys.argv[1] if len(sys.argv) > 1 else "."

pages = {p for p in os.listdir(ROOT) if p.lower().endswith((".md", ".markdown"))}
pages_lower = {p.lower() for p in pages}


def norm(name: str) -> str:
    """GitHub-wiki-like normalization: strip .md, fold case and separators."""
    name = os.path.basename(name).rstrip(".md")
    return re.sub(r"[^a-z0-9]+", " ", name.lower()).strip()


LINK_RE = re.compile(r"!?\[[^\]]*\]\(([^)]+)\)")
broken = []

for f in sorted(pages):
    with open(os.path.join(ROOT, f), encoding="utf-8", errors="replace") as fh:
        for lineno, line in enumerate(fh, 1):
            for target in LINK_RE.findall(line):
                target = target.strip()
                if not target:
                    continue
                if target.startswith(("http://", "https://", "#", "//", "mailto:", "<", ">", "data:")):
                    continue
                # Strip inline titles, pipe args and trailing anchors.
                target = target.split(" ")[0]
                target = re.split(r"[|#]", target)[0]
                dec = urllib.parse.unquote(target).replace("\\", "/")
                if dec.endswith("/") or dec.startswith("../"):
                    broken.append((f, lineno, target, "directory or escape target"))
                    continue
                name = os.path.basename(dec).rstrip(".md")
                hit = dec in pages or dec.lower() in pages_lower
                if not hit:
                    hit = any(norm(p) == norm(name) for p in pages)
                if not hit:
                    broken.append((f, lineno, target, "no such page"))

if broken:
    print(f"BROKEN WIKI LINKS: {len(broken)}")
    for f, lineno, target, why in broken:
        print(f"  {f}:{lineno}  [{why}] -> {target}")
    sys.exit(1)

print(f"Wiki link check passed: {len(pages)} pages, 0 broken links.")
