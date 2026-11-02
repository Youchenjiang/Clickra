#!/usr/bin/env python3
"""Icon generation forwarder to pure .NET IconGen tool."""
import subprocess
import sys
from pathlib import Path

repo_root = Path(__file__).resolve().parent.parent
tool_proj = repo_root / "scripts" / "tools" / "IconGen" / "IconGen.csproj"
res = subprocess.run(["dotnet", "run", "--project", str(tool_proj), *sys.argv[1:]])
sys.exit(res.returncode)
