# Script Unit Tests

This directory contains automated unit tests for developer tooling, PR governance policies, git guards, preflight checks, and Microsoft Store publication scripts.

## Running Tests

Run the full script test suite:

```powershell
python -m unittest discover -s scripts/tests -v
```

Run specific test suites:

- **PR Governance & Git Guards**:
  ```powershell
  python -m unittest discover -s scripts/tests -p 'test_pr_*.py' -v
  python -m unittest discover -s scripts/tests -p 'test_gh_guard.py' -v
  python -m unittest discover -s scripts/tests -p 'test_branch_policy.py' -v
  ```

- **Microsoft Store Publishing**:
  ```powershell
  python -m unittest discover -s scripts/tests -p 'test_*store*.py' -v
  ```

- **Public PDF Fixture Scope**:
  ```powershell
  python -m unittest discover -s scripts/tests -p 'test_*pdf_fixture*.py' -v
  ```
