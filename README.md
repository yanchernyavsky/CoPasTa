# CoPasTa

[Русский](#русский) · [English](#english)

**Скачать готовый EXE:** [последний релиз](https://github.com/yanchernyavsky/CoPasTa/releases/latest)  
**Download the EXE:** [latest release](https://github.com/yanchernyavsky/CoPasTa/releases/latest)

---

## Русский

Небольшое трей-приложение для Windows:

- **выделил текст мышью и отпустил кнопку** — выделенный текст один раз копируется в буфер;
- **двойной щелчок в текстовом поле** — вставляет текст из буфера (скопированный приложением или вручную).

Реализация написана с нуля, без сторонних библиотек.

### Поведение

- Копирование происходит **только в конце выделения**, по отпусканию левой кнопки мыши.
  Простое нажатие/клик и выделение с клавиатуры буфер не трогают; во время протягивания
  буфер тоже не меняется.
- Вставка по двойному щелчку работает **только в текстовых полях** (UI Automation проверяет,
  что в фокусе редактируемый элемент `Edit` или редактируемый `Document`, а не парольное поле).
  В других местах двойной щелчок ничего не делает.
- Сначала текст читается через **UI Automation** (без изменения буфера и выделения).
  Если приложение UIA не поддерживает — используется **«Резервный способ (Ctrl+C)»**:
  приложение посылает `Ctrl+C` активному окну, читает результат из буфера и, если скопировать
  не удалось, возвращает прежний текст. Он нужен для Qt-приложений, старых контролов,
  терминалов и т.п., которые не открывают выделение через UIA. Недостаток: буфер на время
  перезаписывается, а само нажатие может быть перехвачено приложением. Если во всех ваших
  программах основной способ работает — галочку можно снять.
- Вся тяжёлая работа (UI Automation, буфер, `SendKeys`) выполняется в отдельном STA-потоке,
  поэтому потягивание мышью и отпускание кнопки не подтормаживают.

### Меню в трее

- **Включено / Отключено** — также переключается двойным щелчком по иконке;
- **Пауза** — 1, 5 или 15 минут, остаток времени виден в подсказке иконки;
- **Вставка по двойному щелчку** — включена по умолчанию;
- **Резервный способ (Ctrl+C)** — по умолчанию выключен (включайте только если приложение не отдаёт выделение через UIA);
- **Добавить в автозагрузку** — запись `CoPasTa` в `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`;
- **Выход**.

Если двойной щелчок не срабатывает, включите **«Диагностический лог»** (или запустите с ключом
`CoPasTa.exe --log`) и посмотрите `%TEMP%\CoPasTa.log` — там видно, было ли нажатие распознано,
какой элемент был в фокусе и было ли отправлено `Ctrl+V`. Текст буфера в лог не пишется.

Приложение не показывает окон, ничего не отправляет в сеть и не хранит историю.

### Сборка

Нужен [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

или вручную:

```powershell
dotnet publish .\CoPasTa.csproj -c Release -r win-x64 --self-contained false `
  -p:PublishSingleFile=true -p:DebugType=None -p:DebugSymbols=false `
  -o .\publish\win-x64
```

`build.ps1` собирает два варианта:

- `publish\win-x64\CoPasTa.exe` — компактный, требует **.NET 8 Desktop Runtime**;
- `publish\win-x64-portable\CoPasTa.exe` — полностью автономный, работает без установленного .NET (больше по размеру).

В релизе доступны оба файла.

### Удаление автозагрузки

Снимите галочку в меню трее или удалите параметр `CoPasTa` из
`HKCU\Software\Microsoft\Windows\CurrentVersion\Run`.

### Файлы

| Файл | Назначение |
| --- | --- |
| `Program.cs` | Точка входа (STA, приложение без окна) |
| `TrayContext.cs` | Трей, меню, хук мыши, отложенное копирование/вставка, пауза, автозагрузка |
| `ClipboardWorker.cs` | Отдельный STA-поток для буфера / UIA / SendKeys |
| `SelectionReader.cs` | Чтение выделения через UIA + запасной Ctrl+C |
| `NativeMethods.cs` | P/Invoke Win32 |
| `IconFactory.cs` | Иконка в трее, нарисованная в коде |
| `app.ico` | Иконка самого exe (подключена через `ApplicationIcon`) |
| `DebugLog.cs` | Необязательный лог диагностики (`%TEMP%\CoPasTa.log`) |
| `CoPasTa.csproj` | Проект .NET 8 / WinForms |
| `build.ps1` | Сборка one-file exe |
| `LICENSE` | Лицензия MIT © 2026 Yan Chernyavsky |

### Лицензия

[MIT](LICENSE) — можно использовать, изменять и распространять, в том числе в коммерческих
проектах, при сохранении уведомления об авторстве.

---

## English

A tiny Windows tray utility:

- **select text with the mouse and release the button** — the selection is copied to the clipboard once;
- **double-click in a text field** — pastes text from the clipboard (copied by the app or manually).

Written from scratch, with no third-party libraries.

### Behavior

- Copying happens **only at the end of a selection**, when the left mouse button is released.
  A simple click and keyboard selection never touch the clipboard; the clipboard is also unchanged
  while you are dragging.
- Double-click paste works **only in text fields** (UI Automation checks that the focused element
  is an editable `Edit` or editable `Document`, not a password field). Anywhere else a double-click
  does nothing.
- Text is first read via **UI Automation** (without touching the clipboard or the selection).
  If an application does not support UIA, the **"Ctrl+C fallback"** is used: the app sends `Ctrl+C`
  to the active window, reads the clipboard and restores the previous text if copying failed.
  It is meant for Qt apps, legacy controls, terminals, etc., which do not expose their selection
  through UIA. Drawbacks: the clipboard is temporarily overwritten and the keystroke may be
  intercepted by the target app. If the primary method works in all your apps, you can turn it off.
- All heavy work (UI Automation, clipboard, `SendKeys`) runs on a separate STA thread, so dragging
  and releasing the mouse never stutters.

### Tray menu

- **Enabled / Disabled** — also toggled by double-clicking the tray icon;
- **Pause** — 1, 5 or 15 minutes; the remaining time is shown in the icon tooltip;
- **Paste on double-click** — enabled by default;
- **Ctrl+C fallback** — disabled by default (enable it only if an app does not expose its selection via UIA);
- **Add to startup** — writes `CoPasTa` to `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`;
- **Exit**.

If double-click paste does not work, enable **"Diagnostic log"** (or run `CoPasTa.exe --log`)
and check `%TEMP%\CoPasTa.log` — it shows whether the click was recognized, which element was
focused and whether `Ctrl+V` was sent. Clipboard contents are never logged.

The app shows no windows, sends nothing over the network and keeps no history.

### Build

You need the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

or manually:

```powershell
dotnet publish .\CoPasTa.csproj -c Release -r win-x64 --self-contained false `
  -p:PublishSingleFile=true -p:DebugType=None -p:DebugSymbols=false `
  -o .\publish\win-x64
```

`build.ps1` produces two variants:

- `publish\win-x64\CoPasTa.exe` — compact, requires the **.NET 8 Desktop Runtime**;
- `publish\win-x64-portable\CoPasTa.exe` — fully self-contained, needs no installed .NET (larger).

Both files are attached to the GitHub release.

### Removing from startup

Uncheck the option in the tray menu, or delete the `CoPasTa` value from
`HKCU\Software\Microsoft\Windows\CurrentVersion\Run`.

### Files

| File | Purpose |
| --- | --- |
| `Program.cs` | Entry point (STA, no window) |
| `TrayContext.cs` | Tray icon, menu, mouse hook, deferred copy/paste, pause, startup |
| `ClipboardWorker.cs` | Dedicated STA thread for clipboard / UIA / SendKeys |
| `SelectionReader.cs` | Reading the selection via UIA + Ctrl+C fallback |
| `NativeMethods.cs` | Win32 P/Invoke |
| `IconFactory.cs` | Tray icon drawn in code |
| `app.ico` | Executable icon (wired up via `ApplicationIcon`) |
| `DebugLog.cs` | Optional diagnostic log (`%TEMP%\CoPasTa.log`) |
| `CoPasTa.csproj` | .NET 8 / WinForms project |
| `build.ps1` | One-file exe build script |
| `LICENSE` | MIT license © 2026 Yan Chernyavsky |

### License

[MIT](LICENSE) — free to use, modify and redistribute, including in commercial projects,
provided the copyright notice is preserved.
