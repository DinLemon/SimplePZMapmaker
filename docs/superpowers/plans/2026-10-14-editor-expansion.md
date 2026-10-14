# Implementation and validation plan

1. Parallel implementation: material/light mesh visualization; compound building modules; elevated linear structures and junction tools. Each contributor owns separate files and supplies executable regression and browser checks.
2. Root implementation: immutable section manifest store, incremental recovery and selective workspace API/UI; direct streamed native exports; MCP lighting and isolated gameplay-zone proposals.
3. Integrate registration, format migration, resource validation and UI callbacks; review all changes and repair cross-feature regressions.
4. Run core/JS/native suites, targeted real browser workflows, official MCP clients and installed native readers. Publish and verify a self-contained preview before release.
5. Update English README with verified screenshots, exact limitations and installation guidance. Commit, push, verify exact-head CI and publish a versioned release.
