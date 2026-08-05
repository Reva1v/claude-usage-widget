# Windows Port Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Портировать macOS-виджет лимитов Claude на Windows: C#/.NET 8 + WPF, данные через веб-сессию claude.ai в WebView2, десктоп-виджет + трей + опциональная полоска в таскбаре.

**Architecture:** Ядро (`ClaudeUsageWidget.Core`, net8.0) — чистая портированная логика без UI и без таймеров, всё время и I/O инжектируются. Оболочка (`ClaudeUsageWidget.App`, net8.0-windows, WPF + WinForms NotifyIcon) владеет окнами, треем, WebView2 и расписанием обновлений. Swift-оригинал остаётся в дереве до финальной задачи — каждая задача портирования сверяется с ним построчно.

**Tech Stack:** .NET 8 SDK, WPF, WinForms (только NotifyIcon), Microsoft.Web.WebView2 (единственный NuGet-пакет), xUnit, System.Text.Json.

## Global Constraints

- Спека: `docs/superpowers/specs/2026-08-05-windows-port-design.md` — читать перед началом любой задачи.
- Swift-оригинал (`Sources/`, `Tests/`) НЕ трогать и НЕ удалять до Task 18 — это источник истины для портирования.
- Единственный внешний NuGet-пакет — `Microsoft.Web.WebView2`. Никаких других зависимостей (включая тестовые, кроме xUnit + Microsoft.NET.Test.Sdk).
- Везде `DateTimeOffset`, не `DateTime`. Везде `System.Text.Json`, не Newtonsoft.
- Один namespace на проект: `ClaudeUsageWidget.Core` и `ClaudeUsageWidget.App` (папки — по спеке, без под-namespace'ов).
- OAuth-эндпоинт `api.anthropic.com/api/oauth/usage` НЕ использовать нигде — только веб-сессия claude.ai (причина в спеке: липкий rate-limit).
- Тесты гоняются командой `dotnet test` из корня репозитория; перед каждым коммитом — зелёный прогон.
- Сообщения коммитов — на английском, с трейлером `Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>`.
- Комментарии в коде — на русском, в стиле оригинала: только «почему», не «что».

---

### Task 1: Solution scaffold

**Files:**
- Create: `ClaudeUsageWidget.sln`
- Create: `src/ClaudeUsageWidget.Core/ClaudeUsageWidget.Core.csproj`
- Create: `tests/ClaudeUsageWidget.Core.Tests/ClaudeUsageWidget.Core.Tests.csproj`
- Create: `.editorconfig` (4 пробела, file-scoped namespaces, LF)

**Interfaces:**
- Produces: solution, в которую все последующие задачи добавляют файлы; `dotnet test` работает из корня.

- [ ] **Step 1: Создать проекты**

```powershell
dotnet new sln -n ClaudeUsageWidget
dotnet new classlib -o src/ClaudeUsageWidget.Core -n ClaudeUsageWidget.Core -f net8.0
dotnet new xunit -o tests/ClaudeUsageWidget.Core.Tests -n ClaudeUsageWidget.Core.Tests -f net8.0
dotnet sln add src/ClaudeUsageWidget.Core tests/ClaudeUsageWidget.Core.Tests
dotnet add tests/ClaudeUsageWidget.Core.Tests reference src/ClaudeUsageWidget.Core
```

Удалить сгенерированные `Class1.cs` и `UnitTest1.cs`. В Core.csproj добавить `<Nullable>enable</Nullable>`, `<ImplicitUsings>enable</ImplicitUsings>`, `<InternalsVisibleTo Include="ClaudeUsageWidget.Core.Tests" />` (через `<ItemGroup><InternalsVisibleTo .../></ItemGroup>`).

- [ ] **Step 2: Smoke-тест инфраструктуры**

`tests/ClaudeUsageWidget.Core.Tests/SmokeTests.cs`:

```csharp
namespace ClaudeUsageWidget.Core.Tests;

public class SmokeTests
{
    [Fact]
    public void TestInfrastructureRuns() => Assert.True(true);
}
```

- [ ] **Step 3: Прогнать**

Run: `dotnet test`
Expected: 1 passed.

- [ ] **Step 4: Commit**

```bash
git add -A && git commit -m "chore: scaffold .NET solution for the Windows port"
```

---

### Task 2: Типы Usage — bucket, snapshot, ошибки, состояния

**Files:**
- Create: `src/ClaudeUsageWidget.Core/Usage/UsageTypes.cs`
- Test: `tests/ClaudeUsageWidget.Core.Tests/UsageTypesTests.cs`
- Reference: `Sources/ClaudeUsageWidgetCore/Usage/UsageTypes.swift`, `Tests/ClaudeUsageWidgetCoreTests/UsageErrorTests.swift`

**Interfaces:**
- Produces (все последующие задачи опираются на эти типы — имена и сигнатуры точные):

```csharp
public sealed record UsageBucket(double Utilization, DateTimeOffset? ResetsAt);

public sealed class UsageSnapshot : IEquatable<UsageSnapshot>
{
    public IReadOnlyDictionary<string, UsageBucket> Buckets { get; }
    public DateTimeOffset? SourceUpdatedAt { get; }
    public UsageSnapshot(IReadOnlyDictionary<string, UsageBucket> buckets,
                         DateTimeOffset? sourceUpdatedAt = null);
    public UsageBucket? this[string key] { get; }   // null, если ключа нет
    // Equals/GetHashCode: поэлементное сравнение словаря + SourceUpdatedAt
}

public enum UsageErrorKind { NoCredentials, Unauthorized, MalformedResponse, RateLimited, Network }

public sealed record UsageError(UsageErrorKind Kind, int? RetryAfterSeconds = null, string? Message = null)
{
    public static readonly UsageError NoCredentials;
    public static readonly UsageError Unauthorized;
    public static readonly UsageError MalformedResponse;
    public static UsageError RateLimited(int? retryAfterSeconds);
    public static UsageError Network(string message);
    public string Description { get; }   // вычисляемое, в равенстве не участвует
}

public sealed class UsageException : Exception
{
    public UsageError Error { get; }
    public UsageException(UsageError error);   // Message = error.Description
}

public abstract record UsageState
{
    public sealed record Loading : UsageState;
    public sealed record Ok(UsageSnapshot Snapshot, DateTimeOffset FetchedAt) : UsageState;
    public sealed record Failed(UsageError Error) : UsageState;
}
```

- [ ] **Step 1: Написать падающие тесты**

Тексты `Description` — дословно из Swift (`UsageTypes.swift:50-67`):

```csharp
namespace ClaudeUsageWidget.Core.Tests;

public class UsageTypesTests
{
    [Fact]
    public void SnapshotEqualityComparesBucketContents()
    {
        var a = new UsageSnapshot(new Dictionary<string, UsageBucket>
            { ["five_hour"] = new(42, null) });
        var b = new UsageSnapshot(new Dictionary<string, UsageBucket>
            { ["five_hour"] = new(42, null) });
        Assert.Equal(a, b);
        Assert.NotEqual(a, new UsageSnapshot(new Dictionary<string, UsageBucket>
            { ["five_hour"] = new(43, null) }));
    }

    [Fact]
    public void IndexerReturnsNullForMissingKey()
    {
        var s = new UsageSnapshot(new Dictionary<string, UsageBucket>());
        Assert.Null(s["absent"]);
    }

    [Theory]
    [InlineData(UsageErrorKind.NoCredentials, "No Claude.ai web session was found.")]
    [InlineData(UsageErrorKind.Unauthorized, "The Claude.ai session expired. Sign in again from the widget menu.")]
    [InlineData(UsageErrorKind.MalformedResponse, "The server returned something unexpected.")]
    public void DescriptionsReadAsSentences(UsageErrorKind kind, string expected)
    {
        var error = kind switch
        {
            UsageErrorKind.NoCredentials => UsageError.NoCredentials,
            UsageErrorKind.Unauthorized => UsageError.Unauthorized,
            _ => UsageError.MalformedResponse,
        };
        Assert.Equal(expected, error.Description);
    }

    [Fact]
    public void RateLimitedDescriptionMentionsSelfRetry() =>
        Assert.Equal("The API is rate limited. The widget retries on its own.",
            UsageError.RateLimited(600).Description);

    [Fact]
    public void NetworkDescriptionIsTheMessage() =>
        Assert.Equal("boom", UsageError.Network("boom").Description);

    [Fact]
    public void RateLimitedErrorsCompareByRetryAfter()
    {
        Assert.Equal(UsageError.RateLimited(600), UsageError.RateLimited(600));
        Assert.NotEqual(UsageError.RateLimited(600), UsageError.RateLimited(null));
    }
}
```

Свериться с `Tests/ClaudeUsageWidgetCoreTests/UsageErrorTests.swift` и перенести оттуда недостающие случаи, если есть.

- [ ] **Step 2: Прогнать — убедиться, что не компилируется/падает**

Run: `dotnet test`
Expected: ошибка компиляции (типов ещё нет).

- [ ] **Step 3: Реализовать типы** по интерфейсу выше, сверяясь с `UsageTypes.swift`. `UsageSnapshot.Equals`: одинаковый размер словаря + для каждого ключа одинаковый `UsageBucket`; `GetHashCode` — XOR по ключам и количеству (стабильности достаточно).

- [ ] **Step 4: Прогнать тесты**

Run: `dotnet test`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "feat: port usage types, errors, and store states to C#"
```

---

### Task 3: UsageMath

**Files:**
- Create: `src/ClaudeUsageWidget.Core/Usage/UsageMath.cs`
- Test: `tests/ClaudeUsageWidget.Core.Tests/UsageMathTests.cs`
- Reference: `Sources/ClaudeUsageWidgetCore/Usage/UsageMath.swift`, `Tests/ClaudeUsageWidgetCoreTests/UsageMathTests.swift`

**Interfaces:**
- Consumes: ничего.
- Produces:

```csharp
public static class UsageMath
{
    public static string? RemainingText(DateTimeOffset? resetsAt, DateTimeOffset now);
    public static double Fraction(double utilization);   // 0-100 → 0-1 с клампом
    public static string PercentText(double fraction);   // "57%", эпсилон-сдвиг
}
```

- [ ] **Step 1: Написать падающие тесты** — точные значения из Swift-тестов:

```csharp
namespace ClaudeUsageWidget.Core.Tests;

public class UsageMathTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1_785_348_000);

    [Theory]
    [InlineData(59, "59s")]
    [InlineData(600, "10m")]
    [InlineData(3600, "1h 0m")]
    [InlineData(90_000, "1d 1h")]
    public void RemainingTextFormatsByMagnitude(int seconds, string expected) =>
        Assert.Equal(expected, UsageMath.RemainingText(Now.AddSeconds(seconds), Now));

    [Fact]
    public void PastOrMissingResetHasNoText()
    {
        Assert.Null(UsageMath.RemainingText(Now.AddSeconds(-1), Now));
        Assert.Null(UsageMath.RemainingText(null, Now));
    }

    [Theory]
    [InlineData(0, 0)] [InlineData(42, 0.42)] [InlineData(100, 1)]
    [InlineData(140, 1)] [InlineData(-5, 0)]
    public void FractionScalesAndClamps(double utilization, double expected) =>
        Assert.Equal(expected, UsageMath.Fraction(utilization), 10);

    [Theory]
    [InlineData(0.575, "58%")]   // 0.575*100 = 57.4999... в double — эпсилон обязателен
    [InlineData(0.574, "57%")]
    [InlineData(0, "0%")] [InlineData(1, "100%")]
    public void PercentTextRoundsAtBoundary(double fraction, string expected) =>
        Assert.Equal(expected, UsageMath.PercentText(fraction));
}
```

- [ ] **Step 2: Прогнать** — Run: `dotnet test`. Expected: не компилируется.

- [ ] **Step 3: Реализовать** — построчный порт `UsageMath.swift`: целочисленные секунды `(int)Math.Floor((resetsAt - now).TotalSeconds)`; форматы `"{d}d {h}h"` / `"{h}h {m}m"` / `"{m}m"` / `"{s}s"`; `PercentText` = `$"{(int)Math.Round(fraction * 100 + 1e-9)}%"` (`Math.Round` с `MidpointRounding.AwayFromZero`).

- [ ] **Step 4: Прогнать** — Run: `dotnet test`. Expected: PASS.

- [ ] **Step 5: Commit** — `git add -A && git commit -m "feat: port UsageMath"`

---

### Task 4: ThresholdLevel

**Files:**
- Create: `src/ClaudeUsageWidget.Core/Usage/ThresholdLevel.cs`
- Test: `tests/ClaudeUsageWidget.Core.Tests/ThresholdLevelTests.cs`
- Reference: `Sources/ClaudeUsageWidgetCore/Usage/ThresholdLevel.swift`, `Tests/ClaudeUsageWidgetCoreTests/ThresholdLevelTests.swift`

**Interfaces:**
- Produces:

```csharp
public enum ThresholdLevel { Ok, Warning, Danger }
public static class Thresholds
{
    public static ThresholdLevel Level(double fraction);  // <0.6 Ok, <0.85 Warning, иначе Danger
}
```

- [ ] **Step 1: Тесты** (границы включительно — 0.6 уже Warning, 0.85 уже Danger, как в Swift `..<`):

```csharp
namespace ClaudeUsageWidget.Core.Tests;

public class ThresholdLevelTests
{
    [Theory]
    [InlineData(0, ThresholdLevel.Ok)] [InlineData(0.59, ThresholdLevel.Ok)]
    [InlineData(0.6, ThresholdLevel.Warning)] [InlineData(0.84, ThresholdLevel.Warning)]
    [InlineData(0.85, ThresholdLevel.Danger)] [InlineData(1, ThresholdLevel.Danger)]
    public void LevelsMatchThresholds(double fraction, ThresholdLevel expected) =>
        Assert.Equal(expected, Thresholds.Level(fraction));
}
```

Свериться с `ThresholdLevelTests.swift`, добавить недостающие случаи.

- [ ] **Step 2: Прогнать** — Expected: не компилируется.
- [ ] **Step 3: Реализовать** (`fraction < 0.6 ? Ok : fraction < 0.85 ? Warning : Danger`).
- [ ] **Step 4: Прогнать** — Expected: PASS.
- [ ] **Step 5: Commit** — `git add -A && git commit -m "feat: port ThresholdLevel"`

---

### Task 5: UsageDecoder

**Files:**
- Create: `src/ClaudeUsageWidget.Core/Usage/UsageDecoder.cs`
- Test: `tests/ClaudeUsageWidget.Core.Tests/UsageDecoderTests.cs`
- Reference: `Sources/ClaudeUsageWidgetCore/Usage/UsageDecoder.swift`, `Tests/ClaudeUsageWidgetCoreTests/UsageDecoderTests.swift`

**Interfaces:**
- Consumes: `UsageSnapshot`, `UsageBucket`, `UsageException`, `UsageError.MalformedResponse` (Task 2).
- Produces:

```csharp
public static class UsageDecoder
{
    // Бросает UsageException(MalformedResponse) на не-JSON и на тело без бакетов.
    public static UsageSnapshot Snapshot(string json);
}
```

- [ ] **Step 1: Портировать ВСЕ тесты из `UsageDecoderTests.swift`** (там 17 кейсов — каждый переносится как `[Fact]`; payload'ы копировать дословно). Каркас и опорные значения:

```csharp
namespace ClaudeUsageWidget.Core.Tests;

public class UsageDecoderTests
{
    private const string Payload = """
    {
      "five_hour":            { "utilization": 42,   "resets_at": "2026-07-29T18:00:00Z" },
      "seven_day":            { "utilization": 17.5, "resets_at": "2026-08-02T00:00:00Z" },
      "seven_day_opus":       { "utilization": 3,    "resets_at": "2026-08-02T00:00:00Z" },
      "seven_day_oauth_apps": { "utilization": 0,    "resets_at": null },
      "currency":             "EUR"
    }
    """;

    [Fact]
    public void DecodesBuckets()
    {
        var s = UsageDecoder.Snapshot(Payload);
        Assert.Equal(4, s.Buckets.Count);
        Assert.Equal(42, s["five_hour"]!.Utilization);
        Assert.Equal(17.5, s["seven_day"]!.Utilization);
    }

    [Fact]
    public void ParsesIsoDates()
    {
        var s = UsageDecoder.Snapshot(Payload);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_785_348_000), s["five_hour"]!.ResetsAt);
    }

    [Fact]
    public void ParsesEpochDates()
    {
        var s = UsageDecoder.Snapshot("""{"a": {"utilization": 1, "resets_at": 1785348000}}""");
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_785_348_000), s["a"]!.ResetsAt);
    }

    [Fact]
    public void RejectsGarbage() =>
        Assert.Equal(UsageError.MalformedResponse,
            Assert.Throws<UsageException>(() => UsageDecoder.Snapshot("<html>Just a moment</html>")).Error);

    [Fact]
    public void FoldsScopedLimits()
    {
        var s = UsageDecoder.Snapshot("""
        {
          "five_hour": { "utilization": 57 },
          "seven_day_opus": null,
          "limits": [
            { "kind": "weekly_scoped", "percent": 8, "resets_at": "2026-08-02T02:59:00Z",
              "scope": { "model": { "display_name": "Fable" } } },
            { "kind": "five_hour", "percent": 57 }
          ]
        }
        """);
        Assert.Equal(8, s["seven_day_fable"]!.Utilization);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_785_639_540), s["seven_day_fable"]!.ResetsAt);
        Assert.Null(s["seven_day_five_hour"]);
    }

    [Fact]
    public void SlugsDisplayNames()
    {
        var s = UsageDecoder.Snapshot("""
        {
          "five_hour": { "utilization": 1 },
          "limits": [ { "kind": "weekly_scoped", "percent": 3,
              "scope": { "model": { "display_name": "Claude Opus 4.5" } } } ]
        }
        """);
        Assert.Equal(3, s["seven_day_claude_opus_4_5"]!.Utilization);
    }

    // ...остальные кейсы из UsageDecoderTests.swift: skipsScalars, keepsUnknownKeys,
    // toleratesMissingResetTime, rejectsBucketlessBody, rejectsBooleanUtilization,
    // rejectsBooleanResetTime, realBucketWins, skipsIncompleteScopedLimits,
    // scopedLimitsAloneAreEnough, toleratesMalformedLimitEntries,
    // skipsUnsluggableDisplayNames, parsesDates (fractional) — перенести все.
}
```

- [ ] **Step 2: Прогнать** — Expected: не компилируется.

- [ ] **Step 3: Реализовать** порт `UsageDecoder.swift` на `JsonDocument`:
  - Корень не объект / невалидный JSON → `UsageException(MalformedResponse)` (обернуть `JsonException` в try/catch).
  - Для каждого свойства корня: `ValueKind == Object` и есть `utilization` с `ValueKind == Number` (это автоматически отсекает булевы — у JSON true/false свой ValueKind) → бакет; `resets_at`: строка → `DateTimeOffset.TryParse` (ISO 8601, с дробными секундами и без, `DateTimeStyles.AssumeUniversal | AdjustToUniversal`); число > 0 → `DateTimeOffset.FromUnixTimeSeconds` (дробные — через `FromUnixTimeMilliseconds` не нужно, брать `(long)` секунды как в Swift `Date(timeIntervalSince1970:)` — допустимо `FromUnixTimeMilliseconds((long)(seconds*1000))` для точности); иное → null.
  - `limits`: массив; элементы с `kind == "weekly_scoped"`, числовым `percent`, `scope.model.display_name` строкой → ключ `"seven_day_" + Slug(displayName)`; slug: lower-case, split по не-буквам/не-цифрам (`char.IsLetterOrDigit`), join `_`; пустой slug — пропустить; настоящий top-level бакет побеждает синтетический.
  - Пустой набор бакетов → `MalformedResponse`.

- [ ] **Step 4: Прогнать** — Expected: PASS (все ~17).

- [ ] **Step 5: Commit** — `git add -A && git commit -m "feat: port UsageDecoder with scoped-limit folding"`

---

### Task 6: ModelBuckets

**Files:**
- Create: `src/ClaudeUsageWidget.Core/Usage/ModelBuckets.cs`
- Test: `tests/ClaudeUsageWidget.Core.Tests/ModelBucketsTests.cs`
- Reference: `Sources/ClaudeUsageWidgetCore/Usage/ModelBuckets.swift`, `Tests/ClaudeUsageWidgetCoreTests/ModelBucketsTests.swift`

**Interfaces:**
- Consumes: `UsageSnapshot` (Task 2).
- Produces:

```csharp
public static class ModelBuckets
{
    public static IReadOnlyList<string> Available(UsageSnapshot snapshot);
    public static string? Resolve(string? preferred, UsageSnapshot snapshot);
    public static string Label(string key);          // "seven_day_fable" → "FABLE"
    internal static bool IsModelKey(string key);
}
```

- [ ] **Step 1: Портировать тесты из `ModelBucketsTests.swift`** (все кейсы). Константы поведения: preference-порядок `["seven_day_fable", "seven_day_opus", "seven_day_sonnet"]`, исключения `{"seven_day_oauth_apps", "seven_day_overage_included"}`, остальные модельные ключи — по алфавиту после известных. Пример опорного теста:

```csharp
[Fact]
public void AvailableOrdersKnownFirstThenAlphabetical()
{
    var s = new UsageSnapshot(new Dictionary<string, UsageBucket>
    {
        ["seven_day_zeta"] = new(1, null),
        ["seven_day_opus"] = new(1, null),
        ["seven_day_fable"] = new(1, null),
        ["seven_day_oauth_apps"] = new(1, null),
        ["five_hour"] = new(1, null),
    });
    Assert.Equal(new[] { "seven_day_fable", "seven_day_opus", "seven_day_zeta" },
        ModelBuckets.Available(s));
}

[Fact]
public void ResolveHonoursPreferredWhileServerReturnsIt()
{
    var s = new UsageSnapshot(new Dictionary<string, UsageBucket>
    {
        ["seven_day_fable"] = new(1, null), ["seven_day_opus"] = new(1, null),
    });
    Assert.Equal("seven_day_opus", ModelBuckets.Resolve("seven_day_opus", s));
    Assert.Equal("seven_day_fable", ModelBuckets.Resolve("seven_day_gone", s));
    Assert.Equal("seven_day_fable", ModelBuckets.Resolve(null, s));
}
```

- [ ] **Step 2: Прогнать** — Expected: не компилируется.
- [ ] **Step 3: Реализовать** — построчный порт.
- [ ] **Step 4: Прогнать** — Expected: PASS.
- [ ] **Step 5: Commit** — `git add -A && git commit -m "feat: port ModelBuckets"`

---

### Task 7: DialModel и StatusLine

**Files:**
- Create: `src/ClaudeUsageWidget.Core/Views/DialModel.cs`
- Test: `tests/ClaudeUsageWidget.Core.Tests/DialModelTests.cs`
- Reference: `Sources/ClaudeUsageWidgetCore/Views/DialModel.swift`, `Tests/ClaudeUsageWidgetCoreTests/DialModelTests.swift`

**Interfaces:**
- Consumes: `UsageSnapshot`, `UsageMath`, `ModelBuckets`, `UsageState`, `UsageError` (Tasks 2-6).
- Produces:

```csharp
public sealed record DialModel(string Title, double? Fraction, string? Remaining)
{
    public static DialModel Make(string key, string title, UsageSnapshot? snapshot, DateTimeOffset now);
    // Всегда ровно 3 в порядке SESSION, WEEK, модельный (title "MODEL", если модельного нет)
    public static IReadOnlyList<DialModel> All(UsageSnapshot? snapshot, string? preferredModelKey, DateTimeOffset now);
}

public static class StatusLine
{
    // null == всё хорошо, строку не показывать. staleAfter = 15 минут.
    public static string? Text(UsageState state, DateTimeOffset now, DateTimeOffset? retryUntil = null);
}
```

Строки StatusLine — дословно из Swift: `"loading…"`, `"updated {x} ago"`, `"unexpected response from the API"`, `"rate limited"`, `"rate limited · retry in {x}"`, для Network — сам message. NoCredentials/Unauthorized → null (их показывает BlockingNotice).

- [ ] **Step 1: Портировать тесты из `DialModelTests.swift`** (все кейсы). Опорные:

```csharp
private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1_785_348_000);

[Fact]
public void AllProducesThreeDialsInFixedOrder()
{
    var s = new UsageSnapshot(new Dictionary<string, UsageBucket>
    {
        ["five_hour"] = new(42, null),
        ["seven_day"] = new(17.5, null),
        ["seven_day_fable"] = new(8, null),
    });
    var dials = DialModel.All(s, null, Now);
    Assert.Equal(new[] { "SESSION", "WEEK", "FABLE" }, dials.Select(d => d.Title));
    Assert.Equal(0.42, dials[0].Fraction!.Value, 10);
}

[Fact]
public void MissingSnapshotYieldsEmptyDials()
{
    var dials = DialModel.All(null, null, Now);
    Assert.Equal(new[] { "SESSION", "WEEK", "MODEL" }, dials.Select(d => d.Title));
    Assert.All(dials, d => Assert.Null(d.Fraction));
}

[Fact]
public void StaleSnapshotGetsUpdatedAgoLine()
{
    var state = new UsageState.Ok(
        new UsageSnapshot(new Dictionary<string, UsageBucket>()), Now.AddMinutes(-16));
    Assert.Equal("updated 16m ago", StatusLine.Text(state, Now));
}

[Fact]
public void RateLimitWithDeadlineCountsDown() =>
    Assert.Equal("rate limited · retry in 10m",
        StatusLine.Text(new UsageState.Failed(UsageError.RateLimited(600)), Now, Now.AddSeconds(600)));
```

- [ ] **Step 2: Прогнать** — Expected: не компилируется.
- [ ] **Step 3: Реализовать** — построчный порт обоих типов.
- [ ] **Step 4: Прогнать** — Expected: PASS.
- [ ] **Step 5: Commit** — `git add -A && git commit -m "feat: port DialModel and StatusLine"`

---

### Task 8: TrayText (порт MenuBarText)

**Files:**
- Create: `src/ClaudeUsageWidget.Core/Formatting/TrayText.cs`
- Test: `tests/ClaudeUsageWidget.Core.Tests/TrayTextTests.cs`
- Reference: `Sources/ClaudeUsageWidgetCore/Formatting/MenuBarText.swift`, `Tests/ClaudeUsageWidgetCoreTests/MenuBarTextTests.swift`

**Interfaces:**
- Consumes: `DialModel` (Task 7), `UsageMath` (Task 3).
- Produces:

```csharp
public sealed record TrayMetric(string Label, string Value);

public static class TrayText
{
    // Пустые title отбрасываются; label: ≤4 символов как есть, длиннее — первые 3;
    // value: процент или "—" при отсутствии данных.
    public static IReadOnlyList<TrayMetric> Metrics(IReadOnlyList<DialModel> models);
    internal static string LabelFor(string title);
}
```

- [ ] **Step 1: Портировать тесты из `MenuBarTextTests.swift`.** Опорные: `LabelFor("WEEK") == "WEEK"`, `LabelFor("SESSION") == "SES"`, метрика без данных → `"—"`.
- [ ] **Step 2: Прогнать** — Expected: не компилируется.
- [ ] **Step 3: Реализовать.**
- [ ] **Step 4: Прогнать** — Expected: PASS.
- [ ] **Step 5: Commit** — `git add -A && git commit -m "feat: port MenuBarText as TrayText"`

---

### Task 9: ServiceStatus и StatusDecoder

**Files:**
- Create: `src/ClaudeUsageWidget.Core/Status/ServiceStatus.cs`
- Test: `tests/ClaudeUsageWidget.Core.Tests/ServiceStatusTests.cs`
- Reference: `Sources/ClaudeUsageWidgetCore/Status/ServiceStatus.swift`, `Tests/ClaudeUsageWidgetCoreTests/ServiceStatusTests.swift`

**Interfaces:**
- Consumes: `UsageException`, `UsageError.MalformedResponse` (Task 2).
- Produces:

```csharp
public enum ServiceStatus { Operational, Degraded, PartialOutage, MajorOutage, Maintenance, Unknown }

public static class ServiceStatusText
{
    // OK / SLOW / PARTIAL / DOWN / MAINT / —
    public static string Label(ServiceStatus status);
}

public static class ServiceStatusParser
{
    public static ServiceStatus Component(string raw);   // "operational", "degraded_performance", ...
    public static ServiceStatus Indicator(string raw);   // "none", "minor", "major", "critical", "maintenance"
}

public static class StatusDecoder
{
    // Компонент "Claude Code" приоритетен; иначе page-wide indicator; иначе MalformedResponse.
    public static ServiceStatus Status(string json);
}
```

- [ ] **Step 1: Портировать тесты из `ServiceStatusTests.swift`** — маппинги строк, приоритет компонента над индикатором, fallback при переименовании компонента, MalformedResponse на мусор. Пример:

```csharp
[Fact]
public void PrefersClaudeCodeComponent()
{
    var status = StatusDecoder.Status("""
    {
      "components": [
        { "name": "Other", "status": "major_outage" },
        { "name": "Claude Code", "status": "operational" }
      ],
      "status": { "indicator": "critical" }
    }
    """);
    Assert.Equal(ServiceStatus.Operational, status);
}
```

- [ ] **Step 2: Прогнать** — Expected: не компилируется.
- [ ] **Step 3: Реализовать** порт с `JsonDocument`.
- [ ] **Step 4: Прогнать** — Expected: PASS.
- [ ] **Step 5: Commit** — `git add -A && git commit -m "feat: port ServiceStatus and StatusDecoder"`

---

### Task 10: UsageStore

**Files:**
- Create: `src/ClaudeUsageWidget.Core/Store/UsageStore.cs`
- Test: `tests/ClaudeUsageWidget.Core.Tests/UsageStoreTests.cs`
- Reference: `Sources/ClaudeUsageWidgetCore/Store/UsageStore.swift`, `Tests/ClaudeUsageWidgetCoreTests/UsageStoreTests.swift`

**Interfaces:**
- Consumes: `UsageSnapshot`, `UsageState`, `UsageError`, `UsageException` (Task 2).
- Produces (отличия от Swift — осознанные, зафиксированы спекой: без таймеров в Core — расписание держит App; без cachedSnapshot/tokenProvider — вместо токена `hasCredentials`, потому что транспорт всегда веб-сессия):

```csharp
public sealed record UsageRetryState(DateTimeOffset Until, int ConsecutiveRateLimits);

public sealed class UsageStore
{
    public const int RefreshIntervalSeconds = 300;
    public const int RetryMarginSeconds = 300;
    // Лестница пауз на повторные 429: 1 ч, 6 ч, 24 ч (+ margin).
    public UsageState CurrentState { get; }             // стартует Loading
    public UsageSnapshot? LastSnapshot { get; }
    public DateTimeOffset? RetryPausedUntil { get; }
    public event Action? Changed;                        // после каждой смены CurrentState

    public UsageStore(
        Func<CancellationToken, Task<UsageSnapshot>> fetch,
        Func<bool> hasCredentials,
        Func<DateTimeOffset> now,
        Func<UsageRetryState?> loadRetryState,
        Action<UsageRetryState?> saveRetryState);

    public Task LoadAsync();    // коалесцирует параллельные вызовы в один fetch
}
```

Поведение — построчный порт `performLoad`/`publish` из Swift, минус ветка cachedSnapshot:
пауза (`RetryPausedUntil` в будущем) → выход; `!hasCredentials()` → `Failed(NoCredentials)`;
успех → `Ok(snapshot, fetchedAt: snapshot.SourceUpdatedAt ?? now())`, сброс эскалации и
сохранённого состояния; `UsageException` с RateLimited → эскалация
`max(retryAfter, ladder[min(count-1, 2)]) + margin`, персист; прочие исключения →
`Failed(Network(ex.Message))`. Конструктор: если `loadRetryState()` вернул состояние с
`Until > now()` — восстановить паузу и `Failed(RateLimited(null))`, иначе `saveRetryState(null)`.

- [ ] **Step 1: Портировать ВСЕ тесты из `UsageStoreTests.swift`**, кроме `failsWithoutToken`/`passesToken` (заменяются на `hasCredentials == false` → NoCredentials без вызова fetch) и `cacheBypassesRateLimitPause` (ветка не портируется). Опорные точные значения:
  - 429 c Retry-After 600 → `RetryPausedUntil == now + 3600 + 300`; повторный `LoadAsync` до дедлайна не вызывает fetch.
  - Эскалация: второй подряд 429 → `now + 6*3600 + 300`; третий → `now + 24*3600 + 300`.
  - Успех сбрасывает лестницу на первую ступень.
  - Персист: новый стор с сохранённым состоянием (`Until` в будущем) не фетчит и стоит в `Failed(RateLimited(null))`.
  - Коалесценция: два параллельных `LoadAsync` → один вызов fetch (fetch с `Task.Delay(50)`).
  - Неуспех после успеха сохраняет `LastSnapshot`.

- [ ] **Step 2: Прогнать** — Expected: не компилируется.
- [ ] **Step 3: Реализовать.** Коалесценция: хранить `Task? _inFlight`; вход при непустом — `await` его и выход.
- [ ] **Step 4: Прогнать** — Expected: PASS.
- [ ] **Step 5: Commit** — `git add -A && git commit -m "feat: port UsageStore with rate-limit backoff"`

---

### Task 11: StatusStore и StatusApi

**Files:**
- Create: `src/ClaudeUsageWidget.Core/Store/StatusStore.cs`
- Create: `src/ClaudeUsageWidget.Core/Status/StatusApi.cs`
- Create: `src/ClaudeUsageWidget.Core/HttpValidation.cs`
- Test: `tests/ClaudeUsageWidget.Core.Tests/StatusStoreTests.cs`, `tests/ClaudeUsageWidget.Core.Tests/HttpValidationTests.cs`
- Reference: `Sources/ClaudeUsageWidgetCore/Store/StatusStore.swift`, `Status/StatusAPI.swift`, `Usage/UsageAPI.swift` (метод `validate`), `Tests/ClaudeUsageWidgetCoreTests/StatusStoreTests.swift`, `UsageAPITests.swift` (кейсы validate)

**Interfaces:**
- Consumes: `ServiceStatus`, `StatusDecoder` (Task 9), `UsageException`/`UsageError` (Task 2).
- Produces:

```csharp
public static class HttpValidation
{
    // 2xx — ок; 401/403 → Unauthorized; 429 → RateLimited(retryAfter);
    // иное → Network($"HTTP {code}"). Бросает UsageException.
    public static void Validate(int statusCode, int? retryAfterSeconds = null);
}

public sealed class StatusApi
{
    public static readonly Uri Endpoint; // https://status.claude.com/api/v2/summary.json
    public StatusApi(HttpClient? client = null);   // общий static HttpClient по умолчанию
    public Task<ServiceStatus> FetchAsync(CancellationToken ct = default);
}

public sealed class StatusStore
{
    public const int RefreshIntervalSeconds = 300;
    public ServiceStatus Status { get; }            // стартует Unknown, ошибки фетча его не сбрасывают
    public event Action? Changed;
    public StatusStore(Func<CancellationToken, Task<ServiceStatus>> fetch);
    public Task LoadAsync();                        // коалесценция как в UsageStore
}
```

- [ ] **Step 1: Тесты.** `HttpValidationTests`: 200/204 не бросают; 401 и 403 → Unauthorized; 429 c 600 → RateLimited(600); 500 → Network("HTTP 500"). `StatusStoreTests` — порт Swift-версии: успех публикует статус, ошибка фетча оставляет прежний, параллельные LoadAsync коалесцируются.
- [ ] **Step 2: Прогнать** — Expected: не компилируется.
- [ ] **Step 3: Реализовать.** StatusApi: GET c `Accept: application/json`, сетевые исключения → `UsageException(Network(ex.Message))`, затем `HttpValidation.Validate`, затем `StatusDecoder.Status`.
- [ ] **Step 4: Прогнать** — Expected: PASS.
- [ ] **Step 5: Commit** — `git add -A && git commit -m "feat: port StatusStore and status endpoint client"`

---

### Task 12: Настройки, AccountProfile, OrganizationPicker, DialGeometry, CoreInfo

**Files:**
- Create: `src/ClaudeUsageWidget.Core/Settings/WidgetSettings.cs`
- Create: `src/ClaudeUsageWidget.Core/Settings/SettingsStore.cs`
- Create: `src/ClaudeUsageWidget.Core/Web/OrganizationPicker.cs`
- Create: `src/ClaudeUsageWidget.Core/Views/DialGeometry.cs`
- Create: `src/ClaudeUsageWidget.Core/CoreInfo.cs`
- Test: `tests/ClaudeUsageWidget.Core.Tests/WidgetSettingsTests.cs`, `OrganizationPickerTests.cs`, `DialGeometryTests.cs`
- Reference: `Sources/ClaudeUsageWidgetCore/WidgetSettings.swift`, `Sources/ClaudeUsageWidget/ClaudeWebSession.swift:58-74` (выбор организации), `Sources/ClaudeUsageWidgetCore/Views/DialView.swift:5-10`, `CoreInfo.swift`, соответствующие Swift-тесты

**Interfaces:**
- Produces:

```csharp
public sealed record AccountProfile(string Id, string DisplayName, string ProfileFolder);

public sealed record WidgetSettingsData
{
    public bool WidgetVisible { get; init; } = true;
    public bool PositionLocked { get; init; }
    public string? ModelBucket { get; init; }          // null/пусто = пусть выбирает ModelBuckets
    public double WidgetSide { get; init; } = 170;
    public double? WidgetX { get; init; }
    public double? WidgetY { get; init; }
    public string TrayMetricKey { get; init; } = "five_hour";
    public bool TaskbarBandEnabled { get; init; }
    public string? OrganizationId { get; init; }
    public DateTimeOffset? RetryPausedUntil { get; init; }
    public int ConsecutiveRateLimits { get; init; }
    public IReadOnlyList<AccountProfile> Accounts { get; init; } = [];
}

public static class WidgetSettings
{
    public const double DefaultSide = 170;
    public const double MinSide = 150;
    public const double MaxSide = 340;
    public static double ClampSide(double side);
}

public sealed class SettingsStore
{
    public SettingsStore(string path);                 // App передаёт %APPDATA%\ClaudeUsageWidget\settings.json
    public WidgetSettingsData Load();                  // нет файла/битый JSON → дефолты, не исключение
    public void Save(WidgetSettingsData data);         // создаёт директорию, пишет с отступами
}

public static class OrganizationPicker
{
    // JSON-массив /api/organizations → uuid выбранной: capability "chat",
    // приоритет raven_type == "team", иначе первая; uuid ?? id; null если пусто/мусор.
    public static string? Pick(string organizationsJson);
}

public static class DialGeometry
{
    // Двенадцать часов = -90°, по часовой. Для WPF ArcSegment пригодится и точка на окружности.
    public static double AngleDegrees(double fraction);   // -90 + 360 * fraction
}

public static class CoreInfo
{
    public static string Version { get; }   // InformationalVersion сборки, "unknown" если пусто
}
```

- [ ] **Step 1: Тесты.**

```csharp
public class WidgetSettingsTests
{
    [Theory]
    [InlineData(100, 150)] [InlineData(170, 170)] [InlineData(500, 340)]
    public void ClampsSide(double raw, double expected) =>
        Assert.Equal(expected, WidgetSettings.ClampSide(raw));

    [Fact]
    public void MissingFileLoadsDefaults()
    {
        var store = new SettingsStore(Path.Combine(Path.GetTempPath(), Path.GetRandomFileName(), "s.json"));
        var data = store.Load();
        Assert.True(data.WidgetVisible);
        Assert.Equal(170, data.WidgetSide);
        Assert.Equal("five_hour", data.TrayMetricKey);
    }

    [Fact]
    public void RoundTripsAllFields()
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName(), "s.json");
        var store = new SettingsStore(path);
        var data = new WidgetSettingsData
        {
            WidgetVisible = false, PositionLocked = true, ModelBucket = "seven_day_fable",
            WidgetSide = 200, WidgetX = 10, WidgetY = 20, TaskbarBandEnabled = true,
            OrganizationId = "org-1", ConsecutiveRateLimits = 2,
            RetryPausedUntil = DateTimeOffset.FromUnixTimeSeconds(1_785_348_000),
            Accounts = [new AccountProfile("default", "Main", "profiles/default")],
        };
        store.Save(data);
        Assert.Equal(data, new SettingsStore(path).Load() with { Accounts = data.Accounts });
        Assert.Equal("default", new SettingsStore(path).Load().Accounts[0].Id);
    }

    [Fact]
    public void CorruptFileLoadsDefaults()
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName(), "s.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{not json");
        Assert.True(new SettingsStore(path).Load().WidgetVisible);
    }
}

public class OrganizationPickerTests
{
    [Fact]
    public void PrefersTeamOrganizationWithChat() =>
        Assert.Equal("team-uuid", OrganizationPicker.Pick("""
        [
          { "uuid": "personal-uuid", "capabilities": ["chat"], "raven_type": "personal" },
          { "uuid": "team-uuid", "capabilities": ["chat"], "raven_type": "team" },
          { "uuid": "no-chat", "capabilities": ["api"] }
        ]
        """));

    [Fact]
    public void FallsBackToFirstChatCapable() =>
        Assert.Equal("personal-uuid", OrganizationPicker.Pick("""
        [ { "uuid": "personal-uuid", "capabilities": ["chat"] } ]
        """));

    [Fact]
    public void AcceptsIdWhenUuidMissing() =>
        Assert.Equal("42", OrganizationPicker.Pick("""
        [ { "id": "42", "capabilities": ["chat"] } ]
        """));

    [Fact]
    public void ReturnsNullOnGarbageOrEmpty()
    {
        Assert.Null(OrganizationPicker.Pick("[]"));
        Assert.Null(OrganizationPicker.Pick("not json"));
    }
}

public class DialGeometryTests
{
    [Fact]
    public void ZeroFractionPointsAtTwelve() => Assert.Equal(-90, DialGeometry.AngleDegrees(0));
    [Fact]
    public void FullFractionWrapsAround() => Assert.Equal(270, DialGeometry.AngleDegrees(1));
}
```

Свериться также с `DialGeometryTests.swift` и `WidgetSettingsTests.swift`, перенести недостающее.

- [ ] **Step 2: Прогнать** — Expected: не компилируется.
- [ ] **Step 3: Реализовать.** SettingsStore: `JsonSerializer` с `WriteIndented`, load в try/catch → дефолты. CoreInfo: `Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()`, отрезать суффикс после `+`, пустое → `"unknown"`.
- [ ] **Step 4: Прогнать** — Expected: PASS.
- [ ] **Step 5: Commit** — `git add -A && git commit -m "feat: add settings store, organization picker, dial geometry"`

---

### Task 13: WPF-каркас приложения и трей-иконка

**Files:**
- Create: `src/ClaudeUsageWidget.App/ClaudeUsageWidget.App.csproj`
- Create: `src/ClaudeUsageWidget.App/App.xaml`, `App.xaml.cs`
- Create: `src/ClaudeUsageWidget.App/Tray/TrayIcon.cs`
- Create: `src/ClaudeUsageWidget.App/app.manifest` (PerMonitorV2 DPI)
- Modify: `ClaudeUsageWidget.sln` (добавить проект)

**Interfaces:**
- Consumes: `CoreInfo.Version` (Task 12).
- Produces: запускаемое приложение без главного окна с иконкой в трее; `TrayIcon` с API:

```csharp
public sealed class TrayIcon : IDisposable
{
    public TrayIcon();                                  // создаёт NotifyIcon с меню
    public void SetIcon(System.Drawing.Icon icon);
    public void SetTooltip(string text);                // ≤127 символов
    public event Action? RefreshRequested;              // пункт "Refresh now"
    public event Action? SignInRequested;               // "Sign in to Claude.ai…"
    public event Action? QuitRequested;                 // "Quit Claude Usage Widget"
    public System.Windows.Forms.ContextMenuStrip Menu { get; }  // для доп. пунктов из App
}
```

- [ ] **Step 1: Проект.**

```powershell
dotnet new wpf -o src/ClaudeUsageWidget.App -n ClaudeUsageWidget.App -f net8.0
dotnet sln add src/ClaudeUsageWidget.App
dotnet add src/ClaudeUsageWidget.App reference src/ClaudeUsageWidget.Core
```

В csproj: `<UseWindowsForms>true</UseWindowsForms>` (для NotifyIcon), `<ApplicationManifest>app.manifest</ApplicationManifest>`, `<Version>0.2.0</Version>`, `<AssemblyTitle>Claude Usage Widget</AssemblyTitle>`. Удалить сгенерированные `MainWindow.xaml/.cs`; в `App.xaml` убрать `StartupUri`, задать `ShutdownMode="OnExplicitShutdown"`.

- [ ] **Step 2: TrayIcon.** WinForms `NotifyIcon` + `ContextMenuStrip` с пунктами (сверху вниз, разделители как в macOS-меню, `ClaudeUsageWidgetApp.swift:33-58`): `Claude Usage Widget v{CoreInfo.Version} — GitHub` (открывает `https://github.com/Reva1v/claude-usage-widget` через `Process.Start` c `UseShellExecute`), `Report an Issue`, разделитель, `Refresh now`, `Sign in to Claude.ai…`, разделитель, `Quit Claude Usage Widget`. Стартовая иконка — нарисованное GDI+ кольцо 16×16 (эллипс пером 2 px, белый — трей тёмный), позже её заменит живая цифра.

- [ ] **Step 3: App.xaml.cs** — на `OnStartup` создать TrayIcon; Quit → `Shutdown()`. Остальные события пока пустые.

- [ ] **Step 4: Ручная проверка.** Run: `dotnet run --project src/ClaudeUsageWidget.App`. Ожидается: процесс без окон, кольцо в трее, меню открывается, Quit завершает процесс. Также `dotnet test` зелёный.

- [ ] **Step 5: Commit** — `git add -A && git commit -m "feat: add WPF app shell with tray icon and menu"`

---

### Task 14: Циферблаты и окно виджета на рабочем столе

**Files:**
- Create: `src/ClaudeUsageWidget.App/Views/DialControl.cs` (рисование одного циферблата)
- Create: `src/ClaudeUsageWidget.App/Views/StatusDialControl.cs`
- Create: `src/ClaudeUsageWidget.App/Views/WidgetRootView.xaml`, `.xaml.cs` (2×2 + строка статуса + BlockingNotice)
- Create: `src/ClaudeUsageWidget.App/Views/Theme.cs`
- Create: `src/ClaudeUsageWidget.App/Windows/DesktopWidgetWindow.cs`
- Modify: `src/ClaudeUsageWidget.App/App.xaml.cs`
- Reference: `Sources/ClaudeUsageWidgetCore/Views/DialView.swift`, `StatusDialView.swift`, `WidgetRootView.swift`, `Theme.swift`, `BlockingNotice.swift` — переносить визуал по ним; `Sources/ClaudeUsageWidget/ClaudeUsageWidgetApp.swift:196-213, 238-324` — поведение окна

**Interfaces:**
- Consumes: `DialModel.All`, `StatusLine.Text`, `Thresholds.Level`, `DialGeometry.AngleDegrees`, `ServiceStatusText.Label`, `UsageState`, `SettingsStore`, `WidgetSettings.ClampSide` (Tasks 4, 7, 9, 12).
- Produces:

```csharp
public sealed class DesktopWidgetWindow : Window
{
    public DesktopWidgetWindow(SettingsStore settings);
    public void Render(UsageState state, UsageSnapshot? last, ServiceStatus status,
                       string? preferredModelKey, DateTimeOffset? retryUntil);
    public event Action? HideRequested;      // кнопка-глаз в hover-хедере
    public bool PositionLocked { get; set; } // блокирует drag и resize
}
```

- [ ] **Step 1: Theme.cs** — порт палитры из `Theme.swift`: цвета Ok/Warning/Danger (зелёный/янтарный/красный), приглушённый (dim), фон панели (полупрозрачный тёмный со скруглением), шрифты — по коэффициентам из Swift (масштаб = сторона/170, дизайн-размер циферблата 68).

- [ ] **Step 2: DialControl** — `FrameworkElement.OnRender` c `DrawingContext`: подложка-кольцо, дуга заполнения (`StreamGeometry` + `ArcSegment`; старт в −90°, конец `DialGeometry.AngleDegrees(fraction)`, `IsLargeArc = fraction > 0.5`), в центре процент (`UsageMath.PercentText`) и под ним remaining; при `Fraction == null` — «n/a». Цвет дуги: `Thresholds.Level` → Theme; `dimmed` — серый. StatusDialControl: сплошное кольцо цветом статуса, в центре `ServiceStatusText.Label`; клик открывает `https://status.claude.com`.

- [ ] **Step 3: WidgetRootView** — сетка 2×2 (SESSION, WEEK, модельный, STATUS) + строка `StatusLine.Text` внизу + перекрывающая плашка BlockingNotice при `Failed(NoCredentials)` («Not signed in» + кнопка Sign in) и `Failed(Unauthorized)` (текст из `UsageError.Description`), по `BlockingNotice.swift`. Hover-хедер сверху: глаз (скрыть) и замок (лок позиции), появляется при наведении мыши на панель.

- [ ] **Step 4: DesktopWidgetWindow** — `WindowStyle=None`, `AllowsTransparency=true`, `ShowInTaskbar=false`, `ShowActivated=false`. Win32 (P/Invoke в этом же файле):
  - стиль `WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW` через `SetWindowLongPtr(GWL_EXSTYLE)` в `OnSourceInitialized`;
  - «всегда внизу»: hook `WndProc` (HwndSource.AddHook), на `WM_WINDOWPOSCHANGING` (0x0046) выставлять в `WINDOWPOS.hwndInsertAfter = HWND_BOTTOM (1)` и снимать флаг `SWP_NOZORDER`, чтобы любое всплытие тут же прижималось вниз;
  - drag: `MouseLeftButtonDown` в центре панели при `!PositionLocked` — ручное перетаскивание через захват мыши и сдвиг `Left/Top` (не `DragMove` — окно неактивируемое);
  - resize: полосы захвата ~8 px по краям; при перетаскивании менять сторону (одно число, `WidgetSettings.ClampSide`), верхний левый угол на месте;
  - персист: `Left/Top/Side` в `SettingsStore` (debounce ~500 мс после последнего изменения).

- [ ] **Step 5: Ручная проверка с фейковыми данными.** В `App.xaml.cs` временно скормить `Render` снапшот из строки JSON (payload из Task 5) и `ServiceStatus.Operational`. Run: `dotnet run --project src/ClaudeUsageWidget.App`. Чек-лист: панель на рабочем столе; при клике по ней окна поверх не перекрываются ею (Win+D возвращает — допустимо, панель живёт над обоями, под окнами); drag работает, при локе — нет; resize держит квадрат и клампится 150–340; процент/цвета соответствуют данным; после перезапуска позиция и размер сохранены.

- [ ] **Step 6: Commit** — `git add -A && git commit -m "feat: add dial rendering and bottom-pinned desktop widget window"`

---

### Task 15: Веб-сессия claude.ai (WebView2) и окно логина

**Files:**
- Create: `src/ClaudeUsageWidget.App/Web/ClaudeWebSession.cs`
- Create: `src/ClaudeUsageWidget.App/Web/LoginWindow.cs`
- Modify: `src/ClaudeUsageWidget.App/App.xaml.cs` (собрать всё: стора, таймер, окно, трей)
- Reference: `Sources/ClaudeUsageWidget/ClaudeWebSession.swift` — переносить поведение построчно

**Interfaces:**
- Consumes: `UsageDecoder.Snapshot`, `UsageException`, `UsageError`, `HttpValidation.Validate`, `OrganizationPicker.Pick`, `SettingsStore` (`OrganizationId`), `UsageStore`, `StatusStore`, `StatusApi` (Tasks 5, 10-12).
- Produces:

```csharp
public sealed class ClaudeWebSession
{
    // profileFolder: %LOCALAPPDATA%\ClaudeUsageWidget\profiles\default (из AccountProfile)
    public ClaudeWebSession(string profileFolder, SettingsStore settings);
    public Task<bool> HasSessionCookieAsync();          // sessionKey на домене claude.ai
    public Task<UsageSnapshot> FetchUsageAsync(CancellationToken ct);
    public void ClearCachedOrganization();
    public Task<LoginWindow> OpenLoginWindowAsync();    // повторный вызов поднимает существующее окно
    public event Action? SignedIn;                      // кука появилась, окно закрылось
}
```

- [ ] **Step 1: Подключить WebView2.**

```powershell
dotnet add src/ClaudeUsageWidget.App package Microsoft.Web.WebView2
```

- [ ] **Step 2: ClaudeWebSession.** Один разделяемый `CoreWebView2Environment` c `userDataFolder = profileFolder`. Скрытый `CoreWebView2` (контроллер с невидимым родителем, `IsVisible = false`).
  - `HasSessionCookieAsync`: `CookieManager.GetCookiesAsync("https://claude.ai")` → есть `sessionKey` с непустым значением.
  - Фетч страницей (порт `WebPageJSONFetcher`): `Navigate(url)`; на `NavigationCompleted` — если `HttpStatusCode` 401/403 → `UsageException(Unauthorized)`, 429 → `RateLimited(null)`, не-2xx → `Network($"Claude.ai returned HTTP {code}.")`; успех → `ExecuteScriptAsync("document.body.innerText || document.body.textContent || ''")`, распарсить JSON-строку результата (`JsonSerializer.Deserialize<string>`), таймаут 30 с через CTS. User-Agent не трогать — родной Edge-фингерпринт и есть цель (комментарий в `ClaudeWebSession.swift:6-10`).
  - `FetchUsageAsync`: нет куки → `UsageException(NoCredentials)`; организация: из настроек, иначе навигация на `https://claude.ai/api/organizations` → `OrganizationPicker.Pick` → сохранить (null → `MalformedResponse`); затем `https://claude.ai/api/organizations/{id}/usage` → `UsageDecoder.Snapshot`. На `Unauthorized` — сбросить сохранённую организацию и пробросить (порт `fetchUsage`).
- [ ] **Step 3: LoginWindow** — обычное окно 1000×720 с WebView2 на том же профиле, титул «Sign in to Claude.ai». Перед загрузкой очистить browsing data профиля (`CoreWebView2Profile.ClearBrowsingDataAsync()` — порт сброса Turnstile-состояния), затем `https://claude.ai/login`. Подписка на `CookieManager`: опрос куки после каждого `NavigationCompleted` и раз в 2 с таймером; появился `sessionKey` → закрыть окно, поднять `SignedIn`.
- [ ] **Step 4: Собрать App.** В `App.xaml.cs`: `SettingsStore` → `ClaudeWebSession` → `UsageStore(fetch: session.FetchUsageAsync, hasCredentials: () => session.HasSessionCookieAsync().GetAwaiter().GetResult(), ...)` (загрузка/сохранение retry-state — через поля `RetryPausedUntil`/`ConsecutiveRateLimits` настроек), `StatusStore(new StatusApi().FetchAsync)`. `DispatcherTimer` каждые 300 с → `LoadAsync` обоих сторов; `SystemEvents.PowerModeChanged == Resume` → немедленный refresh (порт didWakeNotification); подписать `Changed` обоих сторов на `DesktopWidgetWindow.Render` и тултип трея; на старте, если куки нет — открыть LoginWindow сразу (порт `applicationDidFinishLaunching`); `SignedIn` → `ClearCachedOrganization` + refresh. Убрать фейковые данные из Task 14.
- [ ] **Step 5: Ручная сквозная проверка.** Run: `dotnet run --project src/ClaudeUsageWidget.App`. Чек-лист: при первом запуске открывается логин; после входа окно закрывается само, циферблаты заполняются реальными процентами в течение секунд; `Refresh now` обновляет мгновенно; после перезапуска приложения логин НЕ требуется (кука в профиле), данные приходят сами; статусный циферблат показывает OK.
- [ ] **Step 6: Commit** — `git add -A && git commit -m "feat: read usage through an authenticated claude.ai WebView2 session"`

---

### Task 16: Живая цифра в трее, полный набор пунктов меню, автозапуск

**Files:**
- Create: `src/ClaudeUsageWidget.App/Tray/TrayIconRenderer.cs`
- Create: `src/ClaudeUsageWidget.App/Autostart.cs`
- Modify: `src/ClaudeUsageWidget.App/Tray/TrayIcon.cs`, `App.xaml.cs`
- Reference: `Sources/ClaudeUsageWidget/ClaudeUsageWidgetApp.swift:61-188` (MenuBarLabel, ModelBucketPicker, LaunchAtLoginToggle)

**Interfaces:**
- Consumes: `TrayText.Metrics`, `DialModel.All`, `ModelBuckets.Available/Label`, `SettingsStore` (Tasks 6-8, 12).
- Produces:

```csharp
public static class TrayIconRenderer
{
    // 16×16 (реально — SystemInformation.SmallIconSize): значение выбранной метрики
    // ("42%" без знака % — только цифры, иначе нечитаемо) белым моноширинным по центру;
    // нет данных — кольцо-заглушка.
    public static System.Drawing.Icon Render(string? valueText);
}

public static class Autostart
{
    // HKCU\Software\Microsoft\Windows\CurrentVersion\Run, имя "ClaudeUsageWidget",
    // значение — путь текущего exe в кавычках (Environment.ProcessPath).
    public static bool IsEnabled();
    public static void SetEnabled(bool enabled);
}
```

- [ ] **Step 1: Renderer.** GDI+: `Bitmap` размера маленькой иконки, текст цифры (без «%») шрифтом Segoe UI полужирным, автоподбор кегля под ширину, белый на прозрачном; `Icon.FromHandle`. Уничтожать предыдущий HIcon (`DestroyIcon`) при замене — иначе утечка GDI-хэндлов.
- [ ] **Step 2: Меню.** Добавить в TrayIcon пункты между существующими: подменю «Tray shows» (SESSION/WEEK/модель — пишет `TrayMetricKey`), подменю «Model limit» (из `ModelBuckets.Available`, видно при >1, пишет `ModelBucket`), чекбоксы «Show on desktop» (`WidgetVisible` + показать/скрыть окно), «Taskbar band» (пока disabled — включит Task 17), «Lock position», «Launch at login» (через `Autostart`).
- [ ] **Step 3: Связка.** На каждый `Changed` UsageStore: пересчитать `DialModel.All` → значение метрики `TrayMetricKey` → `TrayIconRenderer.Render` → `SetIcon`; тултип — все метрики из `TrayText.Metrics` строкой вида `SES 42% · WEEK 18% · FAB 8%` + строка статуса.
- [ ] **Step 4: Ручная проверка.** Чек-лист: цифра в трее совпадает с циферблатом; смена «Tray shows» меняет цифру; «Launch at login» создаёт/удаляет значение в реестре (проверить `reg query HKCU\Software\Microsoft\Windows\CurrentVersion\Run /v ClaudeUsageWidget`); скрытие/показ виджета работает и переживает перезапуск; лок блокирует перетаскивание.
- [ ] **Step 5: Commit** — `git add -A && git commit -m "feat: live tray figure, full tray menu, launch at login"`

---

### Task 17: Полоска в панели задач

**Files:**
- Create: `src/ClaudeUsageWidget.App/Windows/TaskbarBandWindow.cs`
- Modify: `src/ClaudeUsageWidget.App/App.xaml.cs`, `Tray/TrayIcon.cs` (включить пункт меню)

**Interfaces:**
- Consumes: `TrayText.Metrics` (Task 8), `SettingsStore.TaskbarBandEnabled` (Task 12).
- Produces:

```csharp
public sealed class TaskbarBandWindow : Window
{
    public TaskbarBandWindow();
    public bool TryAttach();      // SetParent к таскбару; false → вызвавший включает overlay-режим
    public void Detach();
    public void Render(IReadOnlyList<TrayMetric> metrics);   // колонки label-над-значением
}
```

- [ ] **Step 1: Окно.** Borderless, прозрачное, высота = высоте таскбара, ширина по контенту; рендер колонок как в маковском меню-баре (label 10 px сверху, значение 14 px снизу, моноширинно, белым).
- [ ] **Step 2: Встраивание.** `FindWindow("Shell_TrayWnd", null)`; позиция — слева от области переполнения: `FindWindowEx(tray, ..., "TrayNotifyWnd", ...)`, встать левее её на ширину окна с отступом 8 px. `SetParent(band, tray)`; на `WM_DPICHANGED`/таймер раз в 5 с — перепозиционироваться (таскбар перестраивается). Если `SetParent` вернул 0 — `TryAttach() == false`, окно остаётся top-level: `Topmost = true`, координаты поверх того же места таскбара (overlay-fallback из спеки).
- [ ] **Step 3: Связка.** Чекбокс «Taskbar band» в трее: включает/выключает окно, пишет `TaskbarBandEnabled`; на `Changed` UsageStore — `Render` свежими метриками.
- [ ] **Step 4: Ручная проверка.** Чек-лист: полоска видна в таскбаре в пустой области слева от трея, цифры живые; переключение чекбокса прячет/показывает; после перезапуска состояние восстановлено; при недоступном встраивании полоска всё равно видна поверх таскбара.
- [ ] **Step 5: Commit** — `git add -A && git commit -m "feat: optional TrafficMonitor-style taskbar band"`

---

### Task 18: CI, README, удаление macOS-кода

**Files:**
- Create: `.github/workflows/ci.yml` (заменяет старый)
- Modify: `README.md` (переписать под Windows), `.gitignore` (добавить `bin/`, `obj/`, `*.user`; убрать маковские записи)
- Delete: `Sources/`, `Tests/`, `Package.swift`, `Package.resolved`, `Makefile`, `appcast.xml`, `Resources/`, `Scripts/`, `.github/workflows/release.yml`, `.github/ISSUE_TEMPLATE/` (шаблоны ссылаются на маковские поля — удалить)

**Interfaces:**
- Consumes: всё готовое приложение (Tasks 1-17).

- [ ] **Step 1: CI.**

```yaml
name: CI
on:
  push:
    branches: [main]
  pull_request:
    branches: [main]
jobs:
  build-and-test:
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '8.0.x'
      - run: dotnet build -c Release
      - run: dotnet test -c Release --no-build
```

- [ ] **Step 2: README** — переписать: что это (порт для Windows, форк TadelUnso/claude-usage-widget), скриншота пока нет (`assets/widget.png` удалить вместе с маковским; отметить TODO), требования (Windows 11, .NET 8 SDK для сборки, WebView2 Runtime — на Win11 предустановлен), запуск `dotnet run --project src/ClaudeUsageWidget.App`, публикация `dotnet publish src/ClaudeUsageWidget.App -c Release -r win-x64 --self-contained -p:PublishSingleFile=true`, как читаются данные (веб-сессия, дословно смысл из старого README), известные ограничения (нужна подписка; API недокументирован).
- [ ] **Step 3: Удалить macOS-артефакты** списком из Files/Delete. Прогнать `dotnet test` и `dotnet build -c Release` — зелёные.
- [ ] **Step 4: Финальный smoke.** Run: `dotnet run --project src/ClaudeUsageWidget.App` — виджет работает, данные приходят.
- [ ] **Step 5: Commit** — `git add -A && git commit -m "feat!: replace macOS app with the Windows port"`

---

## Self-Review (выполнено при написании)

- Покрытие спеки: структура (T1, T13), веб-сессия и организация (T15, T12), режимы отображения (T14, T16, T17), настройки и автозапуск (T12, T16), задел на аккаунты (`AccountProfile` в T12, `profileFolder` в T15), тесты (T2-T12), CI/README/удаление Swift (T18). Автообновления, Auth/Keychain, StatuslineUsageCache — сознательно вне объёма (спека).
- Отличия от Swift-оригинала зафиксированы в задачах: UsageStore без таймеров и без cachedSnapshot/tokenProvider (T10), таймеры в App (T15), retry-state в settings.json вместо UserDefaults (T12, T15).
- Согласованность типов проверена: `UsageState`/`UsageError`/`UsageSnapshot` (T2) используются в T5, T7, T10, T14, T15 с одинаковыми сигнатурами; `TrayMetric` (T8) в T16, T17; `SettingsStore` (T12) в T14, T15, T16, T17.
