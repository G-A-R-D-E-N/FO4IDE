from pathlib import Path
import unittest


ROOT = Path(__file__).resolve().parents[1]


class ReleaseAssetLayoutTests(unittest.TestCase):
    def test_release_workflow_builds_windows_and_linux_packages(self) -> None:
        workflow = ROOT / ".github" / "workflows" / "release-assets.yml"
        self.assertTrue(workflow.is_file())

        contents = workflow.read_text(encoding="utf-8")
        self.assertIn("push:", contents)
        self.assertIn("tags:", contents)
        self.assertIn("github.ref_name", contents)
        self.assertIn("ref: main", contents)
        self.assertIn("Publish Windows app", contents)
        self.assertIn("Windows publish failed", contents)
        self.assertIn("Linux package failed", contents)
        self.assertIn("windows-package", contents)
        self.assertIn("linux-package", contents)
        self.assertIn("gh release upload", contents)

    def test_linux_package_uses_the_fo4ide_launcher(self) -> None:
        package = (ROOT / "packaging" / "linux" / "build-deb.sh").read_text(encoding="utf-8")
        package_path = ROOT / "packaging" / "linux" / "build-deb.sh"
        desktop = (ROOT / "packaging" / "linux" / "fo4recordeditor.desktop").read_text(encoding="utf-8")

        self.assertIn("Package: fo4ide", package)
        self.assertIn('"$STAGE/usr/bin/fo4ide"', package)
        self.assertIn("Exec=fo4ide", desktop)
        self.assertNotEqual(package_path.stat().st_mode & 0o111, 0)

    def test_windows_package_can_stage_a_runner_publish(self) -> None:
        package = (ROOT / "package.ps1").read_text(encoding="utf-8")

        self.assertIn("SkipPublish", package)
        self.assertNotIn("<#", package)


if __name__ == "__main__":
    unittest.main()
