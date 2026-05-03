using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using IL.VirtualViews.Generation.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace IL.VirtualViews.Generation.SourceGeneration;

[Generator]
public sealed class CshtmlToVirtualViewClassGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var compilationProvider = context.CompilationProvider;

        // Get the .virtual.cshtml files
        var cshtmlFiles = context
            .AdditionalTextsProvider
            .Where(at => at.Path.EndsWith(".virtual.cshtml"))
            .Select((cshtmlFile, cancellationToken) =>
            {
                var className = Path.GetFileNameWithoutExtension(cshtmlFile.Path);
                className = className.Replace(".virtual", string.Empty);
                var content = cshtmlFile.GetText(cancellationToken)?.ToString() ?? string.Empty;
                return new GenerationClass(className, content, cshtmlFile.Path);
            })
            .Collect();

        // Combine the cshtmlFiles with the assembly name to pass both pieces of information to the next step
        var combined = cshtmlFiles.Combine(compilationProvider.Select((compilation, _) => compilation.AssemblyName));

        context.RegisterSourceOutput(combined, Generate!);
        context.RegisterSourceOutput(compilationProvider, GenerateRegistry!);
    }

    private static void Generate(SourceProductionContext spc, (ImmutableArray<GenerationClass> generationClasses, string assemblyName) combined)
    {
        foreach (var generationClass in combined.generationClasses)
        {
            var pathSplit = Path
                .GetDirectoryName(generationClass.Path)!
                .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Where(x => !string.IsNullOrEmpty(x))
                .ToList();
            var startIndex = pathSplit.LastIndexOf(pathSplit.LastOrDefault(x => combined.assemblyName.StartsWith(x, StringComparison.InvariantCultureIgnoreCase)));
            var namespaceToUse = startIndex != -1 ? string.Join(".", pathSplit.Skip(startIndex)) : combined.assemblyName;

            spc.AddSource($"{generationClass.Name}.g.cs",
                BuildClassDeclarationSyntaxWithinGivenNamespace(generationClass, namespaceToUse)
                    .NormalizeWhitespace()
                    .ToFullString()
            );
        }
    }

    private static CompilationUnitSyntax BuildClassDeclarationSyntaxWithinGivenNamespace(GenerationClass generationClass, string namespaceToUse)
    {
        var compilationUnit = CompilationUnit()
            .AddUsings(
                CreateUsing("IL.VirtualViews.Interfaces"),
                CreateUsing("IL.VirtualViews.Attributes")
            )
            .AddMembers(
                FileScopedNamespaceDeclaration(IdentifierName(namespaceToUse))
                    .AddMembers(CreateClassSyntaxDeclaration(generationClass))
            );

        return compilationUnit;
    }

    private static ClassDeclarationSyntax CreateClassSyntaxDeclaration(GenerationClass generationClass)
    {
        var viewContent = WrapTextInTripleQuotes(generationClass);
        return ClassDeclaration(Identifier(generationClass.Name))
            .AddModifiers(Token(SyntaxKind.PublicKeyword))
            .AddModifiers(Token(SyntaxKind.PartialKeyword))
            .AddAttributeLists(
                AttributeList(SingletonSeparatedList(
                    Attribute(IdentifierName("VirtualViewSourcePath"))
                        .WithArgumentList(
                            AttributeArgumentList(
                                SingletonSeparatedList(
                                    AttributeArgument(
                                        LiteralExpression(
                                            SyntaxKind.StringLiteralExpression,
                                            Literal(generationClass.Path)
                                        )
                                    )
                                )
                            )
                        )
                ))
            )
            .AddBaseListTypes(SimpleBaseType(IdentifierName("IVirtualView")))
            .AddMembers(
                PropertyDeclaration(
                        PredefinedType(Token(SyntaxKind.StringKeyword)),
                        Identifier("ViewContent")
                    )
                    .AddModifiers(
                        Token(SyntaxKind.PublicKeyword),
                        Token(SyntaxKind.StaticKeyword)
                    )
                    .WithExpressionBody(
                        ArrowExpressionClause(
                            LiteralExpression(SyntaxKind.StringLiteralExpression,
                                Token(
                                    SyntaxTriviaList.Empty,
                                    SyntaxKind.MultiLineRawStringLiteralToken,
                                    viewContent,
                                    viewContent,
                                    SyntaxTriviaList.Empty
                                )
                            )
                        )
                    )
                    .WithSemicolonToken(Token(SyntaxKind.SemicolonToken))
            );
    }

    private static string WrapTextInTripleQuotes(GenerationClass generationClass)
    {
        return $""""
                """
                {generationClass.CshtmlContent}
                """
                """";
    }

    private static UsingDirectiveSyntax CreateUsing(string namespaceName)
    {
        var parts = namespaceName.Split('.');
        NameSyntax name = IdentifierName(parts[0]);
        name = parts.Skip(1).Aggregate(name, (current, part) => QualifiedName(current, IdentifierName(part)));
        return UsingDirective(name);
    }

    private static void GenerateRegistry(SourceProductionContext spc, Compilation compilation)
    {
        var iVirtualView = compilation.GetTypeByMetadataName("IL.VirtualViews.Interfaces.IVirtualView");
        var virtualViewPathAttribute = compilation.GetTypeByMetadataName("IL.VirtualViews.Attributes.VirtualViewPathAttribute");

        if (iVirtualView == null || virtualViewPathAttribute == null)
        {
            return;
        }

        var allTypes = GetAllTypes(compilation.Assembly.GlobalNamespace);
        var matchingTypes = new System.Collections.Generic.List<INamedTypeSymbol>();

        foreach (var type in allTypes)
        {
            if (type.IsAbstract || type.IsGenericType)
            {
                continue;
            }

            if (!type.AllInterfaces.Contains(iVirtualView, SymbolEqualityComparer.Default))
            {
                continue;
            }

            if (!HasVirtualViewPathAttribute(type, virtualViewPathAttribute))
            {
                continue;
            }

            matchingTypes.Add(type);
        }

        var source = $$"""
                       namespace IL.VirtualViews.Generated;

                       internal static class VirtualViewRegistry
                       {
                           internal static global::System.Type[] GetVirtualViewTypes()
                           {
                               return new global::System.Type[]
                               {
                       {{string.Join("\n", matchingTypes.Select(x => $"                    typeof({x.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}),"))}}
                               };
                           }
                       }
                       """;

        spc.AddSource("VirtualViewRegistry.g.cs", source);
    }

    private static bool HasVirtualViewPathAttribute(INamedTypeSymbol type, INamedTypeSymbol virtualViewPathAttribute)
    {
        var attribute = type.GetAttributes()
            .FirstOrDefault(x => x.AttributeClass != null && InheritsFromOrEquals(x.AttributeClass, virtualViewPathAttribute));

        return attribute != null;
    }

    private static bool InheritsFromOrEquals(INamedTypeSymbol type, INamedTypeSymbol candidateBase)
    {
        var current = type;
        while (current != null)
        {
            if (SymbolEqualityComparer.Default.Equals(current, candidateBase))
            {
                return true;
            }

            current = current.BaseType;
        }

        return false;
    }

    private static System.Collections.Generic.IEnumerable<INamedTypeSymbol> GetAllTypes(INamespaceSymbol root)
    {
        foreach (var member in root.GetMembers())
        {
            if (member is INamespaceSymbol @namespace)
            {
                foreach (var child in GetAllTypes(@namespace))
                {
                    yield return child;
                }
            }
            else if (member is INamedTypeSymbol namedType)
            {
                foreach (var child in GetAllTypes(namedType))
                {
                    yield return child;
                }
            }
        }
    }

    private static System.Collections.Generic.IEnumerable<INamedTypeSymbol> GetAllTypes(INamedTypeSymbol root)
    {
        yield return root;

        foreach (var nested in root.GetTypeMembers())
        {
            foreach (var child in GetAllTypes(nested))
            {
                yield return child;
            }
        }
    }
}
