#!/usr/bin/env python3
"""
count_loc.py — Recursively count lines in every `.cs` file under the folder this
script lives in and **report two numbers**:

1. **Physical lines** – every line, including blanks and comments.
2. **Effective code lines** – the same set with blank lines, `//` comments and
   `/* … */` block-comments removed.

Run from the project root:

    python count_loc.py
"""

from pathlib import Path
import sys


def tally_cs_file(path: Path) -> tuple[int, int]:
    """
    Return (physical_lines, code_lines) for one .cs file.
    • physical_lines – literal line count.
    • code_lines     – excludes blank & comment lines.
    """
    phys, code = 0, 0
    in_block = False

    with path.open(encoding="utf-8", errors="ignore") as fh:
        for raw in fh:
            phys += 1
            line = raw.strip()

            # ---- handle multi-line /* … */ comments -----------------------
            if in_block:
                if "*/" in line:
                    in_block = False
                    line = line.split("*/", 1)[1].strip()
                else:
                    continue

            if line.startswith("/*"):
                in_block = "*/" not in line
                continue

            if not line or line.startswith("//"):
                continue

            code += 1

    return phys, code


def main() -> None:
    root = Path(__file__).resolve().parent

    total_phys = total_code = 0
    for cs in root.rglob("*.cs"):
        phys, code = tally_cs_file(cs)
        total_phys += phys
        total_code += code

    print(f"{total_phys:,} total physical lines")
    print(f"{total_code:,} effective code lines "
          f"({total_code / total_phys:.1%} of file content)")


if __name__ == "__main__":
    sys.exit(main())