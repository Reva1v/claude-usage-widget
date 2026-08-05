# Порт Claude Usage Widget на Windows — дизайн

Дата: 2026-08-05. Статус: одобрен.

## Цель

Переписать macOS-виджет (Swift/SwiftUI) на C#/.NET + WPF так, чтобы вся логика
работала на Windows. Репозиторий становится чисто Windows-проектом: Swift-код
удаляется. Функциональный объём v1: десктоп-виджет с циферблатами, трей с живой
цифрой и меню, автозапуск, опциональная полоска в панели задач. Многоаккаунтность
(два виджета / объединённый) — не в v1, но ядро проектируется под неё.

## Структура репозитория

```
ClaudeUsageWidget.sln
src/
  ClaudeUsageWidget.Core/    — портированная логика, без UI (net8.0)
    Usage/       UsageTypes, UsageDecoder, UsageMath, ModelBuckets, ThresholdLevel
    Status/      ServiceStatus, StatusAPI (status.claude.com)
    Store/       UsageStore, StatusStore (цикл 5 мин, backoff на 429)
    Formatting/  TrayText (бывший MenuBarText)
  ClaudeUsageWidget.App/     — WPF-приложение (net8.0-windows)
    Web/         ClaudeWebSession + LoginWindow (WebView2)
    Windows/     DesktopWidgetWindow, TaskbarBandWindow
    Tray/        иконка, меню, рендер цифры в иконку
    Views/       циферблаты (перенос DialView/DialModel/Theme)
    Settings/    JSON-настройки в %APPDATA%, автозапуск через реестр
tests/
  ClaudeUsageWidget.Core.Tests/  — xUnit, перенос тестов ядра (~98 в оригинале)
```

Соответствие Swift → C# по файлам сохраняется максимально близко, чтобы при
портировании можно было сверяться с оригиналом построчно (оригинал доступен в
git-истории и в апстриме TadelUnso/claude-usage-widget).

## Получение данных — перенос механики as-is

- **Веб-сессия claude.ai.** WebView2 с выделенным user-data-folder
  (`%LOCALAPPDATA%\ClaudeUsageWidget\profiles\default`). Окно логина открывает
  `https://claude.ai/login`; наблюдатель куки ждёт появления `sessionKey` на
  домене claude.ai, после чего окно закрывается и запускается refresh.
- **Fetch как браузерная навигация.** Запросы к API выполняются невидимым
  WebView2 через настоящую навигацию на URL с извлечением текста страницы —
  тот же приём, что WKWebView в оригинале: Cloudflare видит браузерную
  навигацию с куками логина, а не headless-клиент. User-Agent — родной
  Edge/Chromium (не подменять на чужой браузер: фингерпринт должен совпадать
  с движком, иначе Turnstile-петля).
- **Организация.** `GET https://claude.ai/api/organizations` → фильтр по
  capability `chat`, приоритет `raven_type == "team"`, иначе первая; UUID
  кэшируется в настройках и сбрасывается при 401/403.
- **Usage.** `GET https://claude.ai/api/organizations/{id}/usage` →
  `UsageDecoder.Snapshot`. Обновление каждые 5 минут; 429 → backoff с учётом
  Retry-After и паддингом, как в оригинале; 401/403 → состояние
  «не залогинен», сброс кэша организации.
- **Статус сервиса.** `GET https://status.claude.com/api/v2/summary.json`
  обычным HttpClient; предпочитается компонент «Claude Code», иначе общий
  статус страницы. Эндпоинт OAuth `api.anthropic.com/api/oauth/usage`
  сознательно не используется (липкий rate-limit — причина, по которой
  оригинал ушёл на веб-сессию).

## Режимы отображения

1. **Десктоп-виджет.** Квадрат 2×2: SESSION (5 ч), WEEK (7 д), модельный
   недельный лимит (выбор в меню, когда сервер вернул больше одного), STATUS
   (клик → status.claude.com). Заливка дуги по доле лимита: зелёный < 60 %,
   янтарный < 85 %, красный выше; в центре процент и время до сброса. Окно
   borderless, прозрачный фон, всегда под всеми окнами: WS_EX_NOACTIVATE,
   без активации, и прижатие к низу Z-порядка (HWND_BOTTOM) при каждой
   попытке всплыть через обработку WM_WINDOWPOSCHANGING. Drag за середину,
   resize за края с сохранением квадрата (сторона 150–340 px, DPI-aware),
   позиция и размер запоминаются. Hover-хедер: кнопка скрыть и замок
   (блокирует drag/resize).
2. **Трей.** NotifyIcon: в иконке рендерится одно живое число (по умолчанию —
   процент сессии; выбор метрики в меню), полный расклад в tooltip. Меню:
   Refresh now, Sign in to Claude.ai, выбор модельного лимита, Show widget,
   Taskbar band on/off, Lock position, Launch at login, Quit.
3. **Полоска в панели задач** (опция, выключена по умолчанию). Малое окно,
   встроенное child-ом в Shell_TrayWnd по технике TrafficMonitor; рисует
   колонки «метка над значением» как в меню-баре macOS. При неудаче
   встраивания — деградация до оверлея поверх пустой области таскбара.
   Официальная платформа виджетов Windows 11 не подходит: сторонние виджеты
   живут только в выезжающей панели (Win+W), на таскбар/рабочий стол не
   закрепляются, рисуются только Adaptive Cards.

## Настройки и автозапуск

JSON-файл `%APPDATA%\ClaudeUsageWidget\settings.json`: позиция/сторона
виджета, видимость, замок, выбранный модельный бакет, метрика трея, режим
полоски, кэш ID организации, список профилей аккаунтов. Автозапуск — ключ
реестра `HKCU\...\CurrentVersion\Run` (галочка в трей-меню).

## Задел на два аккаунта

В Core — `AccountProfile { Id, DisplayName, ProfileFolder }`; каждому профилю
соответствует свой WebView2 user-data-folder и свой `UsageStore`. v1 создаёт
ровно один профиль. Будущие сценарии (второй виджет на второй аккаунт,
объединённый виджет) добавляются созданием второго профиля и новым UI-слоем,
без переделки ядра.

## Тесты

Чистая логика переносится с тестами 1:1 на xUnit: UsageMath, UsageDecoder,
ModelBuckets, ThresholdLevel, TrayText, Dial-геометрия/модель, UsageStore и
StatusStore с подставными часами и фейковым fetch. Сетевые обёртки — smoke-
тесты по образцу оригинала.

## Сборка и CI

`dotnet publish -c Release` → self-contained однофайловый exe (win-x64).
GitHub Actions: build + test на windows-latest для push/PR в main. Маковские
workflow, appcast.xml, Makefile, Package.swift — удаляются вместе со Swift-
кодом.

## Что сознательно не переносится

- Sparkle/автообновления (перевыпуск exe вручную; инфраструктура релизов —
  отдельная будущая задача).
- Auth/Keychain и StatuslineUsageCache — в оригинале с v0.1.6 не подключены.
- Ko-fi кнопка, Homebrew cask, нотаризация Apple.

## Риски

- Chromium может оборачивать JSON-ответ при навигации; извлечение текста
  должно брать сырое тело (`document.body.innerText` на странице plain-text
  ответа), сверить на реальном ответе при первом запуске.
- Встраивание в таскбар — неофициальный приём: после крупных обновлений
  Windows может требовать подстройки; режим опционален и деградирует до
  оверлея.
- Usage-API недокументирован и может измениться — как и у оригинала.
