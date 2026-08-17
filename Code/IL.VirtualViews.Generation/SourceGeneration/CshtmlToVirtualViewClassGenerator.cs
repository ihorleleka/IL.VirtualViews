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

}
