using WwvbDecoder.Host.Services;

namespace WwvbDecoder.Host.Tests.Services;

/// <summary>Settings in memory. Records every save, and counts the loads.</summary>
internal sealed class FakeSettingsStore : ISettingsStore
{
    /// <summary>What <see cref="Load"/> gives: the file as the host finds it when it starts.</summary>
    public HostSettings Remembered { get; set; } = HostSettings.Default;

    /// <summary>Every save, in order.</summary>
    public List<HostSettings> Saved { get; } = [];

    public int Loads { get; private set; }

    public HostSettings Load()
    {
        Loads++;
        return Remembered;
    }

    public void Save(HostSettings settings) => Saved.Add(settings);
}
