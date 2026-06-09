# Релиз: сборка полного Windows-инсталлятора

Этот документ описывает, как тег `v*.*.*` превращается в готовые инсталляторы, и что нужно сделать
один раз, чтобы инсталлятор был **полным** (с OCR-моделями и поддержкой DjVu).

## TL;DR

1. **Один раз** запустите workflow **Prepare release assets** (`.github/workflows/prepare-assets.yml`)
   на релизной ветке — он захостит модели PaddleOCR (и, при желании, DjVuLibre) и закоммитит
   `tools/third-party/checksums.json`.
2. Поставьте тег `vX.Y.Z` и запушьте его — workflow **Release** (`release.yml`) соберёт, протестирует,
   подпишет (если есть сертификат) и опубликует инсталляторы в GitHub Release.

---

## Что выкладывается в релиз

| Ассет | Лицензия | Содержимое |
|-------|----------|------------|
| `Foliant-Setup-<ver>-Basic.exe` | MIT (чистый) | app + .NET runtime + нативка (pdfium/Paddle/OpenCV/SQLite) + наш плагин DjVu + модели latin/cyrillic |
| `Foliant-Setup-<ver>-Standard.exe` | MIT | то же + расширенный набор (см. tier'ы) |
| `Foliant-Setup-<ver>-Full.exe` | MIT | то же + все скрипты OCR (chinese/japan/korean/arabic) |
| `Foliant-DjVu-Support-<ver>.exe` | GPL-2.0 (изолирован) | **отдельный** инсталлятор: бинари DjVuLibre + COPYING + source-offer |
| `SHA256SUMS` | — | хеши всех `.exe` |

**Изоляция лицензии.** Основные инсталляторы — MIT-чистые: GPL-бинарей DjVuLibre и GPL-текста в них
нет (есть только наш MIT-плагин `Foliant.Plugin.DjVu.dll`). Сам движок DjVuLibre (GPL-2.0) едет
исключительно в `Foliant-DjVu-Support-<ver>.exe`, который ставит `ddjvu`/`djvused` в каталог уже
установленного Foliant (`{app}\native\djvulibre`, где плагин ищет их в первую очередь) и кладёт
`Licenses\LICENSE-DjVuLibre.txt` (= upstream COPYING) + `DJVULIBRE-SOURCE-OFFER.txt`. Так GPL не
«заражает» ядро (вызов out-of-process). DjVu-инсталлятор собирается **только если** DjVuLibre был
захостен (см. шаг подготовки).

---

## Шаг 1 (однократно): подготовка ассетов

Модели PaddleOCR и бинари DjVuLibre не лежат в репозитории — их хостит постоянный релиз `models-v1`,
а `tools/third-party/checksums.json` пинит их по URL+SHA256. Пока этого файла нет, `fetch-natives.ps1`
штатно ничего не качает, и релиз соберётся **без** моделей и без DjVu-инсталлятора.

Запустите **Actions → Prepare release assets → Run workflow** на нужной ветке. Параметры:

- **tier**: `Full` (захостить все скрипты — тогда инсталлятор любого tier'а укомплектован) или `Basic`.
- **djvulibre_source_url**: URL архива Windows-бинарей DjVuLibre (`.zip`/`.tar`/`.7z`). Оставьте пустым,
  если поддержку DjVu пока не выкладываете — тогда DjVu-инсталлятор просто не будет собираться.
- **model_spec_path**: необязательный путь к своему JSON-набору моделей (переопределяет дефолты
  `tools/prepare-ocr-models.ps1`).

Workflow: соберёт `.tar` → выложит в релиз `models-v1` → сгенерирует и **закоммитит**
`tools/third-party/checksums.json` (+ `ASSETS.lock.md`) в ту же ветку.

> ⚠️ URL апстрим-моделей со временем меняются. Дефолты в `prepare-ocr-models.ps1` верны на момент
> написания — **проверьте** их (или передайте `model_spec_path`). При смене версий обновите
> соответствующие строки в `NOTICE.md` (правило из `tools/third-party/README.md`).

Локально то же можно прогнать вручную:

```pwsh
pwsh tools/prepare-ocr-models.ps1 -OutDir dist/models -Tier Full
pwsh tools/prepare-djvulibre.ps1 -OutDir dist/models -SourceDir C:\path\to\djvulibre-win
# затем выложить dist/models/*.tar в релиз models-v1 и вписать URL+SHA в checksums.json
```

---

## Шаг 2: выпуск версии

```bash
git tag v0.1.0
git push origin v0.1.0
```

Либо **Actions → Release → Run workflow** с полем `tag` (например `v0.1.0`) — удобно для пробного
прогона с ветки.

`release.yml` по шагам:

1. **Resolve version** — `TAG` = `inputs.tag` или `github.ref_name`; `VERSION` = `TAG` без ведущего `v`.
   `VERSION` штампует сборку (`-p:Version`), `AppVersion` в Inno и имена инсталляторов.
2. **Fetch natives** — `fetch-natives.ps1 -Tier Full` тянет модели и DjVuLibre по `checksums.json`.
3. **Build + Test** — `dotnet build -warnaserror`, затем `dotnet test` (unit+integration; Slow/E2E
   пропускаются — часть Slow зависит от моделей, E2E требует WinAppDriver).
4. **Publish** — `dotnet publish -r win-x64 --self-contained` в `publish/` (app + runtime + нативка;
   наш DjVu-плагин кладётся в `publish/plugins/`, при его отсутствии publish **намеренно падает**).
5. **Sign** (если задан секрет, см. ниже).
6. **Release notes** — берётся `.github/release-notes/<TAG>.md`, иначе нарезается из `CHANGELOG.md`.
7. **Build installers** — ISCC ×3 tier'а основного `Foliant.iss` + (опционально) `FoliantDjVu.iss`,
   если присутствует `native/djvulibre/ddjvu.exe`.
8. **Smoke** — тихая установка Full, проверка MIT-манифеста (app + плагин + MIT/OFL-лицензии + pdfium;
   модели адаптивно) и что **GPL-текста в основном инсталляторе нет**; затем установка DjVu-инсталлятора
   поверх и проверка GPL-поставки. Падение блокирует релиз.
9. **SHA256SUMS + Create Release** — все `.exe` из `Output/` + хеши.

---

## Подпись (code signing)

Подпись включается, **только если** заданы секреты репозитория:

- `SIGNING_CERT_BASE64` — PFX-сертификат в Base64.
- `SIGNING_CERT_PASSWORD` — пароль к нему.

Без них шаги Sign пропускаются, и инсталляторы **выходят неподписанными** — Windows SmartScreen покажет
предупреждение «Unknown publisher». Это ожидаемо до получения сертификата. Чтобы включить:

```bash
base64 -w0 cert.pfx > cert.b64       # Linux/macOS
# Settings → Secrets and variables → Actions:
#   SIGNING_CERT_BASE64   = содержимое cert.b64
#   SIGNING_CERT_PASSWORD = пароль PFX
```

`tools/sign-binaries.ps1` подписывает `publish/` (Foliant.exe + DLL) и все `.exe` инсталляторов.

---

## Ручной чек-лист перед публичным `v0.1.0`

Автоматический smoke проверяет манифест артефакта, но не «живую» работу. На чистой Windows-VM:

- [ ] Установить `Foliant-Setup-<ver>-Full.exe`; приложение запускается, иконка брендирована.
- [ ] Открыть PDF — рендерится; прогнать OCR — даёт текст (нужны захостенные модели).
- [ ] DjVu **до** установки поддержки — неактивен/просит доустановить.
- [ ] Установить `Foliant-DjVu-Support-<ver>.exe`; DjVu-файл открывается.
- [ ] В `{app}\Licenses\` есть `LICENSE.txt` (MIT), `NOTICE.md`, `LICENSE-Liberation.txt`; после
      DjVu-инсталлятора добавляются `LICENSE-DjVuLibre.txt` + `DJVULIBRE-SOURCE-OFFER.txt`.
- [ ] Деинсталляция убирает приложение; удаление DjVu-поддержки убирает `native\djvulibre`.

---

## Troubleshooting

- **Релиз падает на «Prepare release notes»** — нет секции версии в `CHANGELOG.md`; добавьте её или
  положите `.github/release-notes/<TAG>.md` вручную (есть fallback на `TEMPLATE.md`).
- **Инсталлятор без моделей / без DjVu** — не пройден Шаг 1 (нет `checksums.json`) либо для DjVu не
  задан `djvulibre_source_url`. Проверьте лог `fetch-natives` и наличие `native/...`.
- **Publish падает с «DjVu plugin DLL not found»** — плагин не собрался; проверьте сборку
  `plugins/Foliant.Plugin.DjVu` (это намеренный жёсткий гейт — без плагина инсталлятор не выпускаем).
- **`prepare-djvulibre.ps1` не нашёл `ddjvu.exe`** — переданный источник не является Windows-сборкой
  DjVuLibre; распакуйте корректный архив и используйте `-SourceDir`.
