namespace MSOSync.Sdk.Abstractions;

public interface IPluginConfiguration
{
    T?                          GetValue<T>(string key);
    T                           GetValue<T>(string key, T defaultValue);
    IPluginConfiguration        GetSection(string sectionName);
    IReadOnlyCollection<string> Keys  { get; }
    bool                        Exists(string key);
}
