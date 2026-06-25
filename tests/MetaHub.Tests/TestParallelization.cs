// Several tests create real SQLite databases (WAL mode) in the temp folder. Under xUnit's default
// per-class parallelism these flake on Windows with transient "database is locked" / readonly /
// EnsureCreated errors caused by concurrent file activity across unrelated test classes. The whole
// suite runs in well under a second, so serialize it for deterministic, non-flaky runs. This does
// not mask a production concurrency bug: production uses a single WAL + busy_timeout database (see
// SqlitePragmaInterceptor), which the embedded tests cover directly.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
