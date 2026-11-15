# Instructions for coding agents

## Run the formatter before you push

This repository enforces code style in every project (`EnforceCodeStyleInBuild`)
and checks it in CI with `dotnet format`. Run the same check before you push:

```bash
dotnet build LinQL.slnx
dotnet format LinQL.slnx --no-restore --verify-no-changes
```

**Build first.** On a tree that has not been built, `dotnet format` loads the
workspace before the source generator has been compiled, so the generated types
look missing and every `using` that names them is reported as unnecessary
(`IDE0005`, `CS0246`). Those findings are an artifact of the order, not a reason
to edit the usings.

When the check fails, fix it with `dotnet format LinQL.slnx` and review the diff.
The rules live in `.editorconfig`, and the categories that are deliberately not
enforced carry a comment there saying why. Adding a suppression to get a green
run is not an acceptable fix: if a rule is genuinely wrong for a file, its
exclusion belongs beside the existing exceptions with a reason.
