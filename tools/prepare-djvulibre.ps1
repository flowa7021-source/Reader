<#
.SYNOPSIS
    Готовит минимальный Windows-бандл DjVuLibre (ddjvu.exe, djvused.exe + нужные DLL + COPYING) и
    пакует его ПЛОСКО в djvulibre.tar — вход для prepare-assets.yml (публикует в релиз models-v1 и
    пишет ключ djvulibre в tools/third-party/checksums.json).

.DESCRIPTION
    DjVuLibre — GPL-2.0. Поэтому бинари едут НЕ в основной (MIT) инсталлятор, а в отдельный
    FoliantDjVu.iss. Этот скрипт лишь собирает .tar: ddjvu/djvused + их DLL + файл лицензии COPYING.
    COPYING кладётся как есть из поставки (или скачивается из апстрим-исходников) — отдельный
    инсталлятор переименует его в Licenses\LICENSE-DjVuLibre.txt (см. FoliantDjVu.iss).

    Готовой «официальной» раздачи только-CLI под Windows у DjVuLibre нет, поэтому источник задаёте вы:
      -SourceDir  — уже распакованный каталог с ddjvu.exe/djvused.exe/*.dll (напр. из DjVuLibre+DjView
                    Windows-сборки или из MSYS2/MinGW), ЛИБО
      -SourceUrl  — URL архива (.zip/.tar/.tar.gz/.7z), который скрипт скачает и распакует.
    Скрипт находит ddjvu.exe и djvused.exe, копирует их и ВСЕ .dll из их каталога(ов), добавляет
    COPYING и пакует.

.PARAMETER OutDir      Куда положить djvulibre.tar. По умолчанию dist/models.
.PARAMETER SourceDir   Каталог с уже распакованными бинарями DjVuLibre (Windows).
.PARAMETER SourceUrl   URL архива с бинарями DjVuLibre (Windows). Используется, если не задан -SourceDir.
.PARAMETER CopyingUrl  Откуда взять GPL-2.0 COPYING, если его нет в источнике. По умолчанию —
                       сопровождаемый зеркальный репозиторий DjVuLibre.

.EXAMPLE
    pwsh tools/prepare-djvulibre.ps1 -SourceDir C:\djvulibre-win -OutDir dist/models
    pwsh tools/prepare-djvulibre.ps1 -SourceUrl https://example/djvulibre-win-x64.zip
#>
[CmdletBinding()]
param(
    [string]$OutDir = 'dist/models',
    [string]$SourceDir,
    [string]$SourceUrl,
    [string]$CopyingUrl = 'https://raw.githubusercontent.com/barak/djvulibre/master/COPYING'
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

if (-not $SourceDir -and -not $SourceUrl) {
    throw "Укажите источник бинарей: -SourceDir <каталог> или -SourceUrl <архив>."
}
if (-not (Get-Command 'tar' -ErrorAction SilentlyContinue)) {
    throw "Не найден 'tar' (bsdtar) — нужен для (рас)паковки."
}

$work = Join-Path ([System.IO.Path]::GetTempPath()) ("foliant-djvu-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $work, $OutDir | Out-Null

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

# Готовим $src — каталог с распакованными бинарями.
if ($SourceDir) {
    if (-not (Test-Path $SourceDir)) { throw "SourceDir не найден: $SourceDir" }
    $src = (Resolve-Path $SourceDir).Path
} else {
    Write-Host "Скачиваю $SourceUrl"
    $arc = Join-Path $work ([System.IO.Path]::GetFileName(($SourceUrl -split '\?')[0]))
    if (-not $arc -or $arc -eq $work) { $arc = Join-Path $work 'djvulibre-src.bin' }
    Invoke-Download $SourceUrl $arc
    $src = Join-Path $work 'src'
    New-Item -ItemType Directory -Force -Path $src | Out-Null
    if ($arc -match '\.zip$') {
        Expand-Archive -Path $arc -DestinationPath $src -Force
    } elseif ($arc -match '\.(tar(\.gz|\.bz2|\.xz)?|tgz)$') {
        tar -xf $arc -C $src
    } else {
        # .7z и прочее — пробуем 7z, если доступен.
        $7z = Get-Command '7z' -ErrorAction SilentlyContinue
        if (-not $7z) { throw "Не знаю, как распаковать $arc (нет 7z). Распакуйте сами и используйте -SourceDir." }
        & $7z.Path x $arc "-o$src" -y | Out-Null
    }
}

# Находим ddjvu.exe и djvused.exe (рекурсивно), собираем их каталоги.
$tools = @('ddjvu.exe', 'djvused.exe')
$found = @{}
foreach ($t in $tools) {
    $hit = Get-ChildItem -Path $src -Recurse -File -Filter $t -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $hit) { throw "Не найден $t под $src — это точно Windows-сборка DjVuLibre?" }
    $found[$t] = $hit
}

$stage = Join-Path $work 'flat'
New-Item -ItemType Directory -Force -Path $stage | Out-Null

# Копируем exe + ВСЕ .dll из каталогов, где лежат инструменты (зависимости рядом).
$exeDirs = ($found.Values | ForEach-Object { $_.Directory.FullName } | Sort-Object -Unique)
foreach ($t in $tools) { Copy-Item $found[$t].FullName (Join-Path $stage $t) -Force }
foreach ($d in $exeDirs) {
    Get-ChildItem -Path $d -File -Filter '*.dll' | ForEach-Object {
        Copy-Item $_.FullName (Join-Path $stage $_.Name) -Force
    }
}

# COPYING (GPL-2.0): предпочитаем файл из источника; иначе скачиваем из апстрим-исходников.
$copying = Get-ChildItem -Path $src -Recurse -File -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -in @('COPYING', 'COPYING.txt', 'LICENSE') } | Select-Object -First 1
if ($copying) {
    Copy-Item $copying.FullName (Join-Path $stage 'COPYING') -Force
    Write-Host "COPYING взят из источника: $($copying.FullName)"
} else {
    Write-Host "COPYING в источнике не найден — скачиваю из $CopyingUrl"
    Invoke-Download $CopyingUrl (Join-Path $stage 'COPYING')
}

$dllCount = (Get-ChildItem $stage -Filter '*.dll').Count
Write-Host "Собрано: ddjvu.exe, djvused.exe, $dllCount DLL, COPYING"

$outTar = Join-Path $OutDir 'djvulibre.tar'
if (Test-Path $outTar) { Remove-Item $outTar -Force }
tar -cf $outTar -C $stage .
$sha = (Get-FileHash $outTar -Algorithm SHA256).Hash.ToLower()
$size = '{0:N1} MB' -f ((Get-Item $outTar).Length / 1MB)
Write-Host "→ $outTar  ($size)  sha256=$sha"

Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
Write-Host "Готово. Публикует djvulibre.tar и пишет checksums.json — prepare-assets.yml."
