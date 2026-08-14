#!/usr/bin/env python3
"""Regression tests for the wiki link and manifest checker."""

from __future__ import annotations

import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

SCRIPT = Path(__file__).with_name("check-wiki-links.py")

MAP = """\




| docs/internal | wiki page | notes |
|---|---|---|
| `PAIRED.md` | PAIRED | |



| wiki page | source in the repo |
|---|---|
| Home | wiki landing page; no repo file |
| README | root `README.md` |



`INTERNAL.md`
"""

class WikiMapCheckTests(unittest.TestCase):
    def make_layout(
        self,
        *,
        wiki_pages: tuple[str, ...] = ("PAIRED.md", "Home.md", "README.md"),
        internal_docs: tuple[str, ...] = ("PAIRED.md", "INTERNAL.md", "README.md"),
        root_files: tuple[str, ...] = ("README.md",),
    ) -> tuple[tempfile.TemporaryDirectory[str], Path, Path]:
        temporary = tempfile.TemporaryDirectory()
        root = Path(temporary.name)
        wiki = root / "wiki"
        internal = root / "repo" / "docs" / "internal"
        wiki.mkdir(parents=True)
        internal.mkdir(parents=True)

        for page in wiki_pages:
            (wiki / page).write_text("# Page\n", encoding="utf-8")
        for document in internal_docs:
            (internal / document).write_text("# Document\n", encoding="utf-8")
        for source in root_files:
            path = root / "repo" / source
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text("# Source\n", encoding="utf-8")
        (internal / "WIKI_MAP.md").write_text(MAP, encoding="utf-8")
        return temporary, wiki, root / "repo"

    def run_checker(self, wiki: Path, repo: Path) -> subprocess.CompletedProcess[str]:
        return subprocess.run(
            [sys.executable, str(SCRIPT), str(wiki), str(repo)],
            capture_output=True,
            check=False,
            text=True,
        )

    def test_rejects_an_unlisted_wiki_page(self) -> None:
        temporary, wiki, repo = self.make_layout(
            wiki_pages=("PAIRED.md", "Home.md", "README.md", "UNLISTED.md")
        )
        with temporary:
            result = self.run_checker(wiki, repo)

        self.assertNotEqual(result.returncode, 0)
        self.assertIn("wiki page is not declared", result.stdout)

    def test_rejects_an_unlisted_internal_document(self) -> None:
        temporary, wiki, repo = self.make_layout(
            internal_docs=("PAIRED.md", "INTERNAL.md", "README.md", "UNLISTED.md")
        )
        with temporary:
            result = self.run_checker(wiki, repo)

        self.assertNotEqual(result.returncode, 0)
        self.assertIn("docs/internal file is not declared", result.stdout)

    def test_rejects_a_missing_paired_wiki_page(self) -> None:
        temporary, wiki, repo = self.make_layout(wiki_pages=("Home.md", "README.md"))
        with temporary:
            result = self.run_checker(wiki, repo)

        self.assertNotEqual(result.returncode, 0)
        self.assertIn("paired wiki page is missing", result.stdout)

    def test_rejects_a_missing_paired_internal_document(self) -> None:
        temporary, wiki, repo = self.make_layout(internal_docs=("INTERNAL.md", "README.md"))
        with temporary:
            result = self.run_checker(wiki, repo)

        self.assertNotEqual(result.returncode, 0)
        self.assertIn("paired docs/internal file is missing", result.stdout)

    def test_rejects_a_missing_wiki_only_source(self) -> None:
        temporary, wiki, repo = self.make_layout(root_files=())
        with temporary:
            result = self.run_checker(wiki, repo)

        self.assertNotEqual(result.returncode, 0)
        self.assertIn("wiki-only source is missing", result.stdout)

if __name__ == "__main__":
    unittest.main()
