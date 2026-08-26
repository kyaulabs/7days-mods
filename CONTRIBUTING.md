# Contributing

Thanks for contributing to the AfterHours 7 Days to Die mods.

## Workflow

1. Fork the repository and create a focused branch from `main`.
2. Change source under `mod-src/`; never edit the ignored live `Mods/` tree.
3. Bump the affected mod's `ModInfo.xml` version for every mod change.
4. Regenerate XML through the mod's generator rather than hand-editing generated output.
5. Run the affected mod's verifier/build on a compatible development server.
6. Confirm `python3 mod-src/build.py verify` passes after deployment.
7. Use a [Conventional Commit](https://www.conventionalcommits.org/) message.
8. Open a pull request describing client/server impact and required upgrade steps.

## Repository safety

Do not commit:

- 7 Days to Die game/server binaries or data
- the live `Mods/` tree or generated client pack
- server configs, logs, saves, worlds, player data or map tiles
- passwords, tokens, SSH details or production-only deployment information
- compiler outputs outside the explicitly retained upstream artifacts

Run Gitleaks before committing:

```bash
gitleaks detect --source . --redact
```

The repository's optional hooks are stored in `.github/hooks/`.

## Builds and tests

The build projects reference proprietary game assemblies from a local dedicated
server installation, so GitHub cannot perform a complete clean-room build. Test
against the compatible game version documented by the affected mod and include
manual verification notes in the pull request.

## Licensing and attribution

Contributions to original project code are accepted under the repository's
[AGPL-3.0 license](LICENSE). Do not remove upstream notices or add third-party
material unless its redistribution terms are understood and documented in
[THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
