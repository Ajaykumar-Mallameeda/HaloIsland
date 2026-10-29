## What and why

<!-- What does this change do, and why is it needed? Link the issue: "Closes #123". -->

## Type of change

- [ ] Bug fix
- [ ] New feature or activity
- [ ] Refactoring (no behaviour change)
- [ ] Documentation
- [ ] Build or CI

## Testing

<!-- List the commands you ran and their results. Say which suites you could not run. -->

- [ ] `dotnet build Halo.slnx -c Release` with 0 warnings
- [ ] Unit tests: `dotnet test --solution Halo.slnx -c Release -- --filter-not-trait "Category=Integration"`
- [ ] Desktop tests (if `Halo.App` behaviour changed), run on an interactive desktop
- [ ] New logic is covered by tests

## Screenshots (UI changes)

<!-- Before and after, cropped to the island, with no personal content. Dark and light theme if relevant. -->

## Checklist

- [ ] Reduce Motion is respected by any new animation
- [ ] New UI has UI Automation names
- [ ] No user content is logged or sent over the network
- [ ] No secrets, certificates, packages, binaries, logs or personal data are included
- [ ] Documentation is updated where behaviour, settings or commands changed
- [ ] I have the right to contribute this code under the MIT License
