# AI Notes

## Tools

- **Claude (chat)** for design discussion: weighing options, challenging my reasoning, and drafting the design notes.
- **Claude Code** for implementation, working from `CLAUDE.md`, a standing context file with every decision and rule.

How I used them: one vertical slice per Claude Code session, a plan reviewed before any code, and every design decision recorded in `CLAUDE.md` so later sessions could not drift.

## Where the AI was wrong or suboptimal, and how I caught it

**1. The concurrency token.** The AI recommended SQL Server `rowversion`. I preferred a managed int: it keeps the design database-agnostic, since MySQL and SQLite have no engine-maintained row version, and it is easier to read in logs or when supporting users.

**2. Save-time logic in the DbContext.** The AI first suggested keeping the version increment in a `SaveChanges` override, and updating `SearchText` with a separate `SearchTextInterceptor`. I preferred keeping `SaveChanges` free of any business logic, and interceptors are also easier to test. The `SearchTextInterceptor` was later dropped, because no endpoint writes searchable fields.

## A prompt I was happy with

`CLAUDE.md` in the repo root. Instead of relying on one clever prompt, I kept every decision and rule in this file (the transition table, the concurrency rules, banned APIs, the list endpoint contract), so each Claude Code session started from the same constraints and each slice prompt only had to name the slice and what "done" means. I drafted the prompts with Claude chat and reviewed each one before sending.
