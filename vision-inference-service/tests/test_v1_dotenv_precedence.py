"""
V1 acceptance tests — dotenv precedence in config.py.

Contract (highest wins):
    real process environment  >  .env.local  >  .env

Each test spawns a fresh interpreter in a temp cwd because config.py reads
the dotenv files exactly once at import time.
"""
import os
import subprocess
import sys

SERVICE_DIR = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))

_SNIPPET = (
    "import os, sys; sys.path.insert(0, {svc!r}); import config; "
    "print('RESULT=' + os.environ.get('VISION_TEST_PRECEDENCE', '<unset>') "
    "+ '|TESTING_MODE=' + str(config.TESTING_MODE))"
)


def _run(tmp_path, env_file=None, env_local=None, extra_env=None):
    if env_file is not None:
        (tmp_path / ".env").write_text(env_file)
    if env_local is not None:
        (tmp_path / ".env.local").write_text(env_local)

    env = dict(os.environ)
    env.pop("VISION_TEST_PRECEDENCE", None)
    env["TESTING_MODE"] = "true"  # keep config import side-effect free
    if extra_env:
        env.update(extra_env)

    proc = subprocess.run(
        [sys.executable, "-c", _SNIPPET.format(svc=SERVICE_DIR)],
        cwd=str(tmp_path),
        env=env,
        capture_output=True,
        text=True,
        timeout=60,
    )
    assert proc.returncode == 0, f"config import failed:\n{proc.stderr}"
    result_lines = [l for l in proc.stdout.splitlines() if l.startswith("RESULT=")]
    assert result_lines, f"no RESULT line in stdout: {proc.stdout!r}"
    return result_lines[-1]


def test_env_local_overrides_env(tmp_path):
    out = _run(
        tmp_path,
        env_file="VISION_TEST_PRECEDENCE=from_env\n",
        env_local="VISION_TEST_PRECEDENCE=from_env_local\n",
    )
    assert "RESULT=from_env_local|" in out


def test_env_used_when_no_local_file(tmp_path):
    out = _run(tmp_path, env_file="VISION_TEST_PRECEDENCE=from_env\n")
    assert "RESULT=from_env|" in out


def test_env_local_used_when_no_env_file(tmp_path):
    out = _run(tmp_path, env_local="VISION_TEST_PRECEDENCE=from_env_local\n")
    assert "RESULT=from_env_local|" in out


def test_process_env_wins_over_both_files(tmp_path):
    """Negative case: injected env vars (AppHost/K8s) beat BOTH dotenv files."""
    out = _run(
        tmp_path,
        env_file="VISION_TEST_PRECEDENCE=from_env\n",
        env_local="VISION_TEST_PRECEDENCE=from_env_local\n",
        extra_env={"VISION_TEST_PRECEDENCE": "from_process"},
    )
    assert "RESULT=from_process|" in out


def test_process_env_wins_for_config_flags(tmp_path):
    """TESTING_MODE injected in the process env must not be flipped by .env.local."""
    out = _run(
        tmp_path,
        env_file="TESTING_MODE=false\n",
        env_local="TESTING_MODE=false\n",
        extra_env={"TESTING_MODE": "true"},
    )
    assert "TESTING_MODE=True" in out


def test_no_dotenv_files_is_harmless(tmp_path):
    out = _run(tmp_path)
    assert "RESULT=<unset>|" in out
