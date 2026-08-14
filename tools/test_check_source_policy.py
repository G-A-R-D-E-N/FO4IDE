import importlib.util
import pathlib
import subprocess
import tempfile
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[1]
MODULE_PATH = ROOT / "tools" / "check-source-policy.py"
SPEC = importlib.util.spec_from_file_location("check_source_policy", MODULE_PATH)
if SPEC is None or SPEC.loader is None:
    raise RuntimeError("Unable to load source policy checker")
POLICY = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(POLICY)

class SourcePolicyTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = pathlib.Path(self.temp.name)
        subprocess.run(["git", "init", "-q"], cwd=self.root, check=True)

    def tearDown(self):
        self.temp.cleanup()

    def write(self, relative_path, content):
        path = self.root / relative_path
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(content, encoding="utf-8")
        subprocess.run(["git", "add", relative_path], cwd=self.root, check=True)

    def violations(self):
        return POLICY.collect_violations(self.root)

    def test_rejects_csharp_line_comments(self):
        self.write("Program.cs", "class Program { } " + "/" + "/ prohibited\n")

        violations = self.violations()

        self.assertEqual(1, len(violations))
        self.assertEqual("comment", violations[0].kind)

    def test_rejects_explicit_ai_authorship(self):
        self.write("Program.cs", "string source = \"AI" + "-generated\";\n")

        violations = self.violations()

        self.assertEqual(1, len(violations))
        self.assertEqual("ai-attribution", violations[0].kind)

    def test_allows_urls_and_compiler_directives(self):
        url = "https:" + "/" + "/example.invalid"
        self.write("Program.cs", "#if DEBUG\nstring url = \"" + url + "\";\n#endif\n")

        self.assertEqual([], self.violations())

    def test_rejects_markup_project_shell_and_papyrus_comments(self):
        markup = "<" + "!-- prohibited -->\n"
        shell = "# prohibited\n"
        papyrus = "; prohibited\n"
        self.write("view.xaml", markup)
        self.write("Project.csproj", markup)
        self.write("build.sh", shell)
        self.write("script.psc", papyrus)

        violations = self.violations()

        self.assertEqual(4, len(violations))
        self.assertTrue(all(violation.kind == "comment" for violation in violations))

    def test_scans_vendored_csharp_tree(self):
        self.write("Mutagen/Generated.cs", "class Generated { } " + "/" + "/ retained\n")

        violations = self.violations()

        self.assertEqual(1, len(violations))
        self.assertEqual("comment", violations[0].kind)

if __name__ == "__main__":
    unittest.main(verbosity=2)
