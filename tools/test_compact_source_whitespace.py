import importlib.util
import pathlib
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[1]
MODULE_PATH = ROOT / "tools" / "compact-source-whitespace.py"
SPEC = importlib.util.spec_from_file_location("compact_source_whitespace", MODULE_PATH)
if SPEC is None or SPEC.loader is None:
    raise RuntimeError("Unable to load whitespace compactor")
COMPACTOR = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(COMPACTOR)

class CompactSourceWhitespaceTests(unittest.TestCase):
    def test_collapses_blank_code_lines(self):
        source = "public class Example\n{\n\n\n\n    public int Value => 1;\n}\n"

        result = COMPACTOR.compact_csharp(source)

        self.assertEqual("public class Example\n{\n\n    public int Value => 1;\n}\n", result)

    def test_preserves_blank_lines_inside_raw_strings(self):
        source = 'var text = """\nfirst\n\nsecond\n""";\n\n\nreturn text;\n'

        result = COMPACTOR.compact_csharp(source)

        self.assertEqual('var text = """\nfirst\n\nsecond\n""";\n\nreturn text;\n', result)

    def test_collapses_blank_lines_after_inline_raw_strings(self):
        source = 'var text = """value""";\n\n\nreturn text;\n'

        result = COMPACTOR.compact_csharp(source)

        self.assertEqual('var text = """value""";\n\nreturn text;\n', result)

    def test_does_not_treat_verbatim_strings_as_raw_strings(self):
        source = 'var pattern = @"#include\\s+""(?<path>[^""]+)""";\n\n\nreturn pattern;\n'

        result = COMPACTOR.compact_csharp(source)

        self.assertEqual('var pattern = @"#include\\s+""(?<path>[^""]+)""";\n\nreturn pattern;\n', result)

    def test_preserves_blank_lines_inside_template_literals(self):
        source = "const text = `first\n\nsecond`;\n\n\nexport { text };\n"

        result = COMPACTOR.compact_backtick_source(source)

        self.assertEqual("const text = `first\n\nsecond`;\n\nexport { text };\n", result)

    def test_preserves_blank_lines_inside_yaml_blocks(self):
        source = "run: |\n  first\n\n  second\n\n\nname: test\n"

        result = COMPACTOR.compact_yaml(source)

        self.assertEqual("run: |\n  first\n\n  second\n\nname: test\n", result)

    def test_preserves_blank_lines_inside_shell_heredocs(self):
        source = "cat <<EOF\nfirst\n\nsecond\nEOF\n\n\necho done\n"

        result = COMPACTOR.compact_shell(source)

        self.assertEqual("cat <<EOF\nfirst\n\nsecond\nEOF\n\necho done\n", result)

    def test_preserves_blank_lines_inside_python_triple_strings(self):
        source = 'text = """first\n\nsecond"""\n\n\nprint(text)\n'

        result = COMPACTOR.compact_python(source)

        self.assertEqual('text = """first\n\nsecond"""\n\nprint(text)\n', result)

    def test_does_not_treat_triple_quote_text_as_a_python_string(self):
        source = 'delimiter = \'"""\'\n\n\nprint(delimiter)\n'

        result = COMPACTOR.compact_python(source)

        self.assertEqual('delimiter = \'"""\'\n\nprint(delimiter)\n', result)

    def test_preserves_blank_lines_inside_powershell_here_strings(self):
        source = '@"\nfirst\n\nsecond\n"@\n\n\nWrite-Output done\n'

        result = COMPACTOR.compact_powershell(source)

        self.assertEqual('@"\nfirst\n\nsecond\n"@\n\nWrite-Output done\n', result)

if __name__ == "__main__":
    unittest.main(verbosity=2)
