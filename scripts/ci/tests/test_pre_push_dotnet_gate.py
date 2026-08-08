from __future__ import annotations

import os
import shutil
import subprocess
from pathlib import Path


_ROOT = Path(__file__).resolve().parents[3]


def _git(repository: Path, *args: str) -> str:
    return subprocess.run(
        ["git", *args],
        cwd=repository,
        check=True,
        stdout=subprocess.PIPE,
        text=True,
    ).stdout.strip()


def _bash() -> str:
    if os.name == "nt":
        git_bash = Path(os.environ.get("ProgramFiles", r"C:\Program Files")) / "Git/bin/bash.exe"
        if git_bash.is_file():
            return str(git_bash)
    return "bash"


def test_pre_push_routes_dotnet_changes_only_in_full_ci_mode(tmp_path: Path) -> None:
    repository = tmp_path / "repo"
    repository.mkdir()
    _git(repository, "init", "--initial-branch=dev")
    _git(repository, "config", "user.name", "CI Test")
    _git(repository, "config", "user.email", "ci-test@example.com")

    dispatcher = repository / "scripts/ci/pre_push.sh"
    dispatcher.parent.mkdir(parents=True)
    shutil.copy2(_ROOT / "scripts/ci/pre_push.sh", dispatcher)
    dispatcher.chmod(0o755)

    (repository / "README.md").write_text("baseline\n", encoding="utf-8")
    _git(repository, "add", ".")
    _git(repository, "commit", "-m", "baseline")
    base = _git(repository, "rev-parse", "HEAD")

    changed = repository / "dotnet/src/Ocb.Core/Feature.cs"
    changed.parent.mkdir(parents=True)
    changed.write_text("// .NET change\n", encoding="utf-8")
    _git(repository, "add", ".")
    _git(repository, "commit", "-m", "change dotnet")

    command = [_bash(), str(dispatcher), "--base", base, "--head", "HEAD", "--dry-run"]
    lint_only = subprocess.run(
        command,
        cwd=repository,
        check=True,
        stdout=subprocess.PIPE,
        text=True,
    ).stdout
    full_env = os.environ.copy()
    full_env["OCB_PRE_PUSH_RUN_CI"] = "1"
    full_ci = subprocess.run(
        command,
        cwd=repository,
        env=full_env,
        check=True,
        stdout=subprocess.PIPE,
        text=True,
    ).stdout

    assert "skipped (lint-only)" in lint_only
    assert "scripts/ci/dotnet_ci.sh" in lint_only
    assert "== required:" in full_ci
    assert "scripts/ci/dotnet_ci.sh" in full_ci


def test_dotnet_ci_uses_ignored_cleaned_results_directory() -> None:
    script = (_ROOT / "scripts/ci/dotnet_ci.sh").read_text(encoding="utf-8")

    assert 'test_results="$repo_root/scripts/.dependencies/dotnet/test-results"' in script
    assert 'rm -rf "$test_results"' in script
    assert '--results-directory "$test_results"' in script
