from pathlib import Path


_ROOT = Path(__file__).resolve().parents[3]


def test_unit_test_workflow_has_dotnet_job() -> None:
    workflow = (_ROOT / ".github/workflows/unit-tests.yml").read_text(encoding="utf-8")

    assert workflow.count("  dotnet:\n") == 1
    assert "DOTNET_BASE_REF: ${{ github.event_name == 'pull_request' && 'HEAD^1' || 'origin/dev' }}" in workflow
    assert "actions/setup-dotnet@v4" in workflow
    assert "10.0.302" in workflow
    assert "astral-sh/setup-uv@v5" in workflow[workflow.index("  dotnet:\n"):workflow.index("  gateway:\n")]
    assert "bash scripts/ci/dotnet_ci.sh" in workflow
    for path in (
        "dotnet",
        "scripts/dotnet",
        "docs/arch/arch.rules.md",
        "docs/arch/ci.enforce.md",
        "docs/arch/context-boundary-format.md",
        "docs/arch/protocol-contract-tests.md",
    ):
        assert path in workflow[workflow.index("  dotnet:\n"):workflow.index("  gateway:\n")]
