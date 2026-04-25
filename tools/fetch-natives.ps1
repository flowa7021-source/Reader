<#
.SYNOPSIS
    Скачивает нативные зависимости Foliant и проверяет SHA256.

.DESCRIPTION
    Phase 0 placeholder. PDFium и Tesseract сейчас тащатся через NuGet (PDFiumCore + Tesseract);
    этот скрипт нужен для:
      - tessdata LSTM моделей (распространяются отдельно, GitHub Releases tesseract-ocr/tessdata_fast),
      - DjVuLibre бинарей (когда подключим opc. плагин в S9),
      - LibreOffice portable (Phase 3).

    SHA256 пин-лист хранится в tools/third-party/checksums.json.
    Если файл уже скачан и SHA256 совпадает — скрипт ничего не делает.

.PARAMETER Tier
    Уровень OCR-моделей: Basic | Standard | Full. По умолчанию Basic (рус+eng).

.PARAMETER Quiet
    Не выводить прогресс-бары (для CI).

.EXAMPLE
    pwsh tools/fetch-natives.ps1
    pwsh tools/fetch-natives.ps1 -Tier Full
#>

[CmdletBinding()]
param(
    [ValidateSet('Basic', 'Standard', 'Full')]
    [string]$Tier = 'Basic',

    [switch]$Quiet
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = if ($Quiet) { 'SilentlyContinue' } else { 'Continue' }

$RepoRoot = Split-Path $PSScriptRoot -Parent
$NativeRoot = Join-Path $RepoRoot 'native'
$ChecksumsFile = Join-Path $PSScriptRoot 'third-party/checksums.json'

if (-not (Test-Path $ChecksumsFile)) {
    Write-Host "tools/third-party/checksums.json пока не создан — Phase 0 placeholder." -ForegroundColor Yellow
    Write-Host "Skip native fetch. Tier=$Tier"
    exit 0
}

$checksums = Get-Content $ChecksumsFile | ConvertFrom-Json

$languagesByTier = @{
    'Basic'    = @('rus', 'eng')
    'Standard' = @('rus', 'eng', 'ukr', 'bel', 'kaz', 'deu', 'fra', 'spa', 'ita')
    'Full'     = @('rus', 'eng', 'ukr', 'bel', 'kaz', 'deu', 'fra', 'spa', 'ita',
                   'chi_sim', 'chi_tra', 'jpn', 'kor', 'ara', 'heb')
}

$tessdataDir = Join-Path $NativeRoot 'tesseract/tessdata'
New-Item -ItemType Directory -Force -Path $tessdataDir | Out-Null

foreach ($lang in $languagesByTier[$Tier]) {
    $entry = $checksums.tessdata.$lang
    if (-not $entry) {
        Write-Warning "Нет SHA256 для tessdata/$lang в checksums.json — пропускаю."
        continue
    }
    $target = Join-Path $tessdataDir "$lang.traineddata"
    if (Test-Path $target) {
        $actual = (Get-FileHash $target -Algorithm SHA256).Hash.ToLower()
        if ($actual -eq $entry.sha256) {
            Write-Host "[ok]   $lang.traineddata"
            continue
        }
        Write-Host "[stale] $lang.traineddata — переcкачиваю"
    }
    Write-Host "[fetch] $lang.traineddata"
    Invoke-WebRequest -Uri $entry.url -OutFile $target -UseBasicParsing
    $actual = (Get-FileHash $target -Algorithm SHA256).Hash.ToLower()
    if ($actual -ne $entry.sha256) {
        Remove-Item $target -Force
        throw "SHA256 mismatch для $lang.traineddata: ожидал $($entry.sha256), получил $actual"
    }
}

Write-Host "Native fetch завершён. Tier=$Tier"
