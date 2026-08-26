# screenshot MCP

Локальный MCP для Windows: скрин экрана или окна, координаты окна, перевод фокуса.

Один файл, стандартная библиотека Python, без Puppeteer/npm. Картинка приходит в ответе инструмента (PNG), на диск ничего не пишется.

English: [README.md](README.md)

## Инструменты

| Tool | Назначение |
|------|------------|
| `screenshot_screen` | Основной монитор; `monitor` (1, 2, …) или `all_monitors` для всех сразу; либо регион `x/y/width/height` |
| `screenshot_window` | Окно по `hwnd`, подстроке `title` или имени процесса |
| `find_window` | Список видимых окон с прямоугольниками; без фильтра — все |
| `focus_window` | Вынести окно на передний план (и развернуть, если свёрнуто) |

`hwnd` лучше всего брать из `find_window`. Если по title/process находится несколько окон — инструмент вернёт ошибку и попросит `hwnd`.

По умолчанию PNG ужимается по ширине до 1920 (`max_width`). `0` — нативное разрешение.

## Запуск без Cursor

Нужен Python 3.10+ (проверялось на 3.12). Зависимостей нет.

```bat
python server.py --list
python server.py --screenshot test.png
```

Без аргументов процесс слушает MCP по stdio.

## Cursor

В `mcp.json` клиента:

```json
{
  "mcpServers": {
    "screenshot": {
      "command": "C:\\Program Files\\Python312\\python.exe",
      "args": ["-u", "E:/EUREKA/mcp_screenshot/server.py"]
    }
  }
}
```

После добавления перезапусти MCP / Cursor.

## Заметки

- Скрин окна берёт пиксели с экрана (то, что видно). Если окно перекрыто — сначала `focus_window` или `bring_to_front: true`.
- Свёрнутое окно снять нельзя, пока его не восстановить.
- Только Windows.
