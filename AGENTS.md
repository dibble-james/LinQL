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

## Document the public API, not the implementation

`GenerateDocumentationFile` and `TreatWarningsAsErrors` make a missing doc comment on
publicly visible API a build error (CS1591) in the three shipping projects. That is the
only thing the build asks for, and the only reason a doc comment belongs in the tree: one
that restates the signature costs a reader time and adds nothing the name, the types and
the body do not already say.

When you add or change one:

- **Public and protected only.** Publicly visible types, members and enum values get the
  comment. `internal` and `private` members get none. When the reasoning is genuinely
  worth keeping, it belongs in a `//` comment beside the line it constrains, where the
  next reader of the body will meet it.
- **One sentence in `<summary>`, and only what the signature cannot say**: the unit, the
  default that surprises, what the return value is really for. If the sentence would
  still be right with the member renamed to `M`, cut it.
- **The why goes in `<remarks>`, in at most three sentences.** A GraphQL quirk, a
  Strawberry Shake behaviour we mirror, an ordering that matters, a deliberate deviation.
  A remark that narrates the next statement is not a remark.
- **`<param>` and `<returns>` have to add a fact** — a range, a unit, when the value is
  `null` — not rename the parameter. They are all-or-nothing per member (CS1573), so
  either every parameter gets one or none does.
- **Those caps keep a doc block around eight `///` lines.** Past that the prose belongs in
  a comment at the code it constrains, or in the pull request, not above the member.
