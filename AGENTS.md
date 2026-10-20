# AGENTS.md

## 1. Branching & Version Control

- **Master First:** Work exclusively on the `master` branch; use other branches only when explicitly requested.
- **Human-Like Commits:** On git sync, create short commits using non-technical American slang ("feels human") and push automatically.

## 2. Runtime, Testing & Quality

- **Startup Verification:** Always restart the application and verify successful startup after any code change.
- **Targeted Testing:** Avoid running the full test suite. Run only tests relevant to the specific change, or skip testing entirely for minor edits.
- **Zero Warnings:** Treat all compiler warnings as errors and resolve them immediately.

## 3. Architecture & Configuration

- **Documentation Check:** Check the root `docs/` folder and `README.md` first for project architecture context, and sync both immediately whenever dependencies, configuration keys, or major endpoints change.
- **Maximum Automation:** Eliminate manual CLI typing or web GUI actions; automate tasks whenever technically feasible.
- **No Local Secrets:** Do not use `dotnet user-secrets` to store data locally. Put it in `appsettings` or Azure Key Vault (if one exists).

## 4. UI Changes & Reporting

- **Visual Documentation:** For UI modifications, capture and annotate old vs. new states, save the output as `SCREENSHOTS/YYMMDD-feature-name.html`, and provide the valid full path.
- **TL;DR Rule:** Append a concise 20-word TL;DR summary at the end of any response exceeding 100 words.
- **3-Tier Diff Summary:** For every code change exceeding 20 lines, append a strictly single-line-per-tier TL;DR:
  1. Toddler: One sentence simple conceptual intuition.
  2. Child: One sentence functional behavior summary.
  3. Adult: One sentence technical/architectural summary.
- **Removal Notice:** If a single prompt removes more than 100 lines of code overall, mention it.
