from __future__ import annotations

import hashlib
import importlib.util
import json
import os
import subprocess
from pathlib import Path

import pytest


_REPO = Path(__file__).parents[3]
_CORPUS = _REPO / "dotnet" / "contracts" / "parity-corpus"


def _load_dump_module(name: str):
    path = _REPO / "scripts" / "dotnet" / f"dump_{name}_openapi.py"
    spec = importlib.util.spec_from_file_location(f"dump_{name}_openapi", path)
    assert spec and spec.loader
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def _assert_utf8_lf_no_bom(path: Path) -> None:
    payload = path.read_bytes()
    assert not payload.startswith(b"\xef\xbb\xbf"), f"{path} must be UTF-8 without BOM"
    assert b"\r\n" not in payload, f"{path} must use LF line endings"


def _normalized_lf_text(payload: bytes) -> str:
    return payload.decode("utf-8").replace("\r\n", "\n").replace("\r", "\n")


def test_manifest_hashes_match_committed_artifacts() -> None:
    manifest = json.loads((_CORPUS / "manifest.json").read_text(encoding="utf-8"))

    assert manifest["version"] == "ocb-parity-corpus-v1"
    assert {entry["service"] for entry in manifest["artifacts"]} == {
        "backend",
        "engine",
        "baas",
        "gateway",
        "bcsfuse",
    }
    assert {(entry["service"], entry["kind"]) for entry in manifest["artifacts"]} >= {
        ("engine", "openapi"),
        ("engine", "websocket"),
        ("gateway", "openapi"),
    }

    files = [entry["file"] for entry in manifest["artifacts"]]
    assert len(files) == len(set(files))
    assert manifest["artifacts"] == sorted(
        manifest["artifacts"],
        key=lambda item: (item["service"], item["kind"], item["file"]),
    )

    _assert_utf8_lf_no_bom(_CORPUS / "manifest.json")

    for entry in manifest["artifacts"]:
        artifact = _CORPUS / entry["file"]
        payload = artifact.read_bytes()
        assert hashlib.sha256(payload).hexdigest() == entry["sha256"]
        assert (_REPO / entry["source"]).is_file()
        if entry["source"].startswith("scripts/dotnet/dump_"):
            assert entry["inputs"]
            assert all((_REPO / source).is_file() for source in entry["inputs"])
        _assert_utf8_lf_no_bom(artifact)


def test_generated_openapi_matches_committed_corpus(tmp_path: Path) -> None:
    cases = [
        (
            _REPO / "src/backend",
            ["uv", "run", "python", "scripts/dump_openapi.py"],
            "backend.openapi.json",
            {"DEPLOY_PROFILE": "community"},
        ),
        (
            _REPO / "src/baas",
            ["uv", "run", "python", "scripts/dump_openapi.py"],
            "baas.openapi.json",
            {},
        ),
        (
            _REPO,
            ["uv", "run", "python", "scripts/dotnet/dump_engine_openapi.py"],
            "engine.openapi.json",
            {},
        ),
        (
            _REPO,
            ["uv", "run", "python", "scripts/dotnet/dump_gateway_openapi.py"],
            "gateway.openapi.json",
            {},
        ),
    ]
    for cwd, command, artifact, extra_env in cases:
        generated = tmp_path / artifact
        env = os.environ.copy()
        env.update(extra_env)
        subprocess.run([*command, str(generated)], cwd=cwd, check=True, env=env)
        assert generated.read_bytes() == (_CORPUS / artifact).read_bytes()


def test_new_dump_scripts_are_byte_stable(tmp_path: Path) -> None:
    scripts = [
        "dump_engine_openapi.py",
        "dump_gateway_openapi.py",
    ]

    for script_name in scripts:
        first = tmp_path / f"first-{script_name}.json"
        second = tmp_path / f"second-{script_name}.json"
        script = ["uv", "run", "python", f"scripts/dotnet/{script_name}"]
        subprocess.run([*script, str(first)], cwd=_REPO, check=True)
        subprocess.run([*script, str(second)], cwd=_REPO, check=True)
        assert first.read_bytes() == second.read_bytes(), script_name


@pytest.mark.parametrize("name", ["engine", "gateway"])
def test_new_dump_scripts_clean_temporary_files_on_failure(
    name: str, tmp_path: Path, monkeypatch
) -> None:
    module = _load_dump_module(name)
    target = tmp_path / f"{name}.openapi.json"

    def fail_second_dump(path: Path) -> None:
        if path.suffix == ".first":
            path.write_bytes(b"first")
            return
        path.write_bytes(b"second")
        raise RuntimeError("second export failed")

    monkeypatch.setattr(module, "_dump_once", fail_second_dump)

    with pytest.raises(RuntimeError, match="second export failed"):
        getattr(module, f"dump_{name}_openapi")(target)

    assert not target.with_suffix(target.suffix + ".first").exists()
    assert not target.with_suffix(target.suffix + ".second").exists()


def test_gateway_dump_does_not_inject_signing_key(tmp_path: Path, monkeypatch) -> None:
    module = _load_dump_module("gateway")
    captured_env: dict[str, str] = {}

    def capture_run(*args, **kwargs) -> None:
        captured_env.update(kwargs["env"])

    monkeypatch.delenv("AVERNET_SECRET_PRINCIPAL_SIGNING_KEY_VALUE", raising=False)
    monkeypatch.setattr(module.subprocess, "run", capture_run)

    module._dump_once(tmp_path / "gateway.openapi.json")

    assert "AVERNET_SECRET_PRINCIPAL_SIGNING_KEY_VALUE" not in captured_env


def test_gateway_committed_openapi_is_real_served_schema() -> None:
    gateway_doc = json.loads((_CORPUS / "gateway.openapi.json").read_text(encoding="utf-8"))
    paths = gateway_doc.get("paths", {})

    assert "/openapi/v1/bots" in paths
    assert "/openapi/v1/chat/sessions/{session_id}" in paths
    assert "/openapi/v1/collaboration/messages/ws" in paths
    assert "/health" not in paths
    assert "/api/test" not in paths


def test_copied_protocol_sources_match_committed_corpus() -> None:
    sources = {
        _REPO / "src/bcsfuse/schemas/openapi.yaml": "bcsfuse.openapi.yaml",
        _REPO
        / "src/engine/src/engine/community/claude_code_gateway/docs/websocket-protocol.md": "engine-websocket-protocol.md",
    }
    for source, artifact in sources.items():
        artifact_path = _CORPUS / artifact
        _assert_utf8_lf_no_bom(artifact_path)
        assert _normalized_lf_text(source.read_bytes()) == _normalized_lf_text(
            artifact_path.read_bytes()
        )