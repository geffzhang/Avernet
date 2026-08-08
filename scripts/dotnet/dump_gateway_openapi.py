from __future__ import annotations

import argparse
import os
import subprocess
from pathlib import Path


_REPO = Path(__file__).parents[2]
_GATEWAY_ROOT = _REPO / "src" / "gateway"
_GATEWAY_CONFIG = _GATEWAY_ROOT / "configs" / "application.yaml"


def _dump_once(target: Path) -> None:
    target = target.resolve()
    target.parent.mkdir(parents=True, exist_ok=True)
    code = (
        "import json\n"
        "from pathlib import Path\n"
        "from gateway.community.adapters.web.app import create_app\n"
        "app = create_app()\n"
        "doc = app.openapi()\n"
        "paths = set(doc.get('paths', {}))\n"
        "required = {'/openapi/v1/bots', '/openapi/v1/chat/sessions/{session_id}', '/openapi/v1/collaboration/messages/ws'}\n"
        "if not required.issubset(paths):\n"
        "    missing = sorted(required - paths)\n"
        "    raise RuntimeError(f'gateway served OpenAPI is missing required baseline paths: {missing}')\n"
        "payload = json.dumps(doc, indent=2, sort_keys=True, ensure_ascii=False) + '\\n'\n"
        "Path(__import__('sys').argv[1]).write_text(payload, encoding='utf-8', newline='\\n')\n"
    )
    env = os.environ.copy()
    env["GATEWAY_CONFIG_PATH"] = str(_GATEWAY_CONFIG)
    env["SERVER_ENV"] = ""
    env.pop("SOFAPY_CONFIG_PATH", None)
    env.pop("AVERNET_SECRET_PRINCIPAL_SIGNING_KEY_VALUE", None)

    subprocess.run(
        ["uv", "run", "python", "-c", code, str(target)],
        cwd=_GATEWAY_ROOT,
        check=True,
        env=env,
    )


def dump_gateway_openapi(target: Path) -> None:
    target = target.resolve()
    first = target.with_suffix(target.suffix + ".first")
    second = target.with_suffix(target.suffix + ".second")
    try:
        _dump_once(first)
        _dump_once(second)

        first_bytes = first.read_bytes()
        second_bytes = second.read_bytes()
        if first_bytes != second_bytes:
            raise RuntimeError("gateway OpenAPI export is not byte-stable across two runs")

        target.write_bytes(first_bytes)
    finally:
        first.unlink(missing_ok=True)
        second.unlink(missing_ok=True)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    dump_gateway_openapi(args.output)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())