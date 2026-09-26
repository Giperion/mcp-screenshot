# План: переписать screenshot MCP на C#

Рабочая ветка: `claude/upbeat-ride-rbs8j5` (создана от `main`).

## 1. Что есть сейчас

`server.py` (~830 строк, Python + ctypes, только Windows):

| Инструмент | Что делает | Как |
|---|---|---|
| `screenshot_screen` | основной монитор / `monitor=N` / `all_monitors` / регион `x,y,width,height` | `BitBlt` с DC экрана |
| `screenshot_window` | окно по `hwnd` / `title` / `process`, `client_only`, `bring_to_front` | `BitBlt` с экрана по прямоугольнику окна |
| `find_window` | список видимых окон + мониторы + виртуальный экран | `EnumWindows`, `EnumDisplayMonitors` |
| `focus_window` | вывести окно на передний план | `AttachThreadInput` + трюк с Alt |

Ещё: CLI `--list` и `--screenshot out.png`, собственный PNG-энкодер (zlib), даунскейл через `StretchBlt`, свой JSON-RPC поверх stdio (NDJSON + `Content-Length`).

### Недостатки, которые исправляем при переносе

1. **Окно снимается как кусок экрана.** Если окно перекрыто, на снимке будет то, что лежит сверху. Для «снимка отдельного окна» это главное ограничение.
2. **`GetWindowRect` захватывает невидимые рамки** (~7 px на Win10/11): на снимок окна попадает фон по краям. Нужно брать `DWMWA_EXTENDED_FRAME_BOUNDS`.
3. **Нумерация мониторов** идёт в порядке `EnumDisplayMonitors`. Этот порядок не гарантирован и не совпадает с «1, 2, 3» в настройках Windows. Имён мониторов тоже нет.
4. **`max_width`** по описанию ограничивает «длинную сторону», а по коду только ширину. Высокие окна и портретные мониторы не уменьшаются.
5. **Даунскейл `COLORONCOLOR`** даёт рваную картинку, мелкий текст не читается.
6. **Нет `CAPTUREBLT`**, поэтому layered-окна (тултипы, оверлеи) могут не попасть на снимок.
7. **Регион не проверяется**: при `x` без `width` можно выйти за пределы экрана.
8. **`protocolVersion`** возвращается тот, что прислал клиент, даже если сервер его не поддерживает.
9. **Кэш имён процессов по PID** не сбрасывается: после переиспользования PID покажется старое имя.

## 2. Цели

- Полный перенос на C# с сохранением имён и параметров текущих инструментов (существующие `mcp.json` и промпты продолжают работать).
- Три режима съёмки:
  - **Отдельное окно.** Работает и когда окно перекрыто, и для GPU/DirectX-окон (игры, UnrealEditor, браузеры).
  - **Экран: конкретный монитор.** Выбор по номеру или по имени устройства, стабильная нумерация.
  - **Все мониторы разом.** Либо одной склеенной картинкой, либо отдельной картинкой на каждый монитор в одном ответе.
- Один `.exe` без зависимостей (Python больше не нужен).
- Сборка и тесты в CI, готовые бинарники в GitHub Releases.

## 3. Технологии

| Что | Выбор | Почему / альтернатива |
|---|---|---|
| Runtime | **.NET 10 (LTS)**, TFM `net10.0-windows10.0.19041.0` | Поддержка LTS до конца 2028 г. TFM с версией Windows SDK нужен для Windows.Graphics.Capture. |
| MCP | Официальный SDK **`ModelContextProtocol`** (NuGet), stdio-транспорт | Сам согласует версию протокола, обрабатывает отмену и ping, даёт атрибуты инструментов и аннотации. Запасной вариант: свой JSON-RPC, как в Python, если SDK помешает NativeAOT. |
| Win32 | **`Microsoft.Windows.CsWin32`** (source generator, `NativeMethods.txt`) | Готовые безопасные сигнатуры и SafeHandle, совместим с AOT. Альтернатива: ручные `[LibraryImport]`. |
| Захват окон | **PrintWindow** (`PW_RENDERFULLCONTENT`) + **Windows.Graphics.Capture** (WGC) + GDI `BitBlt` как fallback | См. раздел 5. |
| PNG | Свой энкодер на `System.IO.Compression.ZLibStream`, как в Python | Без зависимостей и без System.Drawing (плохо дружит с AOT). Потом можно добавить WIC для JPEG. |
| Масштабирование | Своё box/bilinear-уменьшение по BGRA-буферу (или `StretchBlt` в режиме `HALFTONE`) | Читаемый текст после даунскейла. |
| DPI | `app.manifest`: `PerMonitorV2` | Физические пиксели на всех мониторах, как `SetProcessDpiAwareness(2)` в Python, только надёжнее. |
| Тесты | xUnit | |
| Публикация | NativeAOT, `win-x64` + `win-arm64` | Один маленький exe, быстрый старт. Запасной вариант: self-contained single-file. |

## 4. Структура репозитория

```
mcp-screenshot/
├─ ScreenshotMcp.slnx
├─ Directory.Build.props        общие настройки: nullable, warnings-as-errors, LangVersion
├─ global.json                  фиксирует версию SDK
├─ src/ScreenshotMcp/
│  ├─ ScreenshotMcp.csproj
│  ├─ app.manifest              PerMonitorV2 DPI
│  ├─ NativeMethods.txt         список Win32 API для CsWin32
│  ├─ Program.cs                хост MCP (stdio) + CLI-режимы
│  ├─ Tools/                    MCP-инструменты (тонкий слой: валидация аргументов → сервисы → результат)
│  │   ├─ ScreenTools.cs        screenshot_screen, list_monitors
│  │   └─ WindowTools.cs        screenshot_window, find_window, focus_window
│  ├─ Display/                  перечисление мониторов, виртуальный экран, DPI, имена
│  ├─ Windows/                  перечисление окон, WindowInfo, поиск по hwnd/title/process, фокус
│  ├─ Capture/
│  │   ├─ ICaptureBackend.cs
│  │   ├─ GdiScreenCapture.cs   BitBlt с экрана (мониторы, регионы, fallback для окон)
│  │   ├─ PrintWindowCapture.cs
│  │   └─ WgcCapture.cs         Windows.Graphics.Capture (окна и мониторы)
│  └─ Imaging/                  BgraImage, Scaler, PngEncoder, склейка мониторов
├─ tests/ScreenshotMcp.Tests/
├─ .github/workflows/build.yml
├─ README.md / README_RU.md / mcp.json.example
└─ .gitignore                   шаблон VisualStudio вместо Python
```

`server.py` остаётся рядом до паритета функций, в последнем шаге удаляется (в истории git он сохранится).

## 5. Захват окон: три бэкенда

Новый параметр `method` у `screenshot_window`: `auto` (по умолчанию) | `wgc` | `printwindow` | `screen`.

| Бэкенд | Перекрытое окно | GPU/DirectX | Свёрнутое окно | Минусы |
|---|---|---|---|---|
| `screen` (BitBlt с экрана, как сейчас) | ✗ | ✓ | ✗ | Снимает то, что видно на экране |
| `printwindow` (`PW_RENDERFULLCONTENT`) | ✓ | часто ✓ | ✗ | Некоторые окна отдают чёрный кадр |
| `wgc` (Windows.Graphics.Capture) | ✓ | ✓ | ✗ | Нужна Win10 1903+ и D3D11-устройство; первый кадр приходит с задержкой ~50–100 мс; на Win10 появляется жёлтая рамка (на Win11 её можно отключить через `IsBorderRequired = false`) |

Стратегия `auto`:
1. **WGC**, если доступен.
2. Иначе **PrintWindow**. Если кадр целиком чёрный или пустой, переходим к п. 3.
3. Иначе **screen**. Если окно перекрыто, в ответе будет подсказка вызвать `bring_to_front`.

Для свёрнутых окон: опция `restore_minimized`. Окно разворачивается без активации (`SW_SHOWNOACTIVATE`), снимается и сворачивается обратно. Без этой опции остаётся понятная ошибка, как сейчас.

`client_only` работает для всех бэкендов: клиентская область обрезается из полного кадра через `ClientToScreen` и `DWMWA_EXTENDED_FRAME_BOUNDS`.

## 6. Экран и мониторы

- **Модель монитора:** `index`, `device_name` (`\\.\DISPLAY1`), `friendly_name` (например, «DELL U2720Q», через `QueryDisplayConfig` + `DisplayConfigGetDeviceInfo`), `primary`, `rect`, `work_area`, `dpi`, `scale`.
- **Стабильная нумерация:** сортировка по номеру из имени устройства `DISPLAYn`. Он не меняется между запусками и обычно совпадает с нумерацией в настройках Windows (это проверяется на реальной машине). `monitor` принимает число или строку (`"DISPLAY2"`, подстроку `friendly_name`).
- **Все мониторы**, параметр `layout`:
  - `combined` (по умолчанию, как сейчас): одна картинка виртуального экрана. Промежутки между мониторами разной высоты заливаются чёрным. В тексте ответа указаны координаты каждого монитора на картинке.
  - `separate`: по одной картинке на монитор в одном `CallToolResult`. Каждая масштабируется отдельно, чтобы 2×4K не превратились в узкую полоску 1920 px.
- Бэкенд для мониторов: GDI `BitBlt` + `CAPTUREBLT` по умолчанию, WGC `CreateForMonitor` по `method: "wgc"` (для полноэкранных игр в exclusive/flip-режимах, где GDI даёт чёрный кадр).
- Регион `x/y/width/height` обрезается по виртуальному экрану. Если регион не пересекается с экраном, возвращается ошибка.

## 7. API инструментов (совместимо с текущим)

| Инструмент | Параметры (новое **жирным**) | Аннотации |
|---|---|---|
| `screenshot_screen` | `monitor` (**int или строка**), `all_monitors`, **`layout`**, `x/y/width/height`, `max_width`, **`max_height`**, **`include_cursor`**, **`method`** | readOnly |
| `screenshot_window` | `hwnd`/`title`/`process`, `client_only`, `bring_to_front`, `max_width`, **`max_height`**, **`method`**, **`restore_minimized`**, **`include_cursor`** | readOnly* |
| `find_window` | `hwnd`/`title`/`process`, `limit`, **`include_minimized`** | readOnly |
| `focus_window` | `hwnd`/`title`/`process`, `restore` | не readOnly |
| **`list_monitors`** (новый) | — | readOnly |

\* `bring_to_front` и `restore_minimized` меняют состояние окон; это отражается в описании инструмента.

- `max_width`/`max_height` ограничивают размеры с сохранением пропорций, `0` = без ограничения. Поведение по умолчанию (1920 по ширине) остаётся прежним.
- Ответ: текстовый блок с метаданными (источник, координаты, исходный и итоговый размер, бэкенд) плюс image-блок PNG. JSON в `find_window` и `list_monitors` сохраняет текущие поля (`hwnd` как `0x…`, `rect`, `client_rect`), новые поля только добавляются.
- Неоднозначный `title`/`process` по-прежнему даёт ошибку со списком кандидатов.

## 8. Этапы работ

Каждый этап отдельным коммитом в ветку; после каждого этапа проект собирается и тесты проходят.

1. **Каркас.** `.slnx`, `Directory.Build.props`, `global.json`, пустой MCP-сервер на SDK (stdio, логи только в stderr, чтобы не ломать протокол), `.gitignore`, CI-сборка на `windows-latest`.
2. **Проверка рисков (spike).** В CI проверяем, что NativeAOT-публикация работает вместе с MCP SDK и WinRT-проекцией WGC, и что тестовый инструмент возвращает картинку. По результату выбираем AOT или single-file.
3. **Win32-слой.** Мониторы (с именами, DPI и стабильной нумерацией), окна (`EnumWindows`, фильтры cloaked/owner/Progman), процессы, `DWMWA_EXTENDED_FRAME_BOUNDS`, фокус (`AttachThreadInput` + `SendInput` Alt вместо `keybd_event`).
4. **Изображения.** `BgraImage`, PNG-энкодер, качественный даунскейл, склейка мониторов, отрисовка курсора.
5. **Захват экрана.** GDI: монитор, регион, все мониторы (`combined`/`separate`). Инструменты `screenshot_screen`, `list_monitors`, `find_window`, `focus_window` с паритетом Python-версии.
6. **Захват отдельных окон.** Бэкенды `screen` → `printwindow` → `wgc`, стратегия `auto`, `client_only`, `restore_minimized`. Инструмент `screenshot_window`.
7. **CLI.** `--list`, `--screenshot out.png [--monitor N | --all [--separate] | --window 0x… ]`, для ручной проверки без MCP-клиента.
8. **Тесты** (см. раздел 9).
9. **Документация.** README / README_RU, `mcp.json.example` под `.exe`, описание бэкендов и ограничений; удаление `server.py`.
10. **Релизы.** Workflow по тегу `v*`: публикация `win-x64` и `win-arm64`, zip в GitHub Release. Опционально: NuGet-пакет MCP-сервера для запуска через `dnx`.

## 9. Тестирование

- **Юнит-тесты (CI):** PNG-энкодер (декодировать обратно и сравнить пиксели), масштабирование и расчёт размеров, парсинг `hwnd` (`0x…` и десятичный), разбор `monitor` (число или имя), валидация и обрезка региона, логика выбора бэкенда в `auto` (через фейки), неоднозначный поиск окна.
- **Протокол (CI):** запуск exe как процесса, `initialize` → `tools/list` → снимок `inputSchema` (чтобы случайно не сломать совместимость), `tools/call` с неверными аргументами возвращает `isError`.
- **Интеграция с рабочим столом** (`[Trait("Category","Desktop")]`, в CI по возможности): снимок монитора не пустой; тестовое окно с известной заливкой, перекрытое другим окном, снимается через `printwindow`/`wgc` с правильным цветом.
- **Ручная проверка на реальной машине:** 2+ монитора с разным DPI, UnrealEditor/игра, браузер, свёрнутое окно, Cursor/Claude как MCP-клиент.

Ограничение: в облачном контейнере, где я работаю, Linux и нет .NET SDK. Код можно собрать (`EnableWindowsTargeting`), но захват реально проверяется только на Windows, то есть в CI на `windows-latest` и на вашей машине.

## 10. Риски

| Риск | Что делаем |
|---|---|
| MCP SDK или CsWinRT несовместимы с NativeAOT | Проверяем на этапе 2. Запасные варианты: single-file self-contained или свой JSON-RPC вместо SDK. |
| WGC недоступен (старая Windows, сессия без GPU, RDP) | `auto` откатывается на PrintWindow → screen, бэкенд указывается в ответе. |
| Жёлтая рамка WGC на Win10 | Только на время съёмки (сессия закрывается сразу после кадра); на Win11 отключается. |
| PrintWindow отдаёт чёрный кадр у части приложений | Детект пустого кадра и fallback. |
| Большие картинки (склейка 3×4K) превышают лимиты клиента | Лимиты `max_width`/`max_height` по умолчанию и `layout: separate`. |

## 11. Открытые вопросы

1. Удалить `server.py` в конце или оставить как legacy-вариант?
2. Нужен ли один exe без зависимостей (NativeAOT), или допустимо требовать установленный .NET 10 Runtime?
3. «Все мониторы разом»: по умолчанию одна склеенная картинка (как сейчас) или отдельные картинки на монитор?
4. Минимальная версия Windows: 10 1903+ (нужна для WGC) подходит?
5. Нужны ли JPEG/WebP для уменьшения размера ответа и сохранение снимка на диск (`save_to`)?
