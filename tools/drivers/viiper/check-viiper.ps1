$ErrorActionPreference = "Stop"

$viiperExe = Join-Path $env:LOCALAPPDATA "VIIPER\viiper.exe"
$logs = Join-Path $env:APPDATA "4rVivi\Logs"

Write-Host "4ViviTools VIIPER check"
Write-Host "======================="

if (Test-Path -LiteralPath $viiperExe) {
    Write-Host "VIIPER executable: $viiperExe" -ForegroundColor Green
} else {
    Write-Warning "VIIPER was not found at $viiperExe"
}

$tcp = $null
try {
    $tcp = [System.Net.Sockets.TcpClient]::new()
    $iar = $tcp.BeginConnect("127.0.0.1", 3242, $null, $null)
    if (-not $iar.AsyncWaitHandle.WaitOne(800)) {
        throw "Timed out connecting to localhost:3242"
    }
    $tcp.EndConnect($iar)
    $stream = $tcp.GetStream()
    $payload = [System.Text.Encoding]::UTF8.GetBytes("ping`0")
    $stream.Write($payload, 0, $payload.Length)
    $stream.Flush()
    $buffer = New-Object byte[] 1024
    $read = $stream.Read($buffer, 0, $buffer.Length)
    $response = [System.Text.Encoding]::UTF8.GetString($buffer, 0, $read)
    Write-Host "VIIPER API: $response" -ForegroundColor Green
} catch {
    Write-Warning "VIIPER API is not reachable yet: $($_.Exception.Message)"
} finally {
    if ($tcp) { $tcp.Dispose() }
}

Write-Host ""
Write-Host "USB/IP related services/drivers:"
Get-Service -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -match "usbip|vhci|viiper" -or $_.DisplayName -match "USB/IP|USBIP|VIIPER" } |
    Format-Table Name, DisplayName, Status -AutoSize

Write-Host ""
Write-Host "Logs folder: $logs"
if (Test-Path -LiteralPath $logs) {
    Get-ChildItem -LiteralPath $logs -Filter "*VIIPER*.log" -ErrorAction SilentlyContinue |
        Select-Object FullName, Length, LastWriteTime |
        Format-Table -AutoSize
}
