$ErrorActionPreference = "Stop"

if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
    throw "git is required to clone VIIPER."
}

if (-not (Get-Command go -ErrorAction SilentlyContinue)) {
    throw "Go is not installed. Use install-viiper.ps1 for the official release, or install Go before building from source."
}

$make = Get-Command make -ErrorAction SilentlyContinue
if (-not $make) {
    Write-Warning "make was not found. Upstream VIIPER builds with make; install ezwinports.make or use install-viiper.ps1."
}

$src = Join-Path $PSScriptRoot "src"
if (-not (Test-Path -LiteralPath (Join-Path $src ".git"))) {
    git clone https://github.com/Alia5/VIIPER.git $src
} else {
    Push-Location $src
    git pull
    Pop-Location
}

Push-Location $src
try {
    if ($make) {
        make build
    } else {
        go build -o (Join-Path $PSScriptRoot "viiper.exe") ./cmd/viiper
    }
} finally {
    Pop-Location
}

Write-Host "Build output is under: $src\dist"
