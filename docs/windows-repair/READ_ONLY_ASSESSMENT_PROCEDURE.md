# Read-only Windows Repair Assessment Procedure

1. Launch PC-SPA normally. If a protected assessment command requires administrator permission, approve the Windows User Account Control (UAC) prompt for that operation only.
2. Open **Windows Repair**.
3. Select Component Store CheckHealth, Protected Windows Files VerifyOnly, or
   both.
4. Select **Run read-only assessment**.
5. Review the confirmation dialog and continue only when no Windows servicing
   or update operation should be interrupted.
6. Allow the Microsoft command currently running to finish.
7. Review each result, exit code, duration, summary, and limitations.
8. Export the latest report only when support evidence is needed.
9. Inspect the ZIP before sharing it.

This procedure covers the read-only assessment path. Guided repair is a separate, explicitly confirmed workflow and uses operation-scoped UAC only when a protected repair operation starts.
