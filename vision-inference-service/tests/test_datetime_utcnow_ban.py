"""
Tests for Task 1 — datetime.utcnow() deprecation ban.

Python 3.12 emits DeprecationWarning for datetime.utcnow() calls and
datetime.utcfromtimestamp(). Python 3.14 will remove them entirely.
These tests use AST analysis to verify every .py file in the service
is free of the deprecated calls, catching any future regression
before it becomes a runtime warning.
"""
from __future__ import annotations

import ast
import os
from pathlib import Path
from typing import Generator

import pytest

SERVICE_ROOT = Path(__file__).parent.parent


def _iter_py_files() -> Generator[Path, None, None]:
    """Yield all .py files in the vision-inference-service (excluding tests/,
    __pycache__, and any virtual environment directories)."""
    for p in SERVICE_ROOT.rglob("*.py"):
        parts = p.parts
        # Skip test files, bytecode caches, and virtual environments
        if "__pycache__" in parts:
            continue
        if p.parts[-2] == "tests":
            continue
        # Skip any standard venv directories
        if any(part in (".venv", "venv", "env", ".env", "site-packages") for part in parts):
            continue
        yield p


def _find_utcnow_calls(path: Path) -> list[tuple[int, str]]:
    """Return (lineno, line) tuples for any datetime.utcnow() call in *path*."""
    try:
        src = path.read_text(encoding="utf-8")
    except (UnicodeDecodeError, OSError):
        return []

    hits: list[tuple[int, str]] = []
    try:
        tree = ast.parse(src, filename=str(path))
    except SyntaxError:
        return []

    lines = src.splitlines()

    for node in ast.walk(tree):
        # Match: datetime.utcnow()  or  <anything>.utcnow()
        if (
            isinstance(node, ast.Call)
            and isinstance(node.func, ast.Attribute)
            and node.func.attr == "utcnow"
        ):
            hits.append((node.lineno, lines[node.lineno - 1].strip()))

        # Also catch: datetime.utcfromtimestamp(...)
        if (
            isinstance(node, ast.Call)
            and isinstance(node.func, ast.Attribute)
            and node.func.attr == "utcfromtimestamp"
        ):
            hits.append((node.lineno, lines[node.lineno - 1].strip()))

    return hits


# ---------------------------------------------------------------------------
# Parametrize over every source file so each shows as a separate test case
# ---------------------------------------------------------------------------
_SOURCE_FILES = sorted(_iter_py_files())


@pytest.mark.parametrize("path", _SOURCE_FILES, ids=lambda p: str(p.relative_to(SERVICE_ROOT)))
def test_no_utcnow_in_source_file(path: Path):
    """Every source file must be free of datetime.utcnow() / utcfromtimestamp()."""
    hits = _find_utcnow_calls(path)
    assert not hits, (
        f"{path.relative_to(SERVICE_ROOT)} still uses deprecated datetime call(s):\n"
        + "\n".join(f"  line {ln}: {code}" for ln, code in hits)
        + "\n\nReplace with datetime.now(timezone.utc) or "
        "datetime.fromtimestamp(..., tz=timezone.utc)."
    )


def test_modern_utc_pattern_present_in_main():
    """main.py must use datetime.now(timezone.utc) somewhere (positive canary)."""
    src = (SERVICE_ROOT / "main.py").read_text()
    assert "timezone.utc" in src, (
        "main.py should use datetime.now(timezone.utc) — "
        "timezone.utc not found; check that the fix was applied."
    )


def test_timezone_imported_in_main():
    """main.py must import timezone from datetime for the fix to compile."""
    src = (SERVICE_ROOT / "main.py").read_text()
    # Accept any form: `from datetime import datetime, timezone` or
    # `from datetime import ..., timezone, ...`
    assert "timezone" in src and "from datetime import" in src, (
        "main.py must have 'from datetime import ..., timezone' in its imports."
    )
