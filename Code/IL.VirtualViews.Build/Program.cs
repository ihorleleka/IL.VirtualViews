using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace IL.VirtualViews.Build;

internal static class Program
{
    private const string AttributeName = "IL.VirtualViews.Attributes.VirtualViewPathAttribute";

    public static int Main(string[] args)
    {
        try
        {
            var options = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var i = 0; i < args.Length; i += 2)
                options.Add(args[i], args[i + 1]);
            Run(options);
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"error VVIEW001: {exception.Message.Replace('\r', ' ').Replace('\n', ' ')}");
            return 1;
        }
    }

    private static void Run(Dictionary<string, string> options)
    {
        var output = Path.GetFullPath(options["--output"]);
        var project = Path.GetFullPath(options["--project"]);
        var assembly = options["--assembly"];
        var manifest = new BuildManifest(options["--manifest"], project);
        var language = LanguageVersion.Preview;
        if (options.TryGetValue("--language", out var languageText) && !string.IsNullOrEmpty(languageText)
            && !LanguageVersionFacts.TryParse(languageText, out language))
        {
            // A newer SDK may advertise a numeric language version not yet named by
            // this private Roslyn. Preview is the newest syntax this tool can analyze.
            if (Version.TryParse(languageText, out var future) && future.Major > 14)
                language = LanguageVersion.Preview;
            else
                throw new InvalidOperationException($"Unsupported C# language version '{languageText}'.");
        }
        var defines = options.GetValueOrDefault("--defines", "").Split([';', ','], StringSplitOptions.RemoveEmptyEntries);
        var parse = new CSharpParseOptions(language, preprocessorSymbols: defines);
        var trees = manifest.Files('C').Select(path => CSharpSyntaxTree.ParseText(File.ReadAllText(path), parse, path)).ToArray();
        var references = manifest.Files('R').Select(path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create(assembly, trees, references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var templates = TemplateDiscovery.Find(manifest.Files('A'), assembly);

        var classes = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        foreach (var tree in trees)
        {
            var model = compilation.GetSemanticModel(tree);
            foreach (var declaration in tree.GetRoot().DescendantNodes().OfType<TypeDeclarationSyntax>())
                if (model.GetDeclaredSymbol(declaration) is INamedTypeSymbol { TypeKind: TypeKind.Class } type)
                    classes.Add(type);
        }
        var results = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var companions = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var sourceTemplates = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var type in classes.Where(type => !type.IsAbstract && HasVirtualPath(type)))
        {
            var name = GetRuntimeName(type);
            var hasTemplate = templates.TryGetValue(name, out var template);
            if (!hasTemplate && !type.AllInterfaces.Any(@interface =>
                @interface.ToDisplayString() == "IL.VirtualViews.Interfaces.IVirtualView"))
                continue;
            if (type.DeclaredAccessibility != Accessibility.Public)
            {
                // The companion contributes a public partial declaration. Accept an
                // accessibility-less source partial before that companion is compiled.
                var declarations = type.DeclaringSyntaxReferences.Select(reference => reference.GetSyntax())
                    .OfType<TypeDeclarationSyntax>().ToArray();
                var implicitlyPublicPartial = hasTemplate && declarations.Length > 0
                    && declarations.All(declaration => declaration.Modifiers.Any(SyntaxKind.PartialKeyword)
                        && !declaration.Modifiers.Any(modifier => modifier.IsKind(SyntaxKind.InternalKeyword)
                            || modifier.IsKind(SyntaxKind.PrivateKeyword) || modifier.IsKind(SyntaxKind.ProtectedKeyword)));
                if (!implicitlyPublicPartial)
                {
                    if (hasTemplate)
                        throw new InvalidOperationException($"Virtual view '{name}' has an associated template but explicitly non-public or non-partial declarations conflict with the generated public partial class.");
                    continue;
                }
            }
            if (type.IsGenericType || type.ContainingType is not null)
                throw new InvalidOperationException($"Virtual view '{type}' must be a non-generic, top-level class.");
            if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                throw new InvalidOperationException($"Virtual view name '{name}' cannot be represented as a generated Razor file.");
            if (hasTemplate)
            {
                if (type.DeclaringSyntaxReferences.Select(reference => reference.GetSyntax())
                    .OfType<TypeDeclarationSyntax>().Any(declaration => !declaration.Modifiers.Any(SyntaxKind.PartialKeyword)))
                    throw new InvalidOperationException($"Virtual view '{name}' must be partial when associated with a .virtual.cshtml AdditionalFile.");
                if (type.GetMembers("ViewContent").Length > 0)
                    throw new InvalidOperationException($"Virtual view '{name}' cannot declare ViewContent as well as an associated .virtual.cshtml AdditionalFile.");
                var content = File.ReadAllText(template!);
                sourceTemplates.Add(name, template!);
                companions.Add(name, CompanionWriter.Create(type, content));
            }
            else
            {
                var content = GetConstantContent(type, compilation);
                if (content is not null)
                    results.Add(name, content);
                else
                    throw new InvalidOperationException($"Virtual view '{name}' has no associated .virtual.cshtml AdditionalFile and no public static ViewContent property with a constant string expression. Dynamic ViewContent is not supported by build-time Razor compilation.");
            }
        }

        GeneratedOutputs.Write(output, "generated.files", ".cshtml", results);
        GeneratedOutputs.Write(output, "generated.csfiles", ".g.cs", companions);
        RazorInputManifest.Write(output, sourceTemplates, results);
        Console.WriteLine($"VirtualViews: prepared {sourceTemplates.Count + results.Count} build-time Razor view(s) and {companions.Count} C# companion(s).");
    }

    private static string GetRuntimeName(INamedTypeSymbol type)
    {
        // Symbol display escapes C# keywords with '@'; reflection FullName does not.
        var parts = new Stack<string>();
        for (var ns = type.ContainingNamespace; ns is { IsGlobalNamespace: false }; ns = ns.ContainingNamespace)
            parts.Push(ns.MetadataName);
        return parts.Count == 0 ? type.MetadataName : string.Join('.', parts) + "." + type.MetadataName;
    }

    private static bool HasVirtualPath(INamedTypeSymbol type)
    {
        for (var current = type; current is not null; current = current.BaseType)
            foreach (var attribute in current.GetAttributes())
                for (var attributeType = attribute.AttributeClass; attributeType is not null; attributeType = attributeType.BaseType)
                    if (attributeType.ToDisplayString() == AttributeName)
                        return true;
        return false;
    }

    private static string? GetConstantContent(INamedTypeSymbol type, CSharpCompilation compilation)
    {
        // Accept inherited public static implementations too; never execute consumer code.
        for (var current = type; current is not null; current = current.BaseType)
        {
            var property = current.GetMembers("ViewContent").OfType<IPropertySymbol>()
                .FirstOrDefault(property => property.IsStatic && property.DeclaredAccessibility == Accessibility.Public
                    && property.Type.SpecialType == SpecialType.System_String);
            if (property is null)
                continue;
            foreach (var syntax in property.DeclaringSyntaxReferences)
            {
                if (syntax.GetSyntax() is not PropertyDeclarationSyntax declaration)
                    continue;
                ExpressionSyntax? expression = declaration.ExpressionBody?.Expression ?? declaration.Initializer?.Value;
                var getter = declaration.AccessorList?.Accessors.SingleOrDefault(accessor => accessor.IsKind(SyntaxKind.GetAccessorDeclaration));
                expression ??= getter?.ExpressionBody?.Expression;
                if (expression is null && getter?.Body?.Statements is { Count: 1 } statements
                    && statements[0] is ReturnStatementSyntax statement)
                    expression = statement.Expression;
                if (expression is not null)
                {
                    var value = compilation.GetSemanticModel(expression.SyntaxTree).GetConstantValue(expression);
                    if (value.HasValue && value.Value is string text)
                        return text;
                }
            }
            return null;
        }
        return null;
    }
}
