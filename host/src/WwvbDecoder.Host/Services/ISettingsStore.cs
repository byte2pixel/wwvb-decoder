namespace WwvbDecoder.Host.Services;

/// <summary>
/// Store and retrieve the host settings.
/// </summary>
public interface ISettingsStore
{
    /// <summary>
    /// Loads the settings from the store.
    /// </summary>
    /// <returns>The settings.</returns>
    HostSettings Load();

    /// <summary>
    /// Saves the settings to the store.
    /// </summary>
    /// <param name="settings">The settings to save.</param>
    void Save(HostSettings settings);
}
