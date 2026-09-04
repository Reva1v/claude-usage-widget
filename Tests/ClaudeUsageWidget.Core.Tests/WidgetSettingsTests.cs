namespace ClaudeUsageWidget.Core.Tests;

public class WidgetSettingsTests
{
    [Theory]
    [InlineData(100, 150)]
    [InlineData(170, 170)]
    [InlineData(500, 340)]
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
            TrayAccountId = "a1",
            Accounts =
            [
                new AccountProfile("a1", "Work", "org-1", DateTimeOffset.FromUnixTimeSeconds(1_785_348_000), 2),
                new AccountProfile("a2", "Personal", "org-2", null, 0),
            ],
        };
        store.Save(data);

        var loaded = new SettingsStore(path).Load();
        Assert.Equal(data, loaded with { Accounts = data.Accounts });
        Assert.Equal(["a1", "a2"], loaded.Accounts.Select(a => a.Id));
        Assert.Equal("org-2", loaded.Accounts[1].OrganizationId);
    }

    [Fact]
    public void ALayoutWithTheStatusCellSurvivesTheFile()
    {
        // The tray toggle's whole path is write -> read -> Sanitize, and the
        // item is the newest member of an enum written by NAME. A silent loss
        // here would look like the toggle not working at all.
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName(), "s.json");
        var layout = WidgetLayout.Default with { Block = BlockLayout.Default.Toggle(BlockItem.Status) };
        new SettingsStore(path).Save(new WidgetSettingsData { Layout = layout });

        var loaded = WidgetLayout.Sanitize(new SettingsStore(path).Load().Layout, StatusMode.Cell, ModelDial.Shown);

        Assert.Contains(BlockItem.Status, loaded.Block.Order);
        Assert.Equal(layout.Block.Order, loaded.Block.Order);
    }

    [Fact]
    public void TheStatusModeSurvivesTheFile()
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName(), "s.json");
        new SettingsStore(path).Save(new WidgetSettingsData { StatusMode = StatusMode.Line });

        Assert.Equal(StatusMode.Line, new SettingsStore(path).Load().StatusMode);
    }

    [Fact]
    public void AFileWrittenBeforeTheSettingKeepsTheStatusCellItHad()
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName(), "s.json");
        var layout = WidgetLayout.Default with { Block = BlockLayout.Default.Toggle(BlockItem.Status) };
        new SettingsStore(path).Save(new WidgetSettingsData { Layout = layout });

        var loaded = new SettingsStore(path).Load();

        Assert.Null(loaded.StatusMode);
        Assert.Equal(StatusMode.Cell, StatusModes.Resolve(loaded.StatusMode, loaded.Layout));
    }

    [Fact]
    public void TheModelDialSurvivesTheFile()
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName(), "s.json");
        new SettingsStore(path).Save(new WidgetSettingsData { ModelDial = ModelDial.Hidden });

        Assert.Equal(ModelDial.Hidden, new SettingsStore(path).Load().ModelDial);
    }

    [Fact]
    public void AFileWrittenBeforeTheSettingKeepsItsModelDial()
    {
        // Null is a file from before the setting, and it must read as Shown:
        // defaulting to Hidden would silently take the dial off every panel
        // that upgrades.
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName(), "s.json");
        new SettingsStore(path).Save(new WidgetSettingsData());

        var loaded = new SettingsStore(path).Load();

        Assert.Null(loaded.ModelDial);
        Assert.Equal(ModelDial.Shown, ModelDials.Resolve(loaded.ModelDial));
    }

    [Fact]
    public void ThePlanLineSurvivesTheFile()
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName(), "s.json");
        new SettingsStore(path).Save(new WidgetSettingsData { PlanLine = PlanLine.Shown });

        Assert.Equal(PlanLine.Shown, new SettingsStore(path).Load().PlanLine);
    }

    [Fact]
    public void AFileWrittenBeforeThePlanLineReadsAsHidden()
    {
        // Unlike the model dial, this one defaults OFF: the line is new, nobody
        // asked for it before today, and a panel that grows a row on upgrade is
        // a surprise. The user turns it on from the toolbar.
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName(), "s.json");
        new SettingsStore(path).Save(new WidgetSettingsData());

        var loaded = new SettingsStore(path).Load();

        Assert.Null(loaded.PlanLine);
        Assert.Equal(PlanLine.Hidden, PlanLines.Resolve(loaded.PlanLine));
    }

    [Fact]
    public void TheSubscriptionFieldsSurviveTheFile()
    {
        // Stored RAW so a mapping fix reaches an existing install without asking
        // claude.ai for the body again.
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName(), "s.json");
        new SettingsStore(path).Save(new WidgetSettingsData
        {
            Accounts =
            [
                new AccountProfile("a1", "work", "org-1", null, 0,
                    Capabilities: ["raven", "chat"], RateLimitTier: "default_raven", RavenType: "team"),
            ],
        });

        var account = Assert.Single(new SettingsStore(path).Load().Accounts);

        // The list is compared by CONTENT: records compare IReadOnlyList<string>
        // by reference, so an Assert.Equal on the whole profile would fail here
        // even when every field round-tripped.
        Assert.Equal(["raven", "chat"], account.Capabilities);
        Assert.Equal("default_raven", account.RateLimitTier);
        Assert.Equal("team", account.RavenType);
    }

    [Fact]
    public void AnAccountWrittenBeforeTheSubscriptionFieldsLoadsThemAsNull()
    {
        // Null capabilities is the sentinel ClaudeWebSession backfills on: an
        // account picked before this release must be recognisable as one that
        // has never been asked.
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName(), "s.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, """
        {"WidgetVisible": false,
         "Accounts": [{"Id":"a1","DisplayName":"work","OrganizationId":"org-1","ConsecutiveRateLimits":0}]}
        """);

        var loaded = new SettingsStore(path).Load();

        Assert.Null(Assert.Single(loaded.Accounts).Capabilities);
        // Proof the file parsed rather than falling through to the defaults.
        Assert.False(loaded.WidgetVisible);
    }

    [Fact]
    public void ExportDirectorySurvivesTheFile()
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName(), "s.json");
        new SettingsStore(path).Save(new WidgetSettingsData { ExportDirectory = @"C:\dir" });

        Assert.Equal(@"C:\dir", new SettingsStore(path).Load().ExportDirectory);
    }

    [Fact]
    public void ASettingsFileFromBeforeTheExportDirectoryLoadsItAsNull()
    {
        // Null is "export off", and every settings.json written before this
        // release lacks the key. Reading it as anything else would start
        // writing files nobody asked for.
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName(), "s.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, """{"WidgetVisible": false, "TrayMetricKey": "seven_day"}""");

        var loaded = new SettingsStore(path).Load();

        Assert.Null(loaded.ExportDirectory);
        // Proof the file actually parsed instead of falling through to the
        // defaults, which would make the assertion above meaningless.
        Assert.False(loaded.WidgetVisible);
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
