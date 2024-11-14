using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using System.Linq;
using System.Text;
using System;

namespace Versionable
{
    [Generator]
    public class VersionableSourceGenerator : ISourceGenerator
    {
        public void Initialize(GeneratorInitializationContext context)
        {
            context.RegisterForSyntaxNotifications(() => new VersionableSyntaxReceiver());
        }

        public void Execute(GeneratorExecutionContext context)
        {
            if (!(context.SyntaxReceiver is VersionableSyntaxReceiver receiver))
                return;

            foreach (var structDeclaration in receiver.CandidateStructs)
            {
                var model = context.Compilation.GetSemanticModel(structDeclaration.SyntaxTree);
                var structSymbol = model.GetDeclaredSymbol(structDeclaration) as INamedTypeSymbol;

                var generatedCode = GenerateVersionedClass(structSymbol);
                context.AddSource($"{structSymbol.Name}_Versioned.g.cs", SourceText.From(generatedCode, Encoding.UTF8));
            }
        }

        private string GenerateVersionedClass(INamedTypeSymbol structSymbol)
        {
            var structName = structSymbol.Name;
            var members = structSymbol.GetMembers()
                .Where(m => m.DeclaredAccessibility == Accessibility.Public && !m.IsStatic)
                .Where(m => m is IPropertySymbol || m is IFieldSymbol)
                .ToList();

            var builder = new StringBuilder();
            builder.AppendLine("using System;");
            builder.AppendLine("using Versionable;");
            builder.AppendLine("namespace Versionable.Generated");
            builder.AppendLine("{");
            builder.AppendLine($"    public class V_{structName}");
            builder.AppendLine("    {");
            builder.AppendLine($"        private readonly Versioned<{structName}> _versioned;");
            builder.AppendLine();
            builder.AppendLine($"        public V_{structName}(Versioned<{structName}> versioned) => _versioned = versioned;");
            builder.AppendLine($"        public V_{structName}(ref {structName} item) => _versioned = new Versioned<{structName}>(ref item);");
            builder.AppendLine();

            foreach (var member in members)
            {
                string propName = member.Name;
                string propType;

                if (member is IPropertySymbol propertySymbol) propType = propertySymbol.Type.ToDisplayString();
                else if (member is IFieldSymbol fieldSymbol)  propType = fieldSymbol.Type.ToDisplayString();
                else throw new InvalidOperationException("Unexpected member type");
                
                builder.AppendLine($"        public {propType} {propName}");
                builder.AppendLine("        {");
                builder.AppendLine($"            get => _versioned.Peek.{propName};");
                builder.AppendLine($"            set => _versioned.Update((ref {structName} i) => i.{propName} = value);");
                builder.AppendLine("        }");
                builder.AppendLine();
            }

            builder.AppendLine("    }");
            builder.AppendLine("}");
            return builder.ToString();
        }
    }

    internal class VersionableSyntaxReceiver : ISyntaxReceiver
    {
        public List<StructDeclarationSyntax> CandidateStructs { get; } = new List<StructDeclarationSyntax>();

        public void OnVisitSyntaxNode(SyntaxNode syntaxNode)
        {
            if (syntaxNode is StructDeclarationSyntax structDeclaration &&
                structDeclaration.AttributeLists.Any(attrList =>
                    attrList.Attributes.Any(attr =>
                        attr.Name.ToString() == "Versionable" ||
                        attr.Name.ToString() == "Versioning.Versionable")))
            {
                CandidateStructs.Add(structDeclaration);
            }
        }
    }
}
