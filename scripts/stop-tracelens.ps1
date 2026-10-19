# start-tracelens.ps1'in açtığı her şeyi kapatır. Veriler silinmez (ClickHouse verisi Docker volume'unda kalır).
# Kullanım: TraceLens-Durdur.cmd dosyasına çift tıklayın.

$root = Split-Path -Parent $PSScriptRoot
$host.UI.RawUI.WindowTitle = 'TraceLens - Durdurucu'
Write-Host 'TraceLens durduruluyor' -ForegroundColor White

# API, dashboard ve örnek servisler: dinledikleri port üzerinden süreçleri, sonra "TraceLens - …" pencerelerini kapat
foreach ($port in 5100, 5180, 5101, 5102) {
  Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue |
    ForEach-Object { Stop-Process -Id $_.OwningProcess -Force -ErrorAction SilentlyContinue }
}
Get-Process powershell -ErrorAction SilentlyContinue |
  Where-Object { $_.MainWindowTitle -like 'TraceLens - *' -and $_.Id -ne $PID } |
  ForEach-Object { Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue }
# Scheduler örneği port dinlemez; pencere kapanınca kalan dotnet süreci de sonlansın
Get-CimInstance Win32_Process -Filter "Name = 'dotnet.exe'" -ErrorAction SilentlyContinue |
  Where-Object { $_.CommandLine -like '*Sample.Scheduler*' } |
  ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
Write-Host '  ✓ API, dashboard ve örnek servisler kapandı' -ForegroundColor Green

# ClickHouse + Collector (container'lar durur, silinmez)
Push-Location $root
docker compose stop 2>&1 | Out-Null
Pop-Location
Write-Host '  ✓ Veritabanı ve Collector durdu (veriler duruyor)' -ForegroundColor Green
Write-Host ''
Write-Host 'Docker Desktop açık bırakıldı; isterseniz sağ alttaki balina simgesinden kapatabilirsiniz.' -ForegroundColor DarkGray
Start-Sleep -Seconds 4
