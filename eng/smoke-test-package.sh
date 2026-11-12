#!/usr/bin/env bash
# Packs LinQL and LinQL.GraphQL.Client into a throwaway feed, then builds a
# consumer against those packages. This exercises the source generator the way a
# user loads it: from analyzers/dotnet/cs inside the package, including its
# HotChocolate parser dependencies, rather than from a project reference.
#
# Fails when the generator cannot be loaded (CS8785) or produces no source, which
# is what a missing DLL in the analyzer payload looks like.
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

version="0.0.0-smoke"
feed="$work/feed"
consumer="$work/consumer"
mkdir -p "$feed" "$consumer"

dotnet pack "$root/LinQL/LinQL.csproj" -c Release -o "$feed" -p:Version="$version" -v:quiet
dotnet pack "$root/LinQL.GraphQL.Client/LinQL.GraphQL.Client.csproj" -c Release -o "$feed" -p:Version="$version" -v:quiet

cat > "$consumer/nuget.config" <<'EOF'
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local" value="../feed" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
EOF

cat > "$consumer/consumer.csproj" <<EOF
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <OutputType>Exe</OutputType>
    <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
  </PropertyGroup>

  <ItemGroup>
    <AdditionalFiles Include="Smoke.graphql" LinQLClientNamespace="Smoke.Client" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="LinQL" Version="$version" />
    <PackageReference Include="LinQL.GraphQL.Client" Version="$version" />
  </ItemGroup>

</Project>
EOF

cat > "$consumer/Smoke.graphql" <<'EOF'
schema {
  query: Query
}

type Query {
  number: Int
}
EOF

# Compiling this file is only possible if the generator ran, so a missing or
# unloadable analyzer becomes a build failure rather than a silent no-op.
cat > "$consumer/Program.cs" <<'EOF'
using System;
using Smoke.Client;

Console.WriteLine(typeof(Query).FullName);
EOF

dotnet restore "$consumer/consumer.csproj" --packages "$work/packages" -v:quiet

log="$work/build.log"
if ! dotnet build "$consumer/consumer.csproj" --no-restore -c Release > "$log" 2>&1; then
  cat "$log"
  exit 1
fi

if grep -q "CS8785" "$log"; then
  echo "The packaged generator failed to load:"
  grep "CS8785" "$log"
  exit 1
fi

generated="$(find "$consumer/obj" -name 'Smoke.g.cs' -print -quit)"
if [ -z "$generated" ]; then
  echo "The packaged generator produced no source for Smoke.graphql."
  cat "$log"
  exit 1
fi

echo "Package smoke test passed: ${generated#"$work"/}"
