namespace MSOSync.Sdk.Abstractions;

public interface IPluginServices
{
    T              GetRequiredService<T>() where T : notnull;
    T?             GetService<T>();
    IEnumerable<T> GetServices<T>();
}
