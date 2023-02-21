using System.Collections.Immutable;
using CSharp.SourceGen.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;


namespace CSharp.SourceGen.Inlining;


[Generator]
public class InliningGenerator : IIncrementalGenerator
{
    private const string Inline = nameof(Inline);
    private const string GenerateInlined = nameof(GenerateInlined);
    private const string SupportsInliningAttribute = nameof(SupportsInliningAttribute);


    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var provider = context.SyntaxProvider.CreateSyntaxProvider(
            predicate: static (node, _) => node.IsAttribute(GenerateInlined),
            transform: static (context, _) =>
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
                    (IMethodSymbol?)context.SemanticModel.GetDeclaredSymbol(methodSyntax)
                    ?? throw new Exception("Type symbol was not found");

                var declarationInfo = QualifiedDeclarationInfo.FromSyntax(typeSyntax);

                var writer = new StringWriter();
                WriteInlinedMethod(writer, methodSyntax, context.SemanticModel);

                return new
                {
                    FileName = methodSymbol.SuggestedFileName(methodSyntax.Identifier.Text),
                    Text = declarationInfo.ToString(
                        withUsing: "#define SOURCEGEN",
                        withMembers: writer.ToString())
                };
            }).Where(x => x != null);

        context.RegisterSourceOutput(provider,
            static (context, arg) => { context.AddSource(arg!.FileName, arg.Text); });
    }


    private readonly record struct ParameterInfo(string Name, string Type);
    private readonly record struct ArgumentInfo(string Name, string Value);


    private readonly record struct InlinableMethodInfo(
        IMethodSymbol Symbol, InvocationExpressionSyntax InvocationSyntax)
    {
        public IEnumerable<ArgumentInfo> GetArgumentsExceptLambda(
            ParenthesizedLambdaExpressionSyntax lambda)
        {
            var lambdaArg = (ArgumentSyntax?)lambda.Parent;
            var args = this.InvocationSyntax.ArgumentList.Arguments;

            var names = this.Symbol.Parameters.Select(x => x.Name);
            var values = args.Select(x => x != lambdaArg ? x.ToString() : null);

            return names.Zip(values, (name, value) =>
                    value != null ? new ArgumentInfo(name, value) : default)
                .Where(x => !string.IsNullOrEmpty(x.Name));
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
                parameter.Type!.ToString()));
        }

        return builder.MoveToImmutable();
    }


    private static string GetLambdaBody(LambdaExpressionSyntax lambda)
    {
        var body = lambda.ChildNodes().OfType<BlockSyntax>().First();
        return body.ToString();
    }


    private static InlinableMethodInfo GetInlinableMethodInfo(
        LambdaExpressionSyntax lambda, SemanticModel semanticModel)
    {
        var invocationExpression = lambda.FirstAncestorOrSelf<InvocationExpressionSyntax>();
        if (invocationExpression == null)
        {
            throw new Exception("Invocation was not found");
        }

        IdentifierNameSyntax methodIdentifier;
        {
            var child = invocationExpression.ChildNodes().First();
            methodIdentifier = child.DescendantNodesAndSelf()
                .OfType<IdentifierNameSyntax>().Last();
        }

        var methodSymbol = (IMethodSymbol?)semanticModel.GetSymbolInfo(methodIdentifier).Symbol;
        if (methodSymbol == null)
        {
            throw new Exception("Method symbol was not found");
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

        var templateArg = methodAttribute.ConstructorArguments.First();
        var template = (string?)templateArg.Value;
        if (template == null)
        {
            throw new Exception("Inlining template is null.");
        }

        return template;
    }


    private static string RenderTemplate(string template, ImmutableArray<ParameterInfo> parameters,
        string body)
    {
        var source = template;
        for (var i = 0; i < parameters.Length; ++i)
        {
            var parameter = parameters[i];
            source = source.Replace($"{{name{i}}}", parameter.Name);
            source = source.Replace($"{{type{i}}}", parameter.Type);
        }

        return source.Replace("{body}", body);
    }


    private static string GetInlinedText(ParenthesizedLambdaExpressionSyntax lambda,
        SemanticModel semanticModel)
    {
        var methodInfo = GetInlinableMethodInfo(lambda, semanticModel);
        var template = GetInliningTemplate(methodInfo.Symbol);

        var parameters = GetLambdaParameters(lambda);
        var body = GetLambdaBody(lambda);

        var writer = new StringWriter();
        writer.WriteLine("{");

        foreach (var arg in methodInfo.GetArgumentsExceptLambda(lambda))
        {
            writer.Write("var ");
            writer.Write(arg.Name);
            writer.Write(" = ");
            writer.Write(arg.Value);
            writer.WriteLine(";");
        }

        writer.WriteLine(RenderTemplate(template, parameters, body));
        writer.WriteLine("}");

        return writer.ToString();
    }


    private static void WriteInlinedMethod(TextWriter writer, MethodDeclarationSyntax methodSyntax,
        SemanticModel semanticModel)
    {
        WriteInlinedMethodDeclaration(writer, methodSyntax, semanticModel);
        writer.WriteLine();
        WriteInlinedMethodBlock(writer, methodSyntax, semanticModel);
    }


    private static void WriteInlinedMethodDeclaration(TextWriter writer,
        MethodDeclarationSyntax methodSyntax, SemanticModel semanticModel)
    {
        var symbol = (IMethodSymbol?)semanticModel.GetDeclaredSymbol(methodSyntax);
        if (symbol == null)
        {
            throw new Exception("Method symbol not found");
        }

        var accessibility = symbol.DeclaredAccessibility == Accessibility.Public
            ? "public "
            : "private ";

        writer.Write(accessibility);
        if (symbol.IsStatic)
        {
            writer.Write("static ");
        }

        writer.Write(symbol.ReturnType);
        writer.Write(" ");
        writer.Write(symbol.Name);
        writer.Write("_inlined");

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
        MethodDeclarationSyntax methodSyntax, SemanticModel semanticModel)
    {
        var inlineAttributes = methodSyntax.DescendantNodes().Where(x => x.IsAttribute(Inline));
        var inlineBlocks = inlineAttributes.Select(inlineAttrNode =>
        {
            var lambda = inlineAttrNode.FirstAncestorOrSelf<ParenthesizedLambdaExpressionSyntax>()
                ?? throw new Exception("Lambda expression was not found.");

            var expression = lambda.FirstAncestorOrSelf<ExpressionStatementSyntax>()
                ?? throw new Exception("Expression statement was not found.");

            var inlinedText = GetInlinedText(lambda, semanticModel);

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