﻿using System.Collections.Immutable;
using CSharp.SourceGen.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;


namespace CSharp.SourceGen.Inlining;


[Generator]
public class InliningGenerator : IIncrementalGenerator
{
    private const string Inline = nameof(Inline);
    private const string SupportsInliningAttribute = nameof(SupportsInliningAttribute);


    public static bool IsInlineAttribute(AttributeSyntax attribute)
    {
        var name = attribute.Name;
        while (true)
        {
            if (name is not QualifiedNameSyntax qns)
            {
                return false;
            }

            if (qns is
            {
                Right: IdentifierNameSyntax,
                Left: IdentifierNameSyntax leftName
            })
            {
                return leftName.Identifier.Text == Inline;
            }

            name = qns.Right;
        }
    }


    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        context.RegisterPostInitializationOutput(static context =>
        {
            context.AddSource("InliningAttributes.cs", """
#nullable enable
namespace CSharp.SourceGen.Inlining;


[AttributeUsage(AttributeTargets.Method)]
internal class GeneratedFromAttribute : Attribute
{
    public GeneratedFromAttribute(string originalName)
    {
    }
}


[AttributeUsage(AttributeTargets.Method)]
internal class InlineAttribute : Attribute
{
}


internal static class Inline
{

    [AttributeUsage(AttributeTargets.Method)]
    internal class PrivateAttribute : Attribute
    {
        public PrivateAttribute(string? name = null)
        {
        }
    }


    [AttributeUsage(AttributeTargets.Method)]
    internal class PublicAttribute : Attribute
    {
        public PublicAttribute(string? name = null)
        {
        }
    }


    [AttributeUsage(AttributeTargets.Method)]
    internal class ProtectedAttribute : Attribute
    {
        public ProtectedAttribute(string? name = null)
        {
        }
    }


    [AttributeUsage(AttributeTargets.Method)]
    internal class InternalAttribute : Attribute
    {
        public InternalAttribute(string? name = null)
        {
        }
    }

}


[AttributeUsage(AttributeTargets.Method)]
internal class SupportsInliningAttribute : Attribute
{
    public SupportsInliningAttribute(string template)
    {
    }
}
""");
        });

        var provider = context.SyntaxProvider.CreateSyntaxProvider(
            predicate: static (node, _) =>
                node is AttributeSyntax attribute && IsInlineAttribute(attribute),
            transform: static (context, token) =>
            {
                var methodSyntax = context.Node.FirstAncestorOrSelf<MethodDeclarationSyntax>();
                if (methodSyntax == null)
                {
                    return null;
                }

                var typeSyntax = methodSyntax.FirstAncestorOrSelf<TypeDeclarationSyntax>();
                if (typeSyntax == null)
                {
                    return null;
                }

                var methodSymbol =
                    (IMethodSymbol?)ModelExtensions.GetDeclaredSymbol(context.SemanticModel,
                        methodSyntax, token)
                    ?? throw new Exception("Type symbol was not found");

                var declarationInfo = QualifiedDeclarationInfo.FromSyntax(typeSyntax);

                var (inlinedMethodName, accessibility) =
                    GetInlinedMethodNameAndAccessibility(methodSymbol);

                var writer = new StringWriter();
                WriteInlinedMethod(writer, inlinedMethodName, methodSyntax,
                    context.SemanticModel, accessibility, token);

                return new
                {
                    FileName = methodSymbol.SuggestedFileName(methodSyntax.Identifier.Text),
                    Text = declarationInfo.ToString(
                        withUsing: "#define SOURCEGEN",
                        withMembers: writer.ToString())
                };
            });

        context.RegisterSourceOutput(provider,
            static (context, arg) =>
            {
                if (arg == null)
                {
                    return;
                }

                var source = CSharpSyntaxTree.ParseText(arg.Text)
                    .GetRoot().NormalizeWhitespace().ToFullString();

                context.AddSource(arg.FileName, source);
            });
    }


    private readonly record struct ParameterInfo(string Name, string? Type);


    private readonly record struct ArgumentInfo(string Name, string? Value, bool IsLambda);


    private readonly record struct InlinableMethodInfo(
        IMethodSymbol Symbol, InvocationExpressionSyntax InvocationSyntax)
    {
        /// <summary>
        /// Extracts extension method receiver text. For example 'Foo.Bar' for 'Foo.Bar.ExtensionMethod()'
        /// </summary>
        public string GetExtensionMethodReceiverText()
        {
            var memberAccess = this.InvocationSyntax.ChildNodes()
                .OfType<MemberAccessExpressionSyntax>().First();
            return memberAccess.ChildNodes().First().ToString();
        }


        public ImmutableArray<ArgumentInfo> GetArguments(ParenthesizedLambdaExpressionSyntax lambda)
        {
            var args = this.InvocationSyntax.ArgumentList.Arguments;
            if (args.Count == 0)
            {
                return ImmutableArray<ArgumentInfo>.Empty;
            }

            var lambdaArg = (ArgumentSyntax?)lambda.Parent;

            var names = this.Symbol.Parameters.Select(x => x.Name);
            var values = args.Select(x => x != lambdaArg ? x.ToString() : null);

            var argsCount = args.Count;
            var isExtensionMethod = this.Symbol.IsExtensionMethod;
            if (isExtensionMethod) ++argsCount;

            var builder = ImmutableArray.CreateBuilder<ArgumentInfo>(argsCount);
            builder.AddRange(names.Zip(values, (name, value) =>
            {
                var isLambda = value == null;
                if (name == "this")
                {
                    name = "@this";
                }

                if (!isLambda)
                {
                    name = "__" + name;
                }

                return new ArgumentInfo(name, value, isLambda);
            }));

            if (isExtensionMethod)
            {
                builder.Add(new ArgumentInfo("@this", this.GetExtensionMethodReceiverText(),
                    false));
            }

            return builder.MoveToImmutable();
        }
    }


    private static ImmutableArray<ParameterInfo> GetLambdaParameters(LambdaExpressionSyntax lambda)
    {
        var parameters = lambda.ChildNodes().OfType<ParameterListSyntax>()
            .First().Parameters;

        var builder = ImmutableArray.CreateBuilder<ParameterInfo>(parameters.Count);
        foreach (var parameter in parameters)
        {
            builder.Add(new ParameterInfo(
                parameter.Identifier.Text,
                parameter.Type?.ToString()));
        }

        return builder.MoveToImmutable();
    }


    private static string GetLambdaBody(LambdaExpressionSyntax lambda)
    {
        var body = lambda.ChildNodes().OfType<BlockSyntax>().First();
        return body.ToString();
    }


    private static InlinableMethodInfo GetInlinableMethodInfo(
        LambdaExpressionSyntax lambda, SemanticModel semanticModel, CancellationToken token)
    {
        var invocationExpression = lambda.FirstAncestorOrSelf<InvocationExpressionSyntax>();
        if (invocationExpression == null)
        {
            throw new Exception("Invocation was not found");
        }

        var child = invocationExpression.ChildNodes().First();
        var methodIdentifier = child.DescendantNodesAndSelf()
            .OfType<IdentifierNameSyntax>().Last();

        var getMethodSymbolResult =
            ModelExtensions.GetSymbolInfo(semanticModel, methodIdentifier, token);

        var methodSymbol = (IMethodSymbol?)getMethodSymbolResult.Symbol;
        if (methodSymbol == null)
        {
            if (getMethodSymbolResult.CandidateSymbols.Length == 1)
            {
                methodSymbol = (IMethodSymbol)getMethodSymbolResult.CandidateSymbols[0];
            }
            else
            {
                throw new Exception("Method symbol was not found");
            }
        }

        return new InlinableMethodInfo(methodSymbol, invocationExpression);
    }


    private static string GetInliningTemplate(IMethodSymbol method)
    {
        var methodAttribute = method.GetAttributes()
            .FirstOrDefault(x => x.AttributeClass?.Name == SupportsInliningAttribute);

        if (methodAttribute == null)
        {
            throw new Exception("Method does not support inlining");
        }

        var template = GetAttributeArgumentValue(methodAttribute);
        if (template == null)
        {
            throw new Exception("Inlining template is null.");
        }

        return template;
    }


    private static (string?, Accessibility) GetInlinedMethodNameAndAccessibility(
        IMethodSymbol methodSymbol)
    {
        var attribute = methodSymbol.GetAttributes()
            .First(x => x.AttributeClass is { ContainingType.Name: Inline });

        var accessibility = attribute.AttributeClass!.Name switch
        {
            "PrivateAttribute" => Accessibility.Private,
            "ProtectedAttribute" => Accessibility.Protected,
            "InternalAttribute" => Accessibility.Internal,
            "PublicAttribute" => Accessibility.Public,
            _ => Accessibility.Private
        };

        return (GetAttributeArgumentValue(attribute), accessibility);
    }


    private static string? GetAttributeArgumentValue(AttributeData attribute)
    {
        var arg = attribute.ConstructorArguments.First();
        return (string?)arg.Value;
    }


    private static string RenderTemplate(string lambdaArgName, string template,
        ImmutableArray<ParameterInfo> parameters, string body)
    {
        var source = template;
        for (var i = 0; i < parameters.Length; ++i)
        {
            var parameter = parameters[i];
            source = source.Replace($"{{{lambdaArgName}.arg{i}}}", parameter.Name);
            source = source.Replace($"{{{lambdaArgName}.arg{i}.type}}", parameter.Type);
        }

        return source.Replace($"{{{lambdaArgName}.body}}", body);
    }


    private static string GetInlinedText(ParenthesizedLambdaExpressionSyntax lambda,
        SemanticModel semanticModel, CancellationToken token)
    {
        var methodInfo = GetInlinableMethodInfo(lambda, semanticModel, token);
        var template = GetInliningTemplate(methodInfo.Symbol);

        var parameters = GetLambdaParameters(lambda);
        var body = GetLambdaBody(lambda);

        var writer = new StringWriter();
        writer.WriteLine("{");

        var args = methodInfo.GetArguments(lambda);
        foreach (var arg in args)
        {
            if (arg.IsLambda) continue;
            writer.Write("var ");
            writer.Write(arg.Name);
            writer.Write(" = ");
            writer.Write(arg.Value);
            writer.WriteLine(";");
        }

        var lambdaArgName = args.First(x => x.IsLambda).Name;
        writer.WriteLine(RenderTemplate(lambdaArgName, template, parameters, body));
        writer.WriteLine("}");

        return writer.ToString();
    }


    private static void WriteInlinedMethod(TextWriter writer, string? inlinedName,
        MethodDeclarationSyntax methodSyntax, SemanticModel semanticModel,
        Accessibility accessibility, CancellationToken token)
    {
        WriteInlinedMethodDeclaration(writer, inlinedName, methodSyntax, semanticModel,
            accessibility);
        writer.WriteLine();
        WriteInlinedMethodBlock(writer, methodSyntax, semanticModel, token);
    }


    private static void WriteInlinedMethodDeclaration(TextWriter writer,
        string? inlinedName, MethodDeclarationSyntax methodSyntax, SemanticModel semanticModel,
        Accessibility accessibility)
    {
        var symbol = (IMethodSymbol?)ModelExtensions.GetDeclaredSymbol(semanticModel, methodSyntax);
        if (symbol == null)
        {
            throw new Exception("Method symbol not found");
        }

        writer.Write($"[CSharp.SourceGen.Inlining.GeneratedFrom(nameof({symbol.Name}))]");

        var accessibilityStr = accessibility switch
        {
            Accessibility.Private => "private ",
            Accessibility.Protected => "protected ",
            Accessibility.Internal => "internal ",
            Accessibility.Public => "public ",
            _ => ""
        };

        writer.Write(accessibilityStr);
        if (symbol.IsStatic)
        {
            writer.Write("static ");
        }

        writer.Write(symbol.ReturnType);
        writer.Write(" ");
        writer.Write(inlinedName ?? symbol.Name + "_Inlined");

        var typeParameterList = methodSyntax.ChildNodes().OfType<TypeParameterListSyntax>()
            .FirstOrDefault();

        if (typeParameterList != null)
        {
            writer.Write(typeParameterList);
        }

        var parameterList = methodSyntax.ChildNodes().OfType<ParameterListSyntax>()
            .First();
        writer.Write(parameterList.ToString());
    }


    private static void WriteInlinedMethodBlock(TextWriter writer,
        MethodDeclarationSyntax methodSyntax, SemanticModel semanticModel, CancellationToken token)
    {
        var inlineAttributes = methodSyntax.DescendantNodes().Where(x => x.IsAttribute(Inline));
        var inlineBlocks = inlineAttributes.Select(inlineAttrNode =>
        {
            var lambda = inlineAttrNode.FirstAncestorOrSelf<ParenthesizedLambdaExpressionSyntax>()
                ?? throw new Exception("Lambda expression was not found.");

            var expression = lambda.FirstAncestorOrSelf<ExpressionStatementSyntax>()
                ?? throw new Exception("Expression statement was not found.");

            var inlinedText = GetInlinedText(lambda, semanticModel, token);

            return new
            {
                InlinedText = inlinedText,
                expression.SpanStart,
                SpanEnd = expression.Span.End
            };
        });

        var methodBlock = methodSyntax.ChildNodes().OfType<BlockSyntax>().First();
        var methodBlockText = methodSyntax.SyntaxTree.GetText();

        var lastPosition = methodBlock.SpanStart;
        foreach (var inlineBlock in inlineBlocks)
        {
            var sourceTextBeforeForEach =
                methodBlockText.ToString(TextSpan.FromBounds(lastPosition, inlineBlock.SpanStart));

            writer.WriteLine(sourceTextBeforeForEach);
            writer.WriteLine(inlineBlock.InlinedText);

            lastPosition = inlineBlock.SpanEnd;
        }

        writer.WriteLine(
            methodBlockText.ToString(TextSpan.FromBounds(lastPosition, methodBlock.Span.End)));
    }
}