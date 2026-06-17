using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using FourRVivi.Plugins.Abstractions;

namespace FourRVivi.App.Services;

/// <summary>Loads each plugin in its own collectible context so plugins can't clash.</summary>
public sealed class IsolatedPluginLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver _resolver;
    public IsolatedPluginLoadContext(string mainPath) : base(isCollectible: true) => _resolver = new AssemblyDependencyResolver(mainPath);
    protected override Assembly? Load(AssemblyName name)
    {
        var p = _resolver.ResolveAssemblyToPath(name);
        return p != null ? LoadFromAssemblyPath(p) : null;
    }
}

public interface IPluginLoader { IReadOnlyList<IPlugin> LoadAll(string pluginsDir); }

/// <summary>Scans the Plugins folder, loads each .dll, and instantiates every IPlugin it finds.</summary>
public sealed class PluginLoader : IPluginLoader
{
    public IReadOnlyList<IPlugin> LoadAll(string pluginsDir)
    {
        var found = new List<IPlugin>();
        try
        {
            if (!Directory.Exists(pluginsDir)) return found;
            foreach (var dll in Directory.GetFiles(pluginsDir, "*.dll"))
            {
                try
                {
                    var asm = new IsolatedPluginLoadContext(dll).LoadFromAssemblyPath(dll);
                    foreach (var ty in asm.GetTypes())
                    {
                        if (typeof(IPlugin).IsAssignableFrom(ty) && !ty.IsAbstract && ty.GetConstructor(Type.EmptyTypes) != null
                            && Activator.CreateInstance(ty) is IPlugin p) found.Add(p);
                    }
                }
                catch { }
            }
        }
        catch { }
        return found;
    }
}
