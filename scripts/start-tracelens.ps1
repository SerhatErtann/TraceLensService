# TraceLens'i yerelde tek seferde başlatır: Docker (ClickHouse + Collector), API, dashboard, örnek servisler.
# Zaten çalışan parçalar atlanır; tekrar tekrar çalıştırmak güvenlidir. Sonunda dashboard tarayıcıda açılır.
# Kullanım: TraceLens-Baslat.cmd dosyasına çift tıklayın (ya da: powershell -ExecutionPolicy Bypass -File scripts\start-tracelens.ps1)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$frontend = Join-Path (Split-Path -Parent $root) 'tracelens-frontend'
$dockerExe = 'C:\Program Files\Docker\Docker\Docker Desktop.exe'
$host.UI.RawUI.WindowTitle = 'TraceLens - Başlatıcı'

function Step($text) { Write-Host ''; Write-Host "» $text" -ForegroundColor Cyan }
function Ok($text) { Write-Host "  ✓ $text" -ForegroundColor Green }
function Fail($text) {
  Write-Host "  ✗ $text" -ForegroundColor Red
  Write-Host ''
  Read-Host 'Kapatmak için Enter'
  exit 1
}
function Listening($port) { [bool](Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue) }
# Kontrol başarılı olana ya da süre dolana kadar bekler (süre gerçek saniyedir; kontrolün kendisi uzun sürse de aşılmaz)
function WaitFor($what, $seconds, [scriptblock]$check) {
  $sw = [Diagnostics.Stopwatch]::StartNew()
  $nextNote = 15
  while ($sw.Elapsed.TotalSeconds -lt $seconds) {
    if (& $check) { return $true }
    if ($sw.Elapsed.TotalSeconds -ge $nextNote) {
      Write-Host "  … $what bekleniyor ($([int]$sw.Elapsed.TotalSeconds) sn)" -ForegroundColor DarkGray
      $nextNote += 15
    }
    Start-Sleep -Seconds 2
  }
  return $false
}
function DockerReady {
  # docker CLI motor kapalıyken takılabildiği için zaman aşımlı çalıştırılır
  $psi = New-Object Diagnostics.ProcessStartInfo 'docker', 'version --format {{.Server.Version}}'
  $psi.UseShellExecute = $false
  $psi.CreateNoWindow = $true
  $psi.RedirectStandardOutput = $true
  $psi.RedirectStandardError = $true
  $p = [Diagnostics.Process]::Start($psi)
  if (-not $p.WaitForExit(8000)) {
    # Takılan docker komutunu alt süreçleriyle (docker-ai vb. eklentiler) birlikte kapat; yoksa her denemede biri birikir
    cmd /c "taskkill /PID $($p.Id) /T /F >nul 2>&1"
    return $false
  }
  return $p.ExitCode -eq 0
}
# Docker Desktop'un bilinen hatası: önceki çalışmadan kalan bu dosyayı silemeyince her açılışta çöker
function DockerStaleSocket { Test-Path (Join-Path $env:LOCALAPPDATA 'Docker\run\dockerInference') }
# Yeni bir PowerShell penceresinde komut çalıştırır (pencere açık kalır, logları orada görürsünüz)
function OpenWindow($title, $dir, $command, [switch]$Minimized) {
  $script = "`$host.UI.RawUI.WindowTitle = '$title'`n$command"
  $enc = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($script))
  $style = if ($Minimized) { 'Minimized' } else { 'Normal' }
  Start-Process powershell -WorkingDirectory $dir -WindowStyle $style -ArgumentList '-NoExit', '-EncodedCommand', $enc | Out-Null
}

Write-Host 'TraceLens başlatılıyor' -ForegroundColor White

# 1) Docker
Step 'Docker'
if (-not (DockerReady)) {
  if (-not (Get-Process 'Docker Desktop' -ErrorAction SilentlyContinue)) {
    if (-not (Test-Path $dockerExe)) { Fail "Docker Desktop bulunamadı ($dockerExe)." }
    Start-Process $dockerExe
    Write-Host '  Docker Desktop açılıyor…'
  }
  if (-not (WaitFor 'Docker' 180 { DockerReady })) {
    if (DockerStaleSocket) {
      Fail ("Docker açılamadı: Docker Desktop önceki çalışmasından kalan bir dosyayı silemiyor. Çözüm:`n" +
            "    1. Sağ alttaki balina simgesi > Quit Docker Desktop (kapanmazsa Görev Yöneticisi > Docker Desktop.exe > Görevi sonlandır)`n" +
            "    2. Win+R > %LOCALAPPDATA%\Docker > 'run' klasörünün adını 'run-eski' yapın`n" +
            "    3. Bu dosyayı tekrar çalıştırın`n" +
            "    Tekrarlamaması için: Docker Desktop > Settings > AI > 'Enable Docker Model Runner' kapatın.")
    }
    Fail ("Docker 3 dakikada açılmadı. Docker Desktop penceresine bakın (hata ya da 'Lingering processes' uyarısı varsa " +
          "Görev Yöneticisi'nden Docker Desktop.exe işlemlerini sonlandırıp tekrar deneyin; olmazsa bilgisayarı yeniden başlatın).")
  }
}
Ok 'Docker çalışıyor'

# 2) ClickHouse + OTel Collector
Step 'Veritabanı (ClickHouse) ve Collector'
# docker compose ilerleme mesajlarını stderr'e yazar; PowerShell bunları hata saymasın diye cmd üzerinden çalıştırılır
Push-Location $root
cmd /c "docker compose up -d >nul 2>&1"
$composeExit = $LASTEXITCODE
Pop-Location
if ($composeExit -ne 0) { Fail 'docker compose up başarısız oldu. Ayrıntı için tracelens-service klasöründe "docker compose up -d" çalıştırın.' }
if (-not (WaitFor 'ClickHouse' 90 { try { (Invoke-WebRequest 'http://localhost:8123/ping' -UseBasicParsing -TimeoutSec 3).StatusCode -eq 200 } catch { $false } })) {
  Fail 'ClickHouse 90 saniyede cevap vermedi.'
}
Ok 'ClickHouse ve Collector çalışıyor'

# 3) API (5100)
Step 'API (TraceLensService)'
if (Listening 5100) {
  Ok 'Zaten çalışıyor'
} else {
  Write-Host '  Derleniyor…'
  dotnet build (Join-Path $root 'tracelens-service.slnx') -nologo -v q | Out-Null
  # Örnek servisler açıksa dosyaları kilitli olduğu için derleme hata verebilir; son derlenmiş haliyle devam edilir
  if ($LASTEXITCODE -ne 0) { Write-Host '  ! Derleme tamamlanamadı; son derlenmiş haliyle başlatılıyor.' -ForegroundColor Yellow }
  # Claude API anahtarı kullanıcı ortam değişkenindeyse alınır; yoksa asistan kapalı kalır (sormaz, beklemez)
  $api = @'
$key = [Environment]::GetEnvironmentVariable('ANTHROPIC_API_KEY', 'User')
if ($key) { $env:ANTHROPIC_API_KEY = $key; Write-Host 'AI asistanı açık (ANTHROPIC_API_KEY).' -ForegroundColor DarkGray }
else { Write-Host 'AI asistanı kapalı: ANTHROPIC_API_KEY kullanıcı ortam değişkeni yok.' -ForegroundColor DarkGray }
dotnet run --no-build --launch-profile http
'@
  OpenWindow 'TraceLens - API (5100)' (Join-Path $root 'TraceLensService') $api -Minimized
  if (-not (WaitFor 'API' 60 { Listening 5100 })) { Fail 'API 60 saniyede açılmadı. "TraceLens - API (5100)" penceresine bakın.' }
  Ok 'Çalışıyor'
}

# 4) Dashboard (5180)
Step 'Dashboard'
if (Listening 5180) {
  Ok 'Zaten çalışıyor'
} else {
  if (-not (Test-Path $frontend)) { Fail "tracelens-frontend klasörü bulunamadı ($frontend). İki repo yan yana olmalı." }
  $cmd = if (Test-Path (Join-Path $frontend 'node_modules')) { 'npm run dev' } else { 'npm install; npm run dev' }
  OpenWindow 'TraceLens - Dashboard (5180)' $frontend $cmd -Minimized
  if (-not (WaitFor 'Dashboard' 120 { Listening 5180 })) { Fail 'Dashboard açılmadı. "TraceLens - Dashboard (5180)" penceresine bakın.' }
  Ok 'Çalışıyor'
}

# 5) Örnek servisler (test verisi üretir)
Step 'Örnek servisler (test verisi)'
$samples = @(
  @{ Name = 'Sample.OrderService';   Port = 5101 },
  @{ Name = 'Sample.PaymentService'; Port = 5102 },
  @{ Name = 'Sample.Scheduler';      Port = $null }
)
foreach ($s in $samples) {
  $title = "TraceLens - $($s.Name)"
  $running = if ($s.Port) { Listening $s.Port } else { [bool](Get-Process powershell -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowTitle -eq $title }) }
  if ($running) { Ok "$($s.Name) zaten çalışıyor"; continue }
  OpenWindow $title $root "dotnet run --no-build --project Samples\$($s.Name)" -Minimized
  Ok "$($s.Name) başlatıldı"
}

# 6) Tarayıcı
Step 'Hazır'
Start-Process 'http://localhost:5180'
Ok 'Dashboard açıldı: http://localhost:5180'
Write-Host ''
Write-Host 'Parçalar simge durumundaki "TraceLens - …" pencerelerinde çalışıyor. Kapatmak için TraceLens-Durdur.cmd.' -ForegroundColor DarkGray
Start-Sleep -Seconds 4
