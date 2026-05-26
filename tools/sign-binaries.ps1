<#
.SYNOPSIS
    Подписывает Authenticode-сертификатом бинарники/инсталляторы Foliant (signtool).

.DESCRIPTION
    Используется в .github/workflows/release.yml (Windows-раннер). Сертификат передаётся через
    переменные окружения SIGNING_CERT_BASE64 (base64 от .pfx) и SIGNING_CERT_PASSWORD. Если
    SIGNING_CERT_BASE64 пуст — скрипт ничего не подписывает и выходит с кодом 0 (релиз без подписи
    для dev/forks). .pfx материализуется во временный файл и удаляется в finally.

    ВНИМАНИЕ: signtool.exe — Windows-only (Windows SDK). На Linux скрипт не запускается; валидация —
    на Windows-стенде или в release.yml. EV-подпись (HSM/токен) может потребовать иной режим
    (/csp + /kc) — для файлового .pfx достаточно /f + /p.

.PARAMETER Path
    Каталог, в котором искать файлы (рекурсивно) по -Patterns.

.PARAMETER Patterns
    Маски имён для подписи (напр. "Foliant.exe","Foliant.*.dll" или "*.exe").

.PARAMETER TimestampUrl
    RFC3161 timestamp-сервер. По умолчанию DigiCert.

.EXAMPLE
    ./tools/sign-binaries.ps1 -Path publish/ -Patterns "Foliant.exe","Foliant.*.dll"
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Path,

    [Parameter(Mandatory = $true)]
    [string[]]$Patterns,

    [string]$TimestampUrl = 'http://timestamp.digicert.com'
)

$ErrorActionPreference = 'Stop'

$certBase64 = $env:SIGNING_CERT_BASE64
$certPassword = $env:SIGNING_CERT_PASSWORD

if ([string]::IsNullOrWhiteSpace($certBase64)) {
    Write-Host 'SIGNING_CERT_BASE64 не задан — пропускаю подпись (unsigned build).' -ForegroundColor Yellow
    exit 0
}

if (-not (Test-Path $Path)) {
    throw "Path не найден: $Path"
}

function Resolve-SignTool {
    $cmd = Get-Command 'signtool.exe' -ErrorAction SilentlyContinue
    if ($cmd) {
        return $cmd.Source
    }
    # Перебираем Windows Kits 10 bin/<ver>/x64 — берём самую свежую версию.
    $kitsRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
    if (Test-Path $kitsRoot) {
        $candidate = Get-ChildItem -Path $kitsRoot -Recurse -Filter 'signtool.exe' -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -match '\\x64\\signtool\.exe$' } |
            Sort-Object FullName -Descending |
            Select-Object -First 1
        if ($candidate) {
            return $candidate.FullName
        }
    }
    throw 'signtool.exe не найден (установите Windows SDK или добавьте в PATH).'
}

$signtool = Resolve-SignTool

$pfxPath = Join-Path ([System.IO.Path]::GetTempPath()) ("foliant-sign-" + [Guid]::NewGuid().ToString('N') + '.pfx')
[System.IO.File]::WriteAllBytes($pfxPath, [Convert]::FromBase64String($certBase64))

try {
    $files = @()
    foreach ($pattern in $Patterns) {
        $files += Get-ChildItem -Path $Path -Recurse -File -Filter $pattern -ErrorAction SilentlyContinue
    }
    $files = $files | Sort-Object FullName -Unique

    if ($files.Count -eq 0) {
        Write-Warning "Под -Patterns ничего не найдено в $Path — нечего подписывать."
        return
    }

    foreach ($file in $files) {
        Write-Host "[sign] $($file.FullName)"
        if ([string]::IsNullOrEmpty($certPassword)) {
            $signArgs = @('sign', '/fd', 'SHA256', '/f', $pfxPath, '/tr', $TimestampUrl, '/td', 'SHA256', $file.FullName)
        }
        else {
            $signArgs = @('sign', '/fd', 'SHA256', '/f', $pfxPath, '/p', $certPassword, '/tr', $TimestampUrl, '/td', 'SHA256', $file.FullName)
        }
        & $signtool @signArgs
        if ($LASTEXITCODE -ne 0) {
            throw "signtool завершился с кодом $LASTEXITCODE для $($file.FullName)"
        }
    }

    Write-Host "Подписано файлов: $($files.Count)"
}
finally {
    Remove-Item $pfxPath -Force -ErrorAction SilentlyContinue
}
