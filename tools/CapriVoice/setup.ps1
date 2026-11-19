# User-initiated installation of Project Capri's local speech engine.
# No external audio API, microphone access, or changes to settings.json.
# Requires uv on PATH (https://docs.astral.sh/uv/).
$ErrorActionPreference = 'Stop'
$root = Join-Path $env:LOCALAPPDATA 'Capri\Voice'
$models = Join-Path $root 'models'
$venv = Join-Path $root 'venv'
New-Item -ItemType Directory -Path $models -Force | Out-Null
$uv = (Get-Command 'uv.exe' -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty Source)
if (!$uv) {
    $candidate = Join-Path $env:APPDATA 'Python\Python314\Scripts\uv.exe'
    if (Test-Path $candidate) { $uv = $candidate }
}
if (!$uv) { throw 'uv is required. Install from https://docs.astral.sh/uv/ and retry.' }
& $uv python install 3.12
if ($LASTEXITCODE -ne 0) { throw 'Could not install isolated Python 3.12.' }
if (!(Test-Path (Join-Path $venv 'Scripts\python.exe'))) {
    & $uv venv --python 3.12 $venv
    if ($LASTEXITCODE -ne 0) { throw 'Could not initialize private voice environment.' }
}
$python = Join-Path $venv 'Scripts\python.exe'
& $uv pip install --python $python kokoro-onnx==0.6.1 soundfile==0.13.1
if ($LASTEXITCODE -ne 0) { throw 'Voice dependency installation failed.' }

$base = 'https://github.com/thewh1teagle/kokoro-onnx/releases/download/model-files-v1.1/'
$files = @(
    @{ Name='kokoro-v1.0.int8.onnx'; Hash='ae315a79b623f244700e4afb9246c46a26066782e049ba174bf3ba433970ee9c' },
    @{ Name='voices-v1.0.bin'; Hash='bca610b8308e8d99f32e6fe4197e7ec01679264efed0cac9140fe9c29f1fbf7d' }
)
foreach ($asset in $files) {
    $destination = Join-Path $models $asset.Name
    if ((Test-Path $destination) -and
        (Get-FileHash $destination -Algorithm SHA256).Hash -ieq $asset.Hash) {
        Write-Host "Already verified: $($asset.Name)"
        continue
    }
    $temp = "$destination.partial"
    try {
        Invoke-WebRequest -Uri ($base + $asset.Name) -OutFile $temp -MaximumRedirection 5
        if ((Get-FileHash $temp -Algorithm SHA256).Hash -ine $asset.Hash) {
            throw "Checksum mismatch for $($asset.Name)."
        }
        Move-Item -Force $temp $destination
    }
    finally {
        if (Test-Path $temp) { Remove-Item -Force $temp }
    }
    Write-Host "Verified: $($asset.Name)"
}
Copy-Item (Join-Path $PSScriptRoot 'server.py') (Join-Path $root 'server.py') -Force
& $python -m py_compile (Join-Path $root 'server.py')
if ($LASTEXITCODE -ne 0) { throw 'Voice server source is invalid.' }
Write-Host "Kokoro local voice installed in $root"
Write-Host 'Enable local neural voice in Capri Settings. Capri will start the installed local engine on the first spoken reply.'
