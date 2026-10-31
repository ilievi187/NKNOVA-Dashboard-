# Launching ORBIT coding agents on GitHub

The four task briefs are in `MISSIONS.md`; custom Copilot-compatible profiles live in `.github/agents/`.

**Important:** these files do not start background agents by themselves.

## Dedicated repo (recommended)
1. Create a standalone private `NightKnight-ORBIT` repository in the owner's GitHub account.
2. Copy the ORBIT source tree into its repository root, leaving `.env` and `.sqlite3` databases out.
3. Ensure GitHub Issues are enabled and the Copilot coding agent / third-party agent you intend to use is available on the account.
4. Create one issue per mission M1–M4 with acceptance criteria from `MISSIONS.md`.
5. Assign each to the right enabled agent; select the correct dedicated repo and base branch.
6. Require PR review and CI; never let agents auto-merge changes involving filesystem permissions, shell execution or remote host access.

## Temporary NKNOVA holding branch
Until the dedicated repo exists, the source is isolated at `NKNOVA-Dashboard-` branch `orbit/bootstrap-v0.1-20261031` under `orbit-harness/`. Existing NKNOVA `main` has not been modified. **Issues are disabled there** and the temporary branch should not be merged into the dashboard. Agent jobs should wait for the standalone repo to avoid working against the wrong default branch.

## Local ALEX/NOVA/HERMES processes
The current GitHub profile definitions do not control real NightKnightPC processes. Start them only after configuring an authenticated adapter or an authorized local runner. Avoid blindly launching several GPU-heavy models at once.
