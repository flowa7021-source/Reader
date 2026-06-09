<#
.SYNOPSIS
    Готовит оффлайн-модели PaddleOCR к хостингу: качает официальные inference-модели, раскладывает их
    ПЛОСКО (без вложенного *_infer/) и вкладывает в rec-модели нужный словарь как label.txt, затем
    перепаковывает каждую в <name>.tar. Результат — вход для workflow prepare-assets.yml, который
    публикует .tar в релиз models-v1 и пишет tools/third-party/checksums.json.

.DESCRIPTION
    Контракт раскладки (см. tools/third-party/README.md и PaddleOcrEngine):
      det/, cls/                — inference.pdmodel + inference.pdiparams (+ сопутствующие), плоско
      rec/<script>/            — inference-файлы + label.txt (словарь скрипта), плоско
    Официальные PaddleOCR-.tar распаковываются в подкаталог *_infer/ — мы выпрямляем это и
    докладываем label.txt из ppocr/utils/dict.

    ВАЖНО: URL апстрим-моделей со временем меняются. Значения по умолчанию — рабочие на момент
    написания; ПЕРЕД хостингом сверьте их (или передайте свой набор через -ModelSpec). PaddleOCR
    Apache-2.0 → модели можно класть в основной (MIT-совместимый) инсталлятор.

.PARAMETER OutDir
    Куда сложить готовые <name>.tar. По умолчанию dist/models.

.PARAMETER Tier
    Какой набор rec-скриптов готовить: Basic (latin+cyrillic) | Full (та же + chinese/japan/korean/arabic).
    det и cls общие и готовятся всегда. По умолчанию Full — чтобы хостинг покрывал любой tier инсталлятора.

.PARAMETER ModelSpec
    Путь к JSON со своим набором источников (переопределяет дефолты). Формат:
      { "models": [ { "name": "det", "url": "...tar" },
                    { "name": "rec_latin", "url": "...tar", "dict": "https://.../latin_dict.txt" } ] }

.EXAMPLE
    pwsh tools/prepare-ocr-models.ps1 -OutDir dist/models -Tier Full
#>
[CmdletBinding()]
param(
    [string]$OutDir = 'dist/models',
    [ValidateSet('Basic', 'Full')]
    [string]$Tier = 'Full',
    [string]$ModelSpec
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

if (-not (Get-Command 'tar' -ErrorAction SilentlyContinue)) {
    throw "Не найден 'tar' (bsdtar) — нужен для (рас)паковки моделей."
}

# Словари (label.txt) лежат в репозитории PaddleOCR; пин на стабильный тег release/2.7.
$dictBase = 'https://raw.githubusercontent.com/PaddlePaddle/PaddleOCR/release/2.7/ppocr/utils'

# Набор по умолчанию. det общий (ch-детектор языконезависим), cls — стандартный угловой классификатор.
# rec — мультиязычные распознаватели (V4 для ch, V3 для остальных скриптов на момент написания).
$defaultModels = @(
    @{ name = 'det';          url = 'https://paddleocr.bj.bcebos.com/PP-OCRv4/chinese/ch_PP-OCRv4_det_infer.tar' }
    @{ name = 'cls';          url = 'https://paddleocr.bj.bcebos.com/dygraph_v2.0/ch/ch_ppocr_mobile_v2.0_cls_infer.tar' }
    @{ name = 'rec_latin';    url = 'https://paddleocr.bj.bcebos.com/PP-OCRv3/multilingual/latin_PP-OCRv3_rec_infer.tar';    dict = "$dictBase/dict/latin_dict.txt" }
    @{ name = 'rec_cyrillic'; url = 'https://paddleocr.bj.bcebos.com/PP-OCRv3/multilingual/cyrillic_PP-OCRv3_rec_infer.tar'; dict = "$dictBase/dict/cyrillic_dict.txt" }
    @{ name = 'rec_chinese';  url = 'https://paddleocr.bj.bcebos.com/PP-OCRv4/chinese/ch_PP-OCRv4_rec_infer.tar';            dict = "$dictBase/ppocr_keys_v1.txt"; tier = 'Full' }
    @{ name = 'rec_japan';    url = 'https://paddleocr.bj.bcebos.com/PP-OCRv3/multilingual/japan_PP-OCRv3_rec_infer.tar';    dict = "$dictBase/dict/japan_dict.txt";    tier = 'Full' }
    @{ name = 'rec_korean';   url = 'https://paddleocr.bj.bcebos.com/PP-OCRv3/multilingual/korean_PP-OCRv3_rec_infer.tar';   dict = "$dictBase/dict/korean_dict.txt";   tier = 'Full' }
    @{ name = 'rec_arabic';   url = 'https://paddleocr.bj.bcebos.com/PP-OCRv3/multilingual/arabic_PP-OCRv3_rec_infer.tar';   dict = "$dictBase/dict/arabic_dict.txt";   tier = 'Full' }
)

if ($ModelSpec) {
    Write-Host "Использую набор моделей из $ModelSpec"
    $models = (Get-Content $ModelSpec -Raw | ConvertFrom-Json).models
} else {
    $models = $defaultModels | ForEach-Object { [pscustomobject]$_ }
}

# Для Basic берём только общие (det/cls) и rec без пометки tier=Full.
if ($Tier -eq 'Basic') {
    $models = $models | Where-Object { $_.tier -ne 'Full' }
}

$work = Join-Path ([System.IO.Path]::GetTempPath()) ("foliant-ocr-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $work, $OutDir | Out-Null
Write-Host "Рабочий каталог: $work"
Write-Host "Выход:          $OutDir"

function Invoke-Download($uri, $outFile) {
    $delays = @(2, 4, 8)
    for ($i = 0; ; $i++) {
        try { Invoke-WebRequest -Uri $uri -OutFile $outFile -UseBasicParsing; return }
        catch {
            if ($i -ge $delays.Count) { throw }
            Write-Warning "Загрузка $uri не удалась — повтор через $($delays[$i])s"
            Start-Sleep -Seconds $delays[$i]
        }
    }
}

foreach ($m in $models) {
    Write-Host "`n=== $($m.name) ==="
    $dl = Join-Path $work "$($m.name)_src.tar"
    Invoke-Download $m.url $dl

    # Распаковываем во временную папку и ищем каталог с inference-моделью (apстрим кладёт её в *_infer/).
    $ex = Join-Path $work "$($m.name)_x"
    New-Item -ItemType Directory -Force -Path $ex | Out-Null
    tar -xf $dl -C $ex
    $modelFile = Get-ChildItem -Path $ex -Recurse -File |
        Where-Object { $_.Name -in @('inference.pdmodel', 'inference.json') } |
        Select-Object -First 1
    if (-not $modelFile) { throw "В архиве $($m.name) не найден inference.pdmodel/inference.json" }
    $inferDir = $modelFile.Directory.FullName

    # Плоская staging-папка: копируем ВСЕ файлы модели прямо в корень (без вложенности).
    $stage = Join-Path $work "$($m.name)_flat"
    New-Item -ItemType Directory -Force -Path $stage | Out-Null
    Get-ChildItem -Path $inferDir -File | ForEach-Object {
        Copy-Item $_.FullName (Join-Path $stage $_.Name) -Force
    }

    # rec-модели требуют label.txt (движок передаёт recDir/label.txt явно).
    if ($m.dict) {
        Write-Host "  label.txt <- $($m.dict)"
        Invoke-Download $m.dict (Join-Path $stage 'label.txt')
    }

    # Перепаковываем плоско: содержимое staging кладём в корень <name>.tar (fetch-natives делает -xf в каталог).
    $outTar = Join-Path $OutDir "$($m.name).tar"
    if (Test-Path $outTar) { Remove-Item $outTar -Force }
    tar -cf $outTar -C $stage .
    $sha = (Get-FileHash $outTar -Algorithm SHA256).Hash.ToLower()
    $size = '{0:N1} MB' -f ((Get-Item $outTar).Length / 1MB)
    Write-Host "  → $outTar  ($size)  sha256=$sha"
}

Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
Write-Host "`nГотово. Модели в $OutDir. Публикует их и пишет checksums.json — prepare-assets.yml."
