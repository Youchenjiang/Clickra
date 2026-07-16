---
description: "Resolve all unresolved PR review threads on GitHub via GraphQL mutation. Handles thread ID format, PowerShell quote escaping, and orphaned comment re-posting."
---

# PR Review Thread Resolution

Resolve all unresolved review threads on a Clickra PR using GitHub GraphQL API.

## Arguments

`$ARGUMENTS` — PR number (e.g. `21`). If empty, use the most recent open PR.

## Procedure

All commands run from the Clickra project root: `C:\Users\g1014308\Documents\GitHub\Youchen\Clickra`

### Step 1: Fetch unresolved threads

**CRITICAL**: Use GraphQL, NOT REST. REST `PATCH /pulls/comments/{id}` with `resolved: true` is READ-ONLY.

Write the query to a `.graphql` file (PowerShell loses backtick-escaped quotes in `-f` strings):

```powershell
@'
query {
  repository(owner: "Youchenjiang", name: "Clickra") {
    pullRequest(number: PR_NUMBER) {
      reviewThreads(first: 50) {
        nodes {
          id
          isResolved
          comments(first: 1) {
            nodes {
              id
              body
              author { login }
            }
          }
        }
      }
    }
  }
}
'@ | Set-Content -Path "query_threads.graphql" -Encoding UTF8
```

```powershell
gh api graphql -F query=@query_threads.graphql --jq '.data.repository.pullRequest.reviewThreads.nodes[] | select(.isResolved == false) | {id: .id, author: .comments.nodes[0].author.login, body: (.comments.nodes[0].body | split("\n")[0])}'
```

### Step 2: Resolve each thread

**Thread ID format**: `PRRT_...` (NOT `PRRC_...` which is the comment node_id).

For each unresolved thread, write the mutation to a `.graphql` file:

```powershell
@'
mutation {
  resolveReviewThread(input: {threadId: "THREAD_ID_HERE"}) {
    thread {
      id
      isResolved
    }
  }
}
'@ | Set-Content -Path "resolve_thread.graphql" -Encoding UTF8
```

```powershell
gh api graphql -F query=@resolve_thread.graphql
```

### Step 3: Re-post replies if needed

If old review comment replies were orphaned (e.g. after branch rename), re-post them as PR conversation comments:

```powershell
gh api repos/Youchenjiang/Clickra/pulls/PR_NUMBER/comments -f body='REPLY_TEXT'
```

### Step 4: Verify resolution

Re-run the fetch query from Step 1 and confirm all threads show `isResolved: true`.

## Known Pitfalls

- **REST API cannot resolve threads**: `PATCH /pulls/comments/{id}` with `resolved: true` returns success but does nothing. Always use GraphQL.
- **Thread ID ≠ Comment ID**: Thread ID is `PRRT_...`. Comment node_id is `PRRC_...`. Using the wrong one fails silently or errors.
- **PowerShell quote escaping**: `-f query='...'` loses backtick-escaped quotes. Always write queries to `.graphql` files and use `-F query=@file.graphql`.
- **Branch rename orphans replies**: Renaming a branch and creating a new PR orphans old review comments and their replies. Re-post replies as PR conversation comments if they need to persist.
- **SonarCloud C# common code smells to watch for**: static methods, useless init, async console, cognitive complexity, LINQ simplification, empty catch, missing `using System.Linq`.

## Reference

- Knowledge from session `ses_119f3c629ffeIiSC3Msl0dQMsO` (Clickra PR #21 review, 38 threads resolved)
- GraphQL API docs: `gh api graphql --help`
