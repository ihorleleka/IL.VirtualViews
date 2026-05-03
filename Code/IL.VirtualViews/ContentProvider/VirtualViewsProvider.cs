#if NET8_0_OR_GREATER
using System.Collections.Frozen;
#endif
using System.Collections.Immutable;
using System.Reflection;
using IL.VirtualViews.Attributes;
using IL.VirtualViews.Interfaces;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Primitives;

namespace IL.VirtualViews.ContentProvider;

public sealed class VirtualViewsProvider : IFileProvider
{
#if NET8_0_OR_GREATER
    private FrozenDictionary<string, ViewDescriptor> SupportedTypes { get; }
#else
    private ImmutableDictionary<string, ViewDescriptor> SupportedTypes { get; }
#endif

    public VirtualViewsProvider(List<Type> supportedTypes, bool debugPhysicalFilesEnabled = false)
    {
        var debugRootPath = debugPhysicalFilesEnabled
            ? PrepareDebugRootPath()
            : null;

        SupportedTypes = supportedTypes

#if NET8_0_OR_GREATER
            .ToFrozenDictionary(
#else
            .ToImmutableDictionary(
#endif
                keySelector => (keySelector.GetCustomAttributes().First(x => x is VirtualViewPathAttribute) as VirtualViewPathAttribute)!.Path,
                valueSelector =>
                {
                    var property = valueSelector
                        .GetProperty(nameof(IVirtualView.ViewContent), BindingFlags.Public | BindingFlags.Static);

                    if (property == null)
                    {
                        throw new InvalidOperationException($"Type {valueSelector.FullName} does not implement the static property {nameof(IVirtualView.ViewContent)}.");
                    }

                    var content = (string)property.GetValue(null)!;
                    var virtualPath = (valueSelector.GetCustomAttributes().First(x => x is VirtualViewPathAttribute) as VirtualViewPathAttribute)!.Path;
                    var sourcePath = valueSelector.GetCustomAttributes()
                        .FirstOrDefault(x => x is VirtualViewSourcePathAttribute) as VirtualViewSourcePathAttribute;
                    var physicalPath = debugRootPath == null
                        ? null
                        : ResolveDebugPhysicalPath(debugRootPath, virtualPath, content, sourcePath?.SourcePath);

                    return new ViewDescriptor(content, physicalPath);
                }
            );
    }

    public IDirectoryContents GetDirectoryContents(string subpath)
    {
        // Not needed
        return new NotFoundDirectoryContents();
    }

    public IFileInfo GetFileInfo(string subpath)
    {
        if (SupportedTypes.TryGetValue(subpath, out var descriptor))
        {
            return new InMemoryFileInfo(subpath, descriptor.Content, descriptor.PhysicalPath);
        }

        return new NotFoundFileInfo(subpath);
    }

    public IChangeToken Watch(string filter)
    {
        // Not needed
        return NullChangeToken.Singleton;
    }

    private static string PrepareDebugRootPath()
    {
        var rootPath = Path.Combine(Path.GetTempPath(), "IL.VirtualViews", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(rootPath);
        return rootPath;
    }

    private static string WriteDebugPhysicalFile(string rootPath, string virtualPath, string content)
    {
        var relativePath = virtualPath.TrimStart('/', '\\').Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
        var fullPath = Path.Combine(rootPath, relativePath);
        var directory = Path.GetDirectoryName(fullPath);

        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(fullPath, content);
        return fullPath;
    }

    private static string ResolveDebugPhysicalPath(string rootPath, string virtualPath, string content, string? sourcePath)
    {
        if (!string.IsNullOrWhiteSpace(sourcePath) && File.Exists(sourcePath))
        {
            return sourcePath;
        }

        return WriteDebugPhysicalFile(rootPath, virtualPath, content);
    }
}

public sealed class InMemoryFileInfo : IFileInfo
{
    private readonly string _content;
    private readonly string? _physicalPath;

    public InMemoryFileInfo(string path, string content, string? physicalPath = null)
    {
        _content = content;
        _physicalPath = physicalPath;
        Name = Path.GetFileName(path);
    }

    public string Name { get; }

    public bool Exists => true;

    public long Length => _content.Length;

    public DateTimeOffset LastModified { get; } = DateTimeOffset.UtcNow;

    public string PhysicalPath => _physicalPath ?? null!;

    public bool IsDirectory => false;

    public Stream CreateReadStream()
    {
        return new MemoryStream(System.Text.Encoding.UTF8.GetBytes(_content));
    }
}

public sealed record ViewDescriptor(string Content, string? PhysicalPath);
