#!/usr/bin/env bash
set -euo pipefail

# Compile managed code and exercise the real CoreGraphics renderer; no app bundle,
# packaging, signing or release targets are used.
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
check_dir="$(mktemp -d "${TMPDIR:-/tmp}/ft-itc-null-graph.XXXXXX")"
trap 'rm -rf "$check_dir"' EXIT
mono_root="/Library/Frameworks/Mono.framework/Versions/Current"
xamarin_root="/Library/Frameworks/Xamarin.Mac.framework/Versions/Current"
sdk_path="$(DOTNET_CLI_UI_LANGUAGE=en-US dotnet --info | awk '/Base Path:/ { sub(/^.*Base Path:[[:space:]]*/, ""); print; exit }')"

cat > "$check_dir/csc" <<EOF
#!/bin/sh
exec dotnet "${sdk_path}Roslyn/bincore/csc.dll" "\$@"
EOF
chmod +x "$check_dir/csc"
cd "$repo_root"

dotnet build AnalysisITC.Core/AnalysisITC.Core.csproj --configuration Debug \
    --no-restore --verbosity minimal -m:1 -nodeReuse:false \
    /p:UseSharedCompilation=false /p:CopyLocalLockFileAssemblies=true
"$mono_root/Commands/msbuild" AnalysisITC.MacOS/AnalysisITC.MacOS.csproj \
    '/t:ResolveReferences;CoreCompile;_CopyFilesMarkedCopyLocal' /p:Configuration=Debug \
    /p:BuildProjectReferences=false /p:EnableCodeSigning=false /p:CreatePackage=false \
    /p:CscToolPath="$check_dir" /p:CscToolExe=csc /v:minimal /nologo
if (( $# > 0 )); then
    native_tests=("$@")
else
    native_tests=(AnalysisNullGraphTests AnalysisReportReferencesTests FtxtcDuplicatePromptTests ExperimentDesignerTests)
fi
for native_test in "${native_tests[@]}"; do
"$mono_root/Commands/mcs" \
    -r:AnalysisITC.MacOS/obj/Debug/FT-ITC.exe \
    -r:AnalysisITC.Core/bin/Debug/netstandard2.0/AnalysisITC.Core.dll \
    -r:"$xamarin_root/lib/64bits/full/Xamarin.Mac.dll" \
    -r:"$mono_root/lib/mono/4.5/Facades/netstandard.dll" \
    -out:"$check_dir/$native_test.exe" \
    "AnalysisITC.MacOS.Tests/$native_test.cs"
done

export DYLD_FALLBACK_LIBRARY_PATH="$xamarin_root/SDKs/Xamarin.macOS.sdk/lib:${DYLD_FALLBACK_LIBRARY_PATH:-/usr/local/lib:/usr/lib}"
export MONO_PATH="$repo_root/AnalysisITC.Core/bin/Debug/netstandard2.0:$repo_root/AnalysisITC.MacOS/obj/Debug:$xamarin_root/lib/64bits/full:$mono_root/lib/mono/4.5:$mono_root/lib/mono/4.5/Facades:$xamarin_root/lib/mono/4.5/Facades:$repo_root/AnalysisITC.MacOS/bin/Debug:${MONO_PATH:-}"
for native_test in "${native_tests[@]}"; do
    "$mono_root/Commands/mono" "$check_dir/$native_test.exe" "$repo_root"
done
