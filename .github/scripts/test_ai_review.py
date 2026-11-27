"""Тесты ревью-скрипта без обращения к API: python -m unittest discover -s .github/scripts"""

import os
import tempfile
import unittest
from pathlib import Path
from unittest import mock

import ai_review as r

SMALL_DIFF = """diff --git a/RentalService/Controllers/A.cs b/RentalService/Controllers/A.cs
index 1..2 100644
--- a/RentalService/Controllers/A.cs
+++ b/RentalService/Controllers/A.cs
@@ -1,2 +1,3 @@
 line
+added
-removed
diff --git a/RentalService/bin/Debug/x.dll b/RentalService/bin/Debug/x.dll
Binary files a/RentalService/bin/Debug/x.dll and b/RentalService/bin/Debug/x.dll differ
diff --git a/yarn.lock b/yarn.lock
--- a/yarn.lock
+++ b/yarn.lock
@@ -1 +1 @@
-a
+b
diff --git a/img/logo.png b/img/logo.png
Binary files a/img/logo.png and b/img/logo.png differ
diff --git a/RentalService/Migrations/2026_Init.cs b/RentalService/Migrations/2026_Init.cs
+++ b/RentalService/Migrations/2026_Init.cs
+generated
diff --git a/src/generated/Api.cs b/src/generated/Api.cs
+++ b/src/generated/Api.cs
+generated
"""


class Base(unittest.TestCase):
    def setUp(self):
        self.tmp = Path(tempfile.mkdtemp())
        self.instr = self.tmp / "instr.md"
        self.instr.write_text("SEC-1 ...", encoding="utf-8")

    def run_with(self, diff_text):
        d = self.tmp / "diff.txt"
        d.write_text(diff_text, encoding="utf-8")
        return r.run(d, self.instr, None)


class FilterTests(Base):
    def test_ignores_lock_bin_binary_migrations_generated(self):
        kept, skipped = r.filter_files(r.parse_diff(SMALL_DIFF))
        self.assertEqual([f.path for f in kept], ["RentalService/Controllers/A.cs"])
        self.assertEqual(len(skipped), 5)
        self.assertEqual((kept[0].added, kept[0].removed), (1, 1))

    def test_empty_diff(self):
        md, comments = self.run_with("")
        self.assertIn("нечего проверять", md)
        self.assertEqual(comments, [])


class ThresholdTests(Base):
    def test_big_diff_skips_model(self):
        big = "diff --git a/a.cs b/a.cs\n+++ b/a.cs\n" + "+x\n" * (r.MAX_CHANGED_LINES + 1)
        with mock.patch.object(r, "call_model") as cm:
            md, _ = self.run_with(big)
        cm.assert_not_called()
        self.assertIn("слишком большой", md)
        self.assertIn(str(r.MAX_CHANGED_LINES + 1), md)


class FallbackTests(Base):
    def test_no_api_key(self):
        with mock.patch.dict(os.environ, {"OPENROUTER_API_KEY": ""}):
            md, comments = self.run_with(SMALL_DIFF)
        self.assertIsNone(comments)
        self.assertIn("не выполнено", md)

    def test_garbage_response(self):
        with mock.patch.object(r, "call_model", return_value="Sorry, I can't <html>"):
            md, comments = self.run_with(SMALL_DIFF)
        self.assertIsNone(comments)
        self.assertIn("невалидный ответ", md)

    def test_model_error_timeout(self):
        with mock.patch.object(r, "call_model", side_effect=r.ModelError("таймаут")):
            md, _ = self.run_with(SMALL_DIFF)
        self.assertIn("таймаут", md)

    def test_json_in_fence_and_bad_items(self):
        text = 'Вот ответ:\n```json\n{"comments": [' \
               '{"file": "A.cs", "line": "7", "severity": "CRITICAL", "message": "m", "reason": "r"},' \
               '{"file": "", "message": "no file"}, 42]}\n```'
        comments = r.parse_comments(text)
        self.assertEqual(len(comments), 1)
        self.assertEqual(comments[0]["line"], 7)
        self.assertEqual(comments[0]["severity"], "warning")
        self.assertEqual(comments[0]["rule"], "OTHER")

    def test_valid_response_rendered(self):
        payload = '{"comments": [{"file": "A.cs", "line": 3, "severity": "error", "rule": "SEC-1",' \
                  ' "message": "SQL | injection", "reason": "concat"}]}'
        with mock.patch.object(r, "call_model", return_value=payload):
            md, comments = self.run_with(SMALL_DIFF)
        self.assertEqual(len(comments), 1)
        self.assertIn("SEC-1", md)
        self.assertIn("SQL \\| injection", md)
        self.assertTrue(md.startswith(r.MARKER))

    def test_main_never_fails(self):
        out = self.tmp / "out.md"
        argv = ["x", "--diff", str(self.tmp / "missing.txt"), "--instructions", str(self.instr), "--out", str(out)]
        with mock.patch("sys.argv", argv):
            self.assertEqual(r.main(), 0)
        self.assertIn("Не удалось прочитать diff", out.read_text(encoding="utf-8"))


if __name__ == "__main__":
    unittest.main()
