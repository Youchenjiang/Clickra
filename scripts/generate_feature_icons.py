#!/usr/bin/env python3
"""Icon generation forwarder to pure .NET IconGen tool."""
import shutil
import subprocess
import sys
from pathlib import Path

repo_root = Path(__file__).resolve().parent.parent
tool_proj = repo_root / "scripts" / "tools" / "IconGen" / "IconGen.csproj"
dotnet_bin = shutil.which("dotnet")
if not dotnet_bin:
    raise SystemExit("dotnet executable not found on PATH")

res = subprocess.run(
    [dotnet_bin, "run", "--project", str(tool_proj), *sys.argv[1:]],
    check=False,
)  # skipcq: BAN-B607, BAN-B603
sys.exit(res.returncode)
