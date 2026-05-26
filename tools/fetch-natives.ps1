<#
.SYNOPSIS
    Скачивает нативные зависимости Foliant и проверяет SHA256.

.DESCRIPTION
    Phase 0 placeholder. PDFium тащится через NuGet (PDFiumCore); PaddleOCR-нативка — через
    Sdcb NuGet-пакеты. Этот скрипт скачивает оффлайн-модели PaddleOCR (распознаватель по скриптам
    + общие детектор/классификатор) в native/paddleocr/, а также:
      - DjVuLibre бинари (когда подключим опц. плагин в S9),
      - LibreOffice portable (Phase 3).

    Модели PaddleOCR раскладываются как:
      native/paddleocr/det/            — детектор текста (общий)
      native/paddleocr/cls/            — классификатор поворота (общий)
      native/paddleocr/rec/latin/      — распознаватель латиницы (+ label.txt)
      native/paddleocr/rec/cyrillic/   — распознаватель кириллицы (+ label.txt)
      native/paddleocr/rec/<script>/   — прочие скрипты для Full

    SHA256 пин-лист хранится в tools/third-party/checksums.json.
    Если файл уже скачан и SHA256 совпадает — скрипт ничего не делает.

.PARAMETER Tier
    Уровень OCR-моделей: Basic | Standard | Full. По умолчанию Basic (латиница + кириллица).

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

# Распаковка моделей идёт через bsdtar (`tar`). В Windows 10 17063+ он встроен, но в CI/старых
# образах может отсутствовать — падаем рано с понятной ошибкой, а не на середине загрузки.
if (-not (Get-Command 'tar' -ErrorAction SilentlyContinue)) {
    throw "Не найден 'tar' (bsdtar) — нужен для распаковки моделей. Windows 10 17063+ / установите вручную."
}

# Скрипты распознавания PaddleOCR по tier'ам (det/cls — общие, всегда). Латиница покрывает
# базовую Европу, кириллица — рус/СНГ; CJK/арабский — отдельные модели в Full.
$scriptsByTier = @{
    'Basic'    = @('latin', 'cyrillic')
    'Standard' = @('latin', 'cyrillic')
    'Full'     = @('latin', 'cyrillic', 'chinese', 'japan', 'korean', 'arabic')
}

$paddleRoot = Join-Path $NativeRoot 'paddleocr'

# Загрузка с экспоненциальным backoff: модели тянутся в release-пайплайне, и единичный сетевой
# сбой не должен валить весь релиз. 4 попытки: 2s, 4s, 8s.
function Invoke-DownloadWithRetry($uri, $outFile) {
    $delays = @(2, 4, 8)
    for ($attempt = 0; ; $attempt++) {
        try {
            Invoke-WebRequest -Uri $uri -OutFile $outFile -UseBasicParsing
            return
        }
        catch {
            if ($attempt -ge $delays.Count) { throw }
            Write-Warning "Загрузка $uri не удалась ($($_.Exception.Message)) — повтор через $($delays[$attempt])s"
            Start-Sleep -Seconds $delays[$attempt]
        }
    }
}

function Get-PaddleModel($name, $targetDir) {
    $entry = $checksums.paddleocr.$name
    if (-not $entry) {
        Write-Warning "Нет SHA256 для paddleocr/$name в checksums.json — пропускаю."
        return
    }
    New-Item -ItemType Directory -Force -Path $targetDir | Out-Null
    $target = Join-Path $targetDir 'model.tar'
    if (Test-Path $target) {
        $actual = (Get-FileHash $target -Algorithm SHA256).Hash.ToLower()
        if ($actual -eq $entry.sha256) {
            Write-Host "[ok]   paddleocr/$name"
            return
        }
        Write-Host "[stale] paddleocr/$name — переcкачиваю"
    }
    Write-Host "[fetch] paddleocr/$name"
    Invoke-DownloadWithRetry $entry.url $target
    $actual = (Get-FileHash $target -Algorithm SHA256).Hash.ToLower()
    if ($actual -ne $entry.sha256) {
        Remove-Item $target -Force
        throw "SHA256 mismatch для paddleocr/${name}: ожидал $($entry.sha256), получил $actual"
    }
    # Архив обязан распаковываться ПЛОСКО (infer-файлы + label.txt для rec прямо в $targetDir),
    # без вложенного *_infer/. Контракт с движком — tools/third-party/README.md.
    tar -xf $target -C $targetDir
}

# Общие модели: детектор + классификатор поворота.
Get-PaddleModel 'det' (Join-Path $paddleRoot 'det')
Get-PaddleModel 'cls' (Join-Path $paddleRoot 'cls')

# Распознаватели по скриптам выбранного tier'а.
foreach ($script in $scriptsByTier[$Tier]) {
    Get-PaddleModel "rec_$script" (Join-Path $paddleRoot "rec/$script")
}

Write-Host "Native fetch завершён. Tier=$Tier"
