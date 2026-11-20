#!/bin/zsh
# Compiles every C# script under <project>/Assets with the same references and defines the Unity
# Editor uses, without opening Unity. Lets work happen in git worktrees where the Editor can't run.
#
# usage: Tools/compile-check.sh [project-dir]     (defaults to the directory containing Tools/)
# Exit code 0 = compiles. Errors are printed as file(line,col): error CSxxxx: message.
#
# References come from the main checkout's Library (set ASHLINE_MAIN to override), so the Editor
# must have compiled the main project at least once.
set -e
SELF=${0:A:h}
PROJECT=${1:-${SELF:h}}
PROJECT=${PROJECT:A}
MAIN=${ASHLINE_MAIN:-/Users/skogarmaor/WorkSpace/Projects/ashline/unity}
EDITOR_APP=/Applications/Unity/Hub/Editor/6000.6.10f1/Unity.app/Contents
DOTNET=$EDITOR_APP/Resources/Scripting/NetCoreRuntime/dotnet
CSC=$EDITOR_APP/Resources/Scripting/DotNetSdk/sdk/8.0.318/Roslyn/bincore/csc.dll

SRC_RSP=$(find "$MAIN/Library/Bee" -name "Assembly-CSharp-Editor.rsp" | head -1)
[[ -f $SRC_RSP ]] || { echo "No Assembly-CSharp-Editor.rsp in $MAIN/Library/Bee; open the project in Unity once."; exit 2; }

OUT=$(mktemp -d)
RSP=$OUT/check.rsp
# Keep options, defines and engine/package references; drop sources, outputs, analyzers and the
# project's own assemblies (we are recompiling those sources).
rg -v '\.cs"?$|^-out:|^-refout:|^-analyzer:|^-additionalfile:|^-generatedfilesout:|Assembly-CSharp' "$SRC_RSP" \
  | sed -E "s#^-r:\"Library/#-r:\"$MAIN/Library/#; s#^-r:Library/#-r:$MAIN/Library/#" > "$RSP"
echo "-out:\"$OUT/Check.dll\"" >> "$RSP"
echo "-nowarn:CS0618,CS0414,CS0649,CS0169,CS0219" >> "$RSP"
find "$PROJECT/Assets" -name "*.cs" -not -path "*/Tests/*" | sed 's/.*/"&"/' >> "$RSP"

cd "$MAIN"
set +e
"$DOTNET" "$CSC" -nologo "@$RSP" 2>&1 | rg -v ': warning ' | sed "s#$PROJECT/##"
CODE=${pipestatus[1]}
rm -rf "$OUT"
[[ $CODE -eq 0 ]] && echo "compile-check: OK"
exit $CODE
