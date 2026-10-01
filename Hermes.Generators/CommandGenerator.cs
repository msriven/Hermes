using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Hermes.Generators
{
    /// <summary>
    /// Generates command properties for methods marked with [Hermes.Command].
    /// </summary>
    [Generator]
    public sealed class CommandGenerator : IIncrementalGenerator
    {
        private const string AttributeMetadataName = "Hermes.CommandAttribute";

        private static readonly DiagnosticDescriptor TypeNotSupported = new DiagnosticDescriptor(
            "HRM001", "Unsupported containing type",
            "Method '{0}' has [Command], but its containing type must be a non-nested partial class",
            "Hermes", DiagnosticSeverity.Error, true);

        private static readonly DiagnosticDescriptor SignatureNotSupported = new DiagnosticDescriptor(
            "HRM002", "Unsupported command signature",
            "Method '{0}' has an unsupported signature for [Command]. Supported: void M(), void M(T), Task M(), Task M(CancellationToken), Task M(T), Task M(T, CancellationToken)",
            "Hermes", DiagnosticSeverity.Error, true);

        private static readonly DiagnosticDescriptor ValueTypeParameter = new DiagnosticDescriptor(
            "HRM003", "Value-type parameter",
            "Sync command '{0}' has a non-nullable value-type parameter. Use a nullable parameter (T?) or an async command",
            "Hermes", DiagnosticSeverity.Error, true);

        private static readonly DiagnosticDescriptor CanExecuteNotFound = new DiagnosticDescriptor(
            "HRM004", "CanExecute member not found",
            "CanExecute '{1}' for command '{0}' was not found as a bool property or bool method with a compatible signature; it is ignored",
            "Hermes", DiagnosticSeverity.Warning, true);

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var methods = context.SyntaxProvider.ForAttributeWithMetadataName(
                AttributeMetadataName,
                static (node, _) => node is MethodDeclarationSyntax,
                static (ctx, ct) => Analyze(ctx, ct));

            context.RegisterSourceOutput(methods.Collect(), static (spc, models) => Emit(spc, models));
        }

        private sealed class Model
        {
            public Diagnostic Error;
            public Diagnostic Warning;
            public string Namespace;
            public string TypeName;
            public string TypeDeclaration;
            public string MethodName;
            public string CommandName;
            public bool IsAsync;
            public string ParameterType;
            public string CanExecute;
            public List<string> Observed = new List<string>();
            public int Concurrency;
        }

        private static Model Analyze(GeneratorAttributeSyntaxContext ctx, CancellationToken ct)
        {
            var method = (IMethodSymbol)ctx.TargetSymbol;
            var attribute = ctx.Attributes[0];
            var location = method.Locations.FirstOrDefault();
            var type = method.ContainingType;
            var model = new Model { MethodName = method.Name };

            string canExecuteName = null;
            string customName = null;
            foreach (var argument in attribute.NamedArguments)
            {
                switch (argument.Key)
                {
                    case "CanExecute":
                        canExecuteName = argument.Value.Value as string;
                        break;
                    case "Name":
                        customName = argument.Value.Value as string;
                        break;
                    case "ObservedProperties":
                        if (!argument.Value.IsNull)
                            foreach (var item in argument.Value.Values)
                                if (item.Value is string text) model.Observed.Add(text);
                        break;
                    case "Concurrency":
                        if (argument.Value.Value is int mode) model.Concurrency = mode;
                        break;
                }
            }

            // Containing type: non-nested partial class
            bool partial = type.TypeKind == TypeKind.Class
                && type.ContainingType == null
                && type.DeclaringSyntaxReferences
                    .Select(r => r.GetSyntax(ct))
                    .OfType<TypeDeclarationSyntax>()
                    .All(d => d.Modifiers.Any(SyntaxKind.PartialKeyword));
            if (!partial)
            {
                model.Error = Diagnostic.Create(TypeNotSupported, location, method.Name);
                return model;
            }

            model.Namespace = type.ContainingNamespace.IsGlobalNamespace ? null : type.ContainingNamespace.ToDisplayString();
            model.TypeName = type.Name;
            model.TypeDeclaration = type.TypeParameters.Length == 0
                ? type.Name
                : type.Name + "<" + string.Join(", ", type.TypeParameters.Select(p => p.Name)) + ">";

            // Signature
            bool isVoid = method.ReturnsVoid;
            bool isTask = method.ReturnType.ToDisplayString() == "System.Threading.Tasks.Task";
            var parameters = method.Parameters;
            bool lastIsToken = parameters.Length > 0
                && parameters[parameters.Length - 1].Type.ToDisplayString() == "System.Threading.CancellationToken";
            int valueParameters = parameters.Length - (lastIsToken ? 1 : 0);

            if ((!isVoid && !isTask)
                || valueParameters > 1
                || (lastIsToken && !isTask)
                || parameters.Any(p => p.RefKind != RefKind.None)
                || method.IsGenericMethod)
            {
                model.Error = Diagnostic.Create(SignatureNotSupported, location, method.Name);
                return model;
            }

            model.IsAsync = isTask;

            if (valueParameters == 1)
            {
                var parameterType = parameters[0].Type;
                bool isNullableValue = parameterType.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T;
                if (!isTask && parameterType.IsValueType && !isNullableValue)
                {
                    model.Error = Diagnostic.Create(ValueTypeParameter, location, method.Name);
                    return model;
                }
                model.ParameterType = parameterType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            }

            // Name
            string trimmed = method.Name.EndsWith("Async") && method.Name.Length > 5
                ? method.Name.Substring(0, method.Name.Length - 5)
                : method.Name;
            model.CommandName = string.IsNullOrWhiteSpace(customName) ? trimmed + "Command" : customName;

            // CanExecute
            if (!string.IsNullOrEmpty(canExecuteName))
            {
                model.CanExecute = BuildCanExecute(type, canExecuteName, model.ParameterType != null);
                if (model.CanExecute == null)
                    model.Warning = Diagnostic.Create(CanExecuteNotFound, location, method.Name, canExecuteName);
            }

            return model;
        }

        private static string BuildCanExecute(INamedTypeSymbol type, string name, bool hasParameter)
        {
            foreach (var member in type.GetMembers(name))
            {
                if (member is IPropertySymbol property && property.Type.SpecialType == SpecialType.System_Boolean)
                    return hasParameter ? "_ => " + name : "() => " + name;

                if (member is IMethodSymbol m && m.ReturnType.SpecialType == SpecialType.System_Boolean)
                {
                    if (m.Parameters.Length == 0)
                        return hasParameter ? "_ => " + name + "()" : name;
                    if (m.Parameters.Length == 1 && hasParameter)
                        return name;
                }
            }
            return null;
        }

        private static void Emit(SourceProductionContext context, ImmutableArray<Model> models)
        {
            foreach (var model in models)
            {
                if (model.Error != null) context.ReportDiagnostic(model.Error);
                if (model.Warning != null) context.ReportDiagnostic(model.Warning);
            }

            var groups = models
                .Where(m => m.Error == null)
                .GroupBy(m => (m.Namespace ?? "") + "|" + m.TypeDeclaration);

            foreach (var group in groups)
            {
                var first = group.First();
                var sb = new StringBuilder();
                sb.AppendLine("// <auto-generated by Hermes.Generators/>");
                sb.AppendLine("#nullable enable");
                sb.AppendLine("using Hermes;");
                sb.AppendLine();

                string indent = "";
                if (first.Namespace != null)
                {
                    sb.AppendLine("namespace " + first.Namespace);
                    sb.AppendLine("{");
                    indent = "    ";
                }

                sb.AppendLine(indent + "partial class " + first.TypeDeclaration);
                sb.AppendLine(indent + "{");
                foreach (var model in group)
                    AppendCommand(sb, model, indent + "    ");
                sb.AppendLine(indent + "}");

                if (first.Namespace != null)
                    sb.AppendLine("}");

                string hint = ((first.Namespace ?? "global") + "." + first.TypeName).Replace('<', '_').Replace('>', '_') + ".Commands.g.cs";
                context.AddSource(hint, SourceText.From(sb.ToString(), Encoding.UTF8));
            }
        }

        private static void AppendCommand(StringBuilder sb, Model model, string indent)
        {
            string commandType = (model.IsAsync ? "global::Hermes.AsyncDelegateCommand" : "global::Hermes.DelegateCommand")
                + (model.ParameterType != null ? "<" + model.ParameterType + ">" : "");

            string field = "_" + char.ToLowerInvariant(model.CommandName[0]) + model.CommandName.Substring(1);

            var creation = new StringBuilder();
            creation.Append("new " + commandType + "(" + model.MethodName);
            if (model.CanExecute != null)
                creation.Append(", " + model.CanExecute);
            creation.Append(")");

            foreach (var observed in model.Observed)
                creation.Append(".ObservesProperty(() => " + observed + ")");

            if (model.IsAsync && model.Concurrency != 0)
                creation.Append(".WithConcurrency((global::Hermes.ConcurrencyMode)" + model.Concurrency + ")");

            sb.AppendLine(indent + "private " + commandType + "? " + field + ";");
            sb.AppendLine(indent + "/// <summary>Command generated from <see cref=\"" + model.MethodName + "\"/>.</summary>");
            sb.AppendLine(indent + "public " + commandType + " " + model.CommandName + " => " + field + " ??= " + creation + ";");
            sb.AppendLine();
        }
    }
}
