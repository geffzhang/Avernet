import ast
import importlib.util
import sys
from pathlib import Path


_SCRIPT = Path(__file__).parents[1] / "export_contract_inventory.py"
_SPEC = importlib.util.spec_from_file_location("export_contract_inventory", _SCRIPT)
assert _SPEC and _SPEC.loader
_MODULE = importlib.util.module_from_spec(_SPEC)
_SPEC.loader.exec_module(_MODULE)


def test_extract_route_declarations_uses_ast_and_captures_method_path(tmp_path: Path) -> None:
    source = tmp_path / "router.py"
    source.write_text(
        "from fastapi import APIRouter\n"
        "router = APIRouter()\n"
        "@router.get('/health')\n"
        "def health(): return {'ok': True}\n",
        encoding="utf-8",
    )

    routes = _MODULE.extract_route_declarations(source, tmp_path)

    assert routes == [{"method": "GET", "declared_path": "/health", "source": "router.py"}]
    ast.parse(source.read_text(encoding="utf-8"))


def test_extract_protocols_captures_protocol_class(tmp_path: Path) -> None:
    source = tmp_path / "ports.py"
    source.write_text(
        "from typing import Protocol\n"
        "class StoragePlugin(Protocol):\n"
        "    def get(self, key: str) -> bytes: ...\n",
        encoding="utf-8",
    )

    assert _MODULE.extract_protocols(source, tmp_path) == [
        {
            "name": "StoragePlugin",
            "source": "ports.py",
            "members": [{"name": "get", "signature": "def get(self, key: str) -> bytes"}],
        }
    ]


def test_inventory_declares_all_migrated_services(repo_root: Path = Path(__file__).parents[3]) -> None:
    inventory = _MODULE.build_inventory(repo_root)
    assert set(inventory["services"]) == {"backend", "engine", "baas", "gateway", "bcsfuse"}

    for service in inventory["services"].values():
        assert service["entrypoints"]

    assert inventory["services"]["backend"]["route_declarations"]
    assert inventory["services"]["baas"]["route_declarations"]

    assert inventory["services"]["backend"]["service_protocols"]
    assert inventory["services"]["backend"]["plugin_protocols"]
    assert inventory["services"]["baas"]["service_protocols"]
    assert inventory["services"]["baas"]["plugin_protocols"]

    assert len(inventory["source_tree_sha256"]) == 64

    assert all(
        not Path(route["source"]).is_absolute()
        for service in inventory["services"].values()
        for route in service["route_declarations"]
    )


def test_inventory_hash_includes_discovered_schemas(tmp_path: Path, monkeypatch) -> None:
    service_root = tmp_path / "service"
    schema = service_root / "nested" / "schemas.py"
    schema.parent.mkdir(parents=True)
    schema.write_text("VALUE = 1\n", encoding="utf-8")
    entrypoint = service_root / "app.py"
    entrypoint.write_text("app = object()\n", encoding="utf-8")
    exporter = tmp_path / "scripts" / "export_contract_inventory.py"
    exporter.parent.mkdir()
    exporter.write_text("# inventory exporter\n", encoding="utf-8")

    monkeypatch.setattr(_MODULE, "__file__", str(exporter))
    monkeypatch.setattr(
        _MODULE,
        "SERVICE_CONFIG",
        {
            "service": {
                "root": "service",
                "entrypoints": ["app.py"],
                "route_globs": [],
                "service_protocol_globs": [],
                "plugin_protocol_globs": [],
                "protocol_documents": [],
            }
        },
    )

    original_hash = _MODULE.build_inventory(tmp_path)["source_tree_sha256"]
    schema.write_text("VALUE = 2\n", encoding="utf-8")

    assert _MODULE.build_inventory(tmp_path)["source_tree_sha256"] != original_hash


def test_inventory_ignores_virtual_environment_schemas(tmp_path: Path, monkeypatch) -> None:
    service_root = tmp_path / "service"
    source_schema = service_root / "src" / "schemas.py"
    local_schema = service_root / ".venv" / "lib" / "schemas.py"
    source_schema.parent.mkdir(parents=True)
    local_schema.parent.mkdir(parents=True)
    source_schema.write_text("SOURCE = 1\n", encoding="utf-8")
    local_schema.write_text("LOCAL = 1\n", encoding="utf-8")
    entrypoint = service_root / "app.py"
    entrypoint.write_text("app = object()\n", encoding="utf-8")
    exporter = tmp_path / "export_contract_inventory.py"
    exporter.write_text("# inventory exporter\n", encoding="utf-8")

    monkeypatch.setattr(_MODULE, "__file__", str(exporter))
    monkeypatch.setattr(
        _MODULE,
        "SERVICE_CONFIG",
        {
            "service": {
                "root": "service",
                "entrypoints": ["app.py"],
                "route_globs": [],
                "service_protocol_globs": [],
                "plugin_protocol_globs": [],
                "protocol_documents": [],
            }
        },
    )

    inventory = _MODULE.build_inventory(tmp_path)
    original_hash = inventory["source_tree_sha256"]
    local_schema.write_text("LOCAL = 2\n", encoding="utf-8")

    assert inventory["services"]["service"]["schemas"] == ["service/src/schemas.py"]
    assert _MODULE.build_inventory(tmp_path)["source_tree_sha256"] == original_hash


def test_inventory_sorts_configured_paths(tmp_path: Path, monkeypatch) -> None:
    service_root = tmp_path / "service"
    service_root.mkdir()
    for relative_path in ("a.py", "z.py", "a.md", "z.md"):
        (service_root / relative_path).write_text("# source\n", encoding="utf-8")
    exporter = tmp_path / "export_contract_inventory.py"
    exporter.write_text("# inventory exporter\n", encoding="utf-8")

    monkeypatch.setattr(_MODULE, "__file__", str(exporter))
    monkeypatch.setattr(
        _MODULE,
        "SERVICE_CONFIG",
        {
            "service": {
                "root": "service",
                "entrypoints": ["z.py", "a.py"],
                "route_globs": [],
                "service_protocol_globs": [],
                "plugin_protocol_globs": [],
                "protocol_documents": ["z.md", "a.md"],
            }
        },
    )

    service = _MODULE.build_inventory(tmp_path)["services"]["service"]

    assert service["entrypoints"] == ["service/a.py", "service/z.py"]
    assert service["protocol_documents"] == ["service/a.md", "service/z.md"]


def test_main_writes_lf_only_json(tmp_path: Path, monkeypatch) -> None:
    output = tmp_path / "inventory.json"
    monkeypatch.setattr(_MODULE, "build_inventory", lambda repo_root: {"version": "test"})
    monkeypatch.setattr(sys, "argv", [str(_SCRIPT), "--repo-root", str(tmp_path), "--output", str(output)])

    assert _MODULE.main() == 0
    assert b"\r\n" not in output.read_bytes()
