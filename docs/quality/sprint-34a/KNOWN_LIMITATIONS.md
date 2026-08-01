# Sprint 34A Known Limitations

- No remote crash-reporting backend exists.
- Diagnostic events remain on the local computer until the user exports or deletes them.
- Global exception handlers identify the application boundary, not the exact feature command in every case.
- The build identifier uses the executable informational version; a Git commit is included only when the release build embeds one.
- Feature-specific operation stages will become more precise only when future services intentionally provide diagnostic context.
- Fatal dispatcher exceptions cause a controlled application shutdown because continuing could leave the UI in an unknown state.
- The anonymous installation ID is not an account and cannot identify a person.
- A hardware summary contains only CPU model and physical-memory totals when the user explicitly includes it.
- Sanitization is defensive but must continue to receive regression tests for every newly discovered path format.
- Real crash-free-session rates cannot be calculated without actual beta sessions.
- Windows compatibility cannot be claimed from documentation alone.
- Benchmark and false-positive evidence remain future sprints.
