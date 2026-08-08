from __future__ import annotations

import argparse
import subprocess
from pathlib import Path


_REPO = Path(__file__).parents[2]
_ENGINE_ROOT = _REPO / "src" / "engine"


def _dump_once(target: Path) -> None:
    target = target.resolve()
    target.parent.mkdir(parents=True, exist_ok=True)
    code = (
        "import json\n"
        "from pathlib import Path\n"
        "from engine.community.api.app import app\n"
        "out = Path(__import__('sys').argv[1])\n"
        "payload = json.dumps(app.openapi(), indent=2, sort_keys=True, ensure_ascii=False) + '\\n'\n"
        "out.write_text(payload, encoding='utf-8', newline='\\n')\n"
    )
    subprocess.run(
        ["uv", "run", "python", "-c", code, str(target)],
        cwd=_ENGINE_ROOT,
        check=True,
    )


def dump_engine_openapi(target: Path) -> None:
    target = target.resolve()
    first = target.with_suffix(target.suffix + ".first")
    second = target.with_suffix(target.suffix + ".second")
    try:
        _dump_once(first)
        _dump_once(second)

        first_bytes = first.read_bytes()
        second_bytes = second.read_bytes()
        if first_bytes != second_bytes:
            raise RuntimeError("engine OpenAPI export is not byte-stable across two runs")

        target.write_bytes(first_bytes)
    finally:
        first.unlink(missing_ok=True)
        second.unlink(missing_ok=True)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    dump_engine_openapi(args.output)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())