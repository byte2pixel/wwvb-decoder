using System.IO;
using System.Text.Json.Nodes;
using WwvbDecoder.Host.Bridge;
using WwvbDecoder.Host.Services;

namespace WwvbDecoder.Host.Tests.Services;

/// <summary>
/// The settings file of "What the host remembers" in docs/design/webview2-messages.md.
/// </summary>
public sealed class SettingsStoreTests : IDisposable
{
    // The example in the document, as it is written there.
    private const string Example = """
        {
          "source": "replay",
          "port": "COM5",
          "capture": "2026-11-02T0310Z-home-west-window.edges.txt",
          "speed": 10,
          "noise": 0.1,
          "reducedCarrierLevel": "high",
          "running": true
        }
        """;

    private static readonly HostSettings ExampleSettings = new(
        Source: SourceKind.Replay,
        Port: "COM5",
        Capture: "2026-11-02T0310Z-home-west-window.edges.txt",
        Speed: 10,
        Noise: 0.1,
        ReducedCarrierLevel: PinLevel.High,
        Running: true
    );

    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "wwvb-settings-tests-" + Guid.NewGuid().ToString("N")
    );

    private string FilePath => Path.Combine(_directory, "settings.json");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    /// <summary>A store on a file that holds <paramref name="json"/>.</summary>
    private ISettingsStore StoreOn(string json)
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(FilePath, json);
        return new SettingsStore(FilePath);
    }

    [Fact]
    public void Default_IsWhatTheDocumentSays()
    {
        // Assert: synthetic, null, null, 1, 0, high, and false
        HostSettings.Default.ShouldBe(
            new HostSettings(SourceKind.Synthetic, null, null, 1, 0, PinLevel.High, false)
        );
    }

    [Fact]
    public void Load_NoFile_GivesTheDefaults()
    {
        // Arrange: neither the file nor its folder exists
        ISettingsStore store = new SettingsStore(FilePath);

        // Act
        HostSettings settings = store.Load();

        // Assert
        settings.ShouldBe(HostSettings.Default);
        Directory.Exists(_directory).ShouldBeFalse("reading must not create anything");
    }

    [Fact]
    public void Load_TheDocumentsExample_GivesItsValues()
    {
        // Act
        HostSettings settings = StoreOn(Example).Load();

        // Assert
        settings.ShouldBe(ExampleSettings);
    }

    [Fact]
    public void Load_NullPortAndCapture_AreNull()
    {
        // Arrange
        string json = Example
            .Replace("\"port\": \"COM5\"", "\"port\": null")
            .Replace(
                "\"capture\": \"2026-11-02T0310Z-home-west-window.edges.txt\"",
                "\"capture\": null"
            );

        // Act
        HostSettings settings = StoreOn(json).Load();

        // Assert
        settings.ShouldBe(ExampleSettings with { Port = null, Capture = null });
    }

    [Theory]
    [InlineData("\"source\": \"replay\"", "\"source\": \"serial\"")]
    [InlineData("\"source\": \"replay\"", "\"source\": \"synthetic\"")]
    [InlineData("\"speed\": 10", "\"speed\": 0")] // as fast as possible
    [InlineData("\"speed\": 10", "\"speed\": 60")]
    [InlineData("\"noise\": 0.1", "\"noise\": 0")]
    [InlineData("\"noise\": 0.1", "\"noise\": 1")]
    [InlineData("\"reducedCarrierLevel\": \"high\"", "\"reducedCarrierLevel\": \"low\"")]
    [InlineData("\"running\": true", "\"running\": false")]
    public void Load_EveryValidValue_IsKept(string property, string replacement)
    {
        // Arrange
        string json = Example.Replace(property, replacement);
        json.ShouldNotBe(Example);

        // Act
        HostSettings settings = StoreOn(json).Load();

        // Assert: it is not the defaults, and writing it back gives the same values
        settings.ShouldNotBe(HostSettings.Default);
        var other = new SettingsStore(Path.Combine(_directory, "copy.json"));
        other.Save(settings);
        JsonNode
            .DeepEquals(
                JsonNode.Parse(File.ReadAllText(Path.Combine(_directory, "copy.json"))),
                JsonNode.Parse(json)
            )
            .ShouldBeTrue(json);
    }

    [Fact]
    public void Load_UnknownProperty_IsIgnored()
    {
        // Arrange: a property a later version might add
        string json = Example.Replace(
            "\"running\": true",
            "\"running\": true, \"theme\": \"dark\""
        );

        // Act
        HostSettings settings = StoreOn(json).Load();

        // Assert
        settings.ShouldBe(ExampleSettings);
    }

    [Theory]
    [InlineData("\"source\": \"replay\"", "\"source\": \"none\"")] // not one of the three
    [InlineData("\"source\": \"replay\"", "\"source\": \"telepathy\"")]
    [InlineData("\"source\": \"replay\"", "\"source\": 1")] // an enum is a string, never a number
    [InlineData("\"source\": \"replay\"", "\"source\": null")]
    [InlineData("\"port\": \"COM5\"", "\"port\": 5")]
    [InlineData("\"capture\": \"2026", "\"capture\": true, \"was\": \"2026")]
    [InlineData("\"speed\": 10", "\"speed\": -1")]
    [InlineData("\"speed\": 10", "\"speed\": -0.001")]
    [InlineData("\"speed\": 10", "\"speed\": 1e999")] // reads as infinity
    [InlineData("\"speed\": 10", "\"speed\": \"fast\"")]
    [InlineData("\"speed\": 10", "\"speed\": null")]
    [InlineData("\"noise\": 0.1", "\"noise\": 1.5")]
    [InlineData("\"noise\": 0.1", "\"noise\": -0.1")]
    [InlineData("\"noise\": 0.1", "\"noise\": null")]
    [InlineData("\"reducedCarrierLevel\": \"high\"", "\"reducedCarrierLevel\": \"sideways\"")]
    [InlineData("\"reducedCarrierLevel\": \"high\"", "\"reducedCarrierLevel\": 0")]
    [InlineData("\"running\": true", "\"running\": \"yes\"")]
    [InlineData("\"running\": true", "\"running\": null")]
    public void Load_OneValueNotValid_GivesAllTheDefaults(string property, string replacement)
    {
        // Arrange
        string json = Example.Replace(property, replacement);
        json.ShouldNotBe(Example);

        // Act
        HostSettings settings = StoreOn(json).Load();

        // Assert
        settings.ShouldBe(HostSettings.Default);
    }

    [Theory]
    [InlineData("source")]
    [InlineData("port")]
    [InlineData("capture")]
    [InlineData("speed")]
    [InlineData("noise")]
    [InlineData("reducedCarrierLevel")]
    [InlineData("running")]
    public void Load_OnePropertyMissing_GivesAllTheDefaults(string property)
    {
        // Arrange
        JsonObject json = JsonNode.Parse(Example)!.AsObject();
        json.Remove(property).ShouldBeTrue();

        // Act
        HostSettings settings = StoreOn(json.ToJsonString()).Load();

        // Assert
        settings.ShouldBe(HostSettings.Default);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("\"replay\"")]
    [InlineData("not json at all")]
    public void Load_NotASettingsObject_GivesTheDefaults(string content)
    {
        // Act
        HostSettings settings = StoreOn(content).Load();

        // Assert
        settings.ShouldBe(HostSettings.Default);
    }

    [Fact]
    public void Load_ExampleCutShort_GivesTheDefaults()
    {
        // Arrange
        string half = Example[..(Example.Length / 2)];

        // Act
        HostSettings settings = StoreOn(half).Load();

        // Assert
        settings.ShouldBe(HostSettings.Default);
    }

    [Fact]
    public void Load_FileHeldByAnotherProcess_GivesTheDefaults()
    {
        // Arrange
        ISettingsStore store = StoreOn(Example);
        using FileStream held = new(FilePath, FileMode.Open, FileAccess.Read, FileShare.None);

        // Act
        HostSettings settings = store.Load();

        // Assert
        settings.ShouldBe(HostSettings.Default);
    }

    [Fact]
    public void Load_PathIsAFolder_GivesTheDefaults()
    {
        // Arrange
        Directory.CreateDirectory(FilePath);
        ISettingsStore store = new SettingsStore(FilePath);

        // Act
        HostSettings settings = store.Load();

        // Assert
        settings.ShouldBe(HostSettings.Default);
    }

    [Fact]
    public void Save_WritesTheDocumentsExample()
    {
        // Arrange
        ISettingsStore store = new SettingsStore(FilePath);

        // Act
        store.Save(ExampleSettings);

        // Assert: the same names and values, and nothing more
        string written = File.ReadAllText(FilePath);
        JsonNode.DeepEquals(JsonNode.Parse(written), JsonNode.Parse(Example)).ShouldBeTrue(written);
    }

    [Fact]
    public void Save_NothingRemembered_WritesNullsAndTheOtherNames()
    {
        // Arrange
        ISettingsStore store = new SettingsStore(FilePath);
        var settings = new HostSettings(SourceKind.Serial, null, null, 1, 0, PinLevel.Low, false);

        // Act
        store.Save(settings);

        // Assert
        JsonObject written = JsonNode.Parse(File.ReadAllText(FilePath))!.AsObject();
        written["source"]!.GetValue<string>().ShouldBe("serial");
        written.ContainsKey("port").ShouldBeTrue();
        written["port"].ShouldBeNull();
        written.ContainsKey("capture").ShouldBeTrue();
        written["capture"].ShouldBeNull();
        written["reducedCarrierLevel"]!.GetValue<string>().ShouldBe("low");
        written["running"]!.GetValue<bool>().ShouldBeFalse();
    }

    [Fact]
    public void Save_ThenLoad_GivesTheSameSettings()
    {
        // Arrange
        var settings = new HostSettings(SourceKind.Serial, "COM7", null, 60, 1, PinLevel.Low, true);

        // Act: another store on the same file, as the next start of the host is
        new SettingsStore(FilePath).Save(settings);
        HostSettings loaded = new SettingsStore(FilePath).Load();

        // Assert
        loaded.ShouldBe(settings);
    }

    [Fact]
    public void Save_FolderDoesNotExist_CreatesIt()
    {
        // Arrange: a fresh PC has no WwvbDecoder folder yet
        string path = Path.Combine(_directory, "not", "there", "settings.json");
        ISettingsStore store = new SettingsStore(path);

        // Act
        store.Save(ExampleSettings);

        // Assert
        new SettingsStore(path)
            .Load()
            .ShouldBe(ExampleSettings);
    }

    [Fact]
    public void Save_Twice_KeepsTheLastAndLeavesNoOtherFile()
    {
        // Arrange
        ISettingsStore store = new SettingsStore(FilePath);

        // Act
        store.Save(ExampleSettings);
        store.Save(ExampleSettings with { Speed = 60, Running = false });

        // Assert
        store.Load().ShouldBe(ExampleSettings with { Speed = 60, Running = false });
        Directory.GetFileSystemEntries(_directory).ShouldBe([FilePath]);
    }

    [Fact]
    public void Save_PathIsAFolder_DoesNotThrow()
    {
        // Arrange
        Directory.CreateDirectory(FilePath);
        ISettingsStore store = new SettingsStore(FilePath);

        // Act
        Should.NotThrow(() => store.Save(ExampleSettings));

        // Assert: the command that saved carries on, and the next read is the defaults
        Directory.Exists(FilePath).ShouldBeTrue();
        store.Load().ShouldBe(HostSettings.Default);
    }

    [Fact]
    public void Save_FolderCannotBeCreated_DoesNotThrow()
    {
        // Arrange: a file stands where the folder would go
        Directory.CreateDirectory(_directory);
        string inTheWay = Path.Combine(_directory, "in-the-way");
        File.WriteAllText(inTheWay, "x");
        ISettingsStore store = new SettingsStore(Path.Combine(inTheWay, "settings.json"));

        // Act
        Should.NotThrow(() => store.Save(ExampleSettings));

        // Assert
        File.ReadAllText(inTheWay).ShouldBe("x");
        store.Load().ShouldBe(HostSettings.Default);
    }

    [Fact]
    public void Save_FileHeldByAnotherProcess_DoesNotThrowAndKeepsTheOldFile()
    {
        // Arrange
        ISettingsStore store = StoreOn(Example);
        using (FileStream _ = new(FilePath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            // Act
            Should.NotThrow(() => store.Save(ExampleSettings with { Speed = 60 }));
        }

        // Assert: nothing was half written
        store.Load().ShouldBe(ExampleSettings);
    }

    [Fact]
    public void FindPath_SettingsArgument_NamesTheFile()
    {
        // Act: the option is found among others, and its file need not exist yet
        string found = SettingsStore.FindPath(["--captures", "somewhere", "--settings", FilePath]);

        // Assert
        found.ShouldBe(FilePath);
    }

    [Fact]
    public void FindPath_RelativeSettingsArgument_IsMadeAbsolute()
    {
        // Act
        string found = SettingsStore.FindPath(["--settings", "my-settings.json"]);

        // Assert
        found.ShouldBe(Path.Combine(Directory.GetCurrentDirectory(), "my-settings.json"));
    }

    [Theory]
    [InlineData]
    [InlineData("--verbose")]
    [InlineData("--captures", "somewhere")]
    [InlineData("--settings")] // the option with no file after it
    public void FindPath_NoSettingsArgument_IsTheFileUnderLocalAppData(params string[] args)
    {
        // Act
        string found = SettingsStore.FindPath(args);

        // Assert: beside the WebView2 user data
        found.ShouldBe(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WwvbDecoder",
                "settings.json"
            )
        );
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{")]
    [InlineData("not json at all")]
    [InlineData("{ \"source\": \"none\" }")]
    public void Load_FileThatCannotBeUsed_IsLeftAsItIs(string content)
    {
        // Arrange
        ISettingsStore store = StoreOn(content);

        // Act
        store.Load();

        // Assert: reading changes nothing on disk
        File.ReadAllText(FilePath).ShouldBe(content);
        Directory.GetFileSystemEntries(_directory).ShouldBe([FilePath]);
    }

    [Fact]
    public void Save_PathWithNoFolderInIt_WritesIntoTheCurrentFolder()
    {
        // Arrange: a bare file name, for which Path.GetDirectoryName gives ""
        string name = "wwvb-settings-" + Guid.NewGuid().ToString("N") + ".json";
        ISettingsStore store = new SettingsStore(name);

        try
        {
            // Act
            store.Save(ExampleSettings);

            // Assert
            File.Exists(name).ShouldBeTrue();
            store.Load().ShouldBe(ExampleSettings);
        }
        finally
        {
            File.Delete(name);
            File.Delete(name + ".tmp");
        }
    }
}
