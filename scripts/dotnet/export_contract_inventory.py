from __future__ import annotations

import argparse
import ast
import hashlib
import json
from pathlib import Path
from typing import Any


HTTP_METHODS = {"GET", "POST", "PUT", "PATCH", "DELETE", "OPTIONS", "HEAD", "WEBSOCKET"}
IGNORED_SOURCE_DIRECTORIES = {".venv", "venv", "__pycache__", "site-packages", "dist-packages"}

SERVICE_CONFIG: dict[str, dict[str, Any]] = {
    "backend": {
        "root": "src/backend",
        "entrypoints": ["src/agentclaw/community/adapters/http/app.py"],
        "route_globs": ["src/agentclaw/community/adapters/http/**/*.py"],
        "service_protocol_globs": ["src/agentclaw/community/api/**/*.py"],
        "plugin_protocol_globs": ["src/agentclaw/community/plugin_api/**/*.py"],
        "protocol_documents": [],
    },
    "engine": {
        "root": "src/engine",
        "entrypoints": ["start.py", "src/engine/community/api/app.py"],
        "route_globs": ["src/engine/community/api/**/*.py"],
        "service_protocol_globs": ["src/engine/community/api/**/*.py"],
        "plugin_protocol_globs": ["src/engine/community/plugin_api/**/*.py"],
        "protocol_documents": ["src/engine/community/claude_code_gateway/docs/websocket-protocol.md"],
    },
    "baas": {
        "root": "src/baas",
        "entrypoints": ["src/secbaas/community/main.py", "src/secbaas/community/adapters/web/app.py"],
        "route_globs": ["src/secbaas/community/adapters/web/**/*.py"],
        "service_protocol_globs": ["src/secbaas/community/api/**/*.py"],
        "plugin_protocol_globs": ["src/secbaas/community/spi/**/*.py"],
        "protocol_documents": [],
    },
    "gateway": {
        "root": "src/gateway",
        "entrypoints": ["src/gateway/community/main.py", "src/gateway/community/adapters/web/app.py"],
        "route_globs": ["src/gateway/community/adapters/web/**/*.py"],
        "service_protocol_globs": ["src/gateway/community/api/**/*.py"],
        "plugin_protocol_globs": ["src/gateway/community/spi/**/*.py"],
        "protocol_documents": [],
    },
    "bcsfuse": {
        "root": "src/bcsfuse",
        "entrypoints": ["main.py", "src/interfaces/api/app.py"],
        "route_globs": ["src/interfaces/api/**/*.py", "servers/web/**/*.py"],
        "service_protocol_globs": ["src/domain/services/**/*.py"],
        "plugin_protocol_globs": ["src/application/ports/**/*.py"],
        "protocol_documents": ["schemas/openapi.yaml", "FUSE_API_LOGIC.md"],
    },
}


def _relative(path: Path, repo_root: Path) -> str:
    return path.resolve().relative_to(repo_root.resolve()).as_posix()


def _literal_str(node: ast.AST) -> str | None:
    if isinstance(node, ast.Constant) and isinstance(node.value, str):
        return node.value
    return None


def _signature(member: ast.FunctionDef | ast.AsyncFunctionDef) -> str:
    prefix = "async def" if isinstance(member, ast.AsyncFunctionDef) else "def"
    returns = f" -> {ast.unparse(member.returns)}" if member.returns else ""
    return f"{prefix} {member.name}({ast.unparse(member.args)}){returns}"


def extract_route_declarations(path: Path, repo_root: Path) -> list[dict[str, str]]:
    tree = ast.parse(path.read_text(encoding="utf-8"), filename=str(path))
    routes: list[dict[str, str]] = []

    for node in ast.walk(tree):
        if not isinstance(node, (ast.FunctionDef, ast.AsyncFunctionDef)):
            continue

        for decorator in node.decorator_list:
            if not isinstance(decorator, ast.Call) or not isinstance(decorator.func, ast.Attribute):
                continue

            method = decorator.func.attr.upper()
            if method not in HTTP_METHODS:
                continue

            declared_path: str | None = None
            if decorator.args:
                declared_path = _literal_str(decorator.args[0])
            if declared_path is None:
                for keyword in decorator.keywords:
                    if keyword.arg == "path":
                        declared_path = _literal_str(keyword.value)
                        break

            if declared_path is None:
                continue

            routes.append({
                "method": method,
                "declared_path": declared_path,
                "source": _relative(path, repo_root),
            })

    return sorted(routes, key=lambda item: (item["declared_path"], item["method"], item["source"]))


def extract_protocols(path: Path, repo_root: Path) -> list[dict[str, Any]]:
    tree = ast.parse(path.read_text(encoding="utf-8"), filename=str(path))
    protocols: list[dict[str, Any]] = []

    for node in tree.body:
        if not isinstance(node, ast.ClassDef):
            continue

        is_protocol = any(
            (isinstance(base, ast.Name) and base.id == "Protocol")
            or (isinstance(base, ast.Attribute) and base.attr == "Protocol")
            for base in node.bases
        )
        if not is_protocol:
            continue

        members: list[dict[str, str]] = []
        for member in node.body:
            if isinstance(member, (ast.FunctionDef, ast.AsyncFunctionDef)):
                members.append({"name": member.name, "signature": _signature(member)})

        protocols.append({
            "name": node.name,
            "source": _relative(path, repo_root),
            "members": members,
        })

    return protocols


def _glob(service_root: Path, patterns: list[str]) -> list[Path]:
    return sorted({path for pattern in patterns for path in service_root.glob(pattern) if path.is_file()})


def _is_repository_source(path: Path, service_root: Path) -> bool:
    return not IGNORED_SOURCE_DIRECTORIES.intersection(path.relative_to(service_root).parts)


def _source_tree_sha256(repo_root: Path, paths: set[Path]) -> str:
    digest = hashlib.sha256()
    for path in sorted(paths, key=lambda item: _relative(item, repo_root)):
        digest.update(_relative(path, repo_root).encode("utf-8"))
        digest.update(b"\0")
        digest.update(path.read_bytes())
        digest.update(b"\0")
    return digest.hexdigest()


def _sse_contract_sources(route_files: list[Path], repo_root: Path) -> list[str]:
    markers = ("EventSourceResponse", "text/event-stream")
    hits: list[str] = []
    for route_file in route_files:
        content = route_file.read_text(encoding="utf-8")
        if any(marker in content for marker in markers):
            hits.append(_relative(route_file, repo_root))
    return sorted(hits)


def build_inventory(repo_root: Path) -> dict[str, Any]:
    services: dict[str, Any] = {}
    inventory_sources: set[Path] = {Path(__file__).resolve()}

    for name, config in SERVICE_CONFIG.items():
        service_root = repo_root / config["root"]

        route_files = _glob(service_root, config["route_globs"])
        service_protocol_files = _glob(service_root, config["service_protocol_globs"])
        plugin_protocol_files = _glob(service_root, config["plugin_protocol_globs"])
        entrypoint_files = [service_root / path for path in config["entrypoints"]]
        document_files = [service_root / path for path in config["protocol_documents"]]
        schema_files = sorted(
            path
            for path in service_root.rglob("schemas.py")
            if path.is_file() and _is_repository_source(path, service_root)
        )

        all_sources = set(
            route_files
            + service_protocol_files
            + plugin_protocol_files
            + entrypoint_files
            + document_files
            + schema_files
        )
        missing = sorted(_relative(path, repo_root) for path in all_sources if not path.is_file())
        if missing:
            raise FileNotFoundError(f"{name} inventory sources are missing: {missing}")

        inventory_sources.update(all_sources)

        routes = [route for route_file in route_files for route in extract_route_declarations(route_file, repo_root)]
        service_protocols = [
            protocol
            for protocol_file in service_protocol_files
            for protocol in extract_protocols(protocol_file, repo_root)
        ]
        plugin_protocols = [
            protocol
            for protocol_file in plugin_protocol_files
            for protocol in extract_protocols(protocol_file, repo_root)
        ]

        services[name] = {
            "entrypoints": sorted(_relative(path, repo_root) for path in entrypoint_files),
            "route_declarations": routes,
            "service_protocols": sorted(service_protocols, key=lambda item: (item["name"], item["source"])),
            "plugin_protocols": sorted(plugin_protocols, key=lambda item: (item["name"], item["source"])),
            "sse_contract_sources": _sse_contract_sources(route_files, repo_root),
            "schemas": [_relative(path, repo_root) for path in schema_files],
            "protocol_documents": sorted(_relative(path, repo_root) for path in document_files),
        }

    return {
        "version": "ocb-dotnet-migration-inventory-v1",
        "source_tree_sha256": _source_tree_sha256(repo_root, inventory_sources),
        "services": services,
    }


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--repo-root", type=Path, default=Path(__file__).parents[2])
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()

    inventory = build_inventory(args.repo_root.resolve())
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_bytes((json.dumps(inventory, indent=2, ensure_ascii=False) + "\n").encode("utf-8"))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
