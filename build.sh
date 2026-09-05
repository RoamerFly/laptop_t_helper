#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "${SCRIPT_DIR}"

echo "[1/5] Checking .NET 10 SDK..."
if ! command -v dotnet >/dev/null 2>&1; then
    echo "ERROR: dotnet CLI was not found in PATH. Please install .NET 10 SDK: https://dotnet.microsoft.com/download/dotnet/10.0"
    exit 1
fi

DOTNET_VERSION="$(dotnet --version || true)"
echo "Using .NET SDK ${DOTNET_VERSION}."

OS_NAME="$(uname -s)"
case "${OS_NAME}" in
    Linux*)     OUTPUT_DIR="${SCRIPT_DIR}/dist_linux"; RID="linux-x64" ;;
    Darwin*)    
        ARCH="$(uname -m)"
        OUTPUT_DIR="${SCRIPT_DIR}/dist_macos"
        if [ "${ARCH}" = "arm64" ]; then
            RID="osx-arm64"
        else
            RID="osx-x64"
        fi
        ;;
    *)          OUTPUT_DIR="${SCRIPT_DIR}/dist_unix"; RID="" ;;
esac

PROJECTS=(
    "src/LaptopThermalHelper.Core/LaptopThermalHelper.Core.csproj"
    "src/LaptopThermalHelper.Application/LaptopThermalHelper.Application.csproj"
    "src/LaptopThermalHelper.Hardware.Linux/LaptopThermalHelper.Hardware.Linux.csproj"
    "src/LaptopThermalHelper.Hardware.Mac/LaptopThermalHelper.Hardware.Mac.csproj"
    "src/LaptopThermalHelper.Cli/LaptopThermalHelper.Cli.csproj"
)

TEST_PROJECTS=(
    "tests/LaptopThermalHelper.Core.Tests/LaptopThermalHelper.Core.Tests.csproj"
    "tests/LaptopThermalHelper.Application.Tests/LaptopThermalHelper.Application.Tests.csproj"
    "tests/LaptopThermalHelper.Hardware.Linux.Tests/LaptopThermalHelper.Hardware.Linux.Tests.csproj"
    "tests/LaptopThermalHelper.Cli.Tests/LaptopThermalHelper.Cli.Tests.csproj"
)

echo "[2/5] Restoring dependencies..."
for proj in "${PROJECTS[@]}" "${TEST_PROJECTS[@]}"; do
    dotnet restore "${proj}" --framework net10.0
done

echo "[3/5] Building Release configuration..."
for proj in "${PROJECTS[@]}"; do
    dotnet build "${proj}" --configuration Release --framework net10.0 --no-restore
done

echo "[4/5] Running cross-platform unit tests..."
for test_proj in "${TEST_PROJECTS[@]}"; do
    dotnet test "${test_proj}" --configuration Release --framework net10.0 --no-restore --nologo
done

echo "[5/5] Publishing LaptopThermalHelper CLI..."
rm -rf "${OUTPUT_DIR}"
mkdir -p "${OUTPUT_DIR}"

if [ -n "${RID}" ]; then
    dotnet publish "src/LaptopThermalHelper.Cli/LaptopThermalHelper.Cli.csproj" \
        --configuration Release \
        --framework net10.0 \
        --runtime "${RID}" \
        --self-contained true \
        --output "${OUTPUT_DIR}"
else
    dotnet publish "src/LaptopThermalHelper.Cli/LaptopThermalHelper.Cli.csproj" \
        --configuration Release \
        --framework net10.0 \
        --output "${OUTPUT_DIR}"
fi

cp -f "LICENSE" "${OUTPUT_DIR}/LICENSE.txt" 2>/dev/null || true
if [ -d "LICENSES" ]; then
    cp -rf "LICENSES" "${OUTPUT_DIR}/" 2>/dev/null || true
fi

echo ""
echo "Build succeeded!"
echo "Published executable output: ${OUTPUT_DIR}/LaptopThermalHelper.Cli"
echo ""
echo "Usage:"
echo "  ${OUTPUT_DIR}/LaptopThermalHelper.Cli             # Launch interactive terminal dashboard"
echo "  ${OUTPUT_DIR}/LaptopThermalHelper.Cli --status    # Print single-line status"
echo "  ${OUTPUT_DIR}/LaptopThermalHelper.Cli --json      # Output status as JSON"
echo "  ${OUTPUT_DIR}/LaptopThermalHelper.Cli --daemon    # Run in background daemon mode"
echo "  ${OUTPUT_DIR}/LaptopThermalHelper.Cli --mock      # Run with mock simulation data"
