# tests/assets

> Эталонный набор тестовых документов. Каждый файл — с явной лицензией. Без неё файл в репо не принимается.

Все большие файлы (> 100 КБ) хранятся через **Git LFS**. См. `.gitattributes` (создаётся при первом добавлении ассета).

## Минимальный «золотой» набор для Phase 1

| Категория | Имя файла | Источник | Лицензия | Назначение |
|---|---|---|---|---|
| PDF text | `pdf-text-en-10p.pdf` | TBD | TBD | Базовый рендер EN |
| PDF text RU | `pdf-text-ru-10p.pdf` | TBD | TBD | Кириллица |
| PDF scan | `pdf-scan-200p.pdf` | TBD | TBD | OCR pipeline |
| PDF mixed | `pdf-mixed-50p.pdf` | TBD | TBD | Текст + картинки |
| PDF RTL | `pdf-rtl-ar-5p.pdf` | TBD | TBD | RTL рендер |
| PDF CJK | `pdf-cjk-zh-5p.pdf` | TBD | TBD | CJK рендер |
| PDF AcroForm | `pdf-form-acroform.pdf` | TBD | TBD | Чтение форм |
| PDF Signed | `pdf-signed-x509.pdf` | TBD | TBD | Validate signature |
| DjVu book | `djvu-book-50p.djvu` | TBD | TBD | DjVu plugin |
| DjVu scan | `djvu-scan-30p.djvu` | TBD | TBD | DjVu OCR |
| Broken | `broken-truncated.pdf` | TBD | TBD | Robustness |
| Broken | `broken-bad-xref.pdf` | TBD | TBD | Robustness |
| Broken | `broken-empty.pdf` | TBD | TBD | Robustness |

## Источники без правовых рисков

- [Project Gutenberg](https://www.gutenberg.org/) — public domain.
- [Internet Archive](https://archive.org/) — фильтр «Public Domain».
- Собственные файлы, явно лицензированные CC0 (вписать в этот README).
- [Sample PDF Files](https://file-examples.com/) — обычно CC0.

## Как добавить ассет

1. Подобрать файл с подходящей лицензией.
2. Скопировать в `tests/assets/`.
3. Если > 100 КБ — `git lfs track "<маска>"` + закоммитить `.gitattributes`.
4. Дописать строку в таблицу выше.
5. Если используется как fixture в тестах — добавить ссылку на тестовый класс.

## Что хранить **нельзя**

- Любые сканы паспортов / договоров / медкарт — даже «обезличенные».
- PDF из закрытых корпоративных источников.
- Файлы с активным DRM.
