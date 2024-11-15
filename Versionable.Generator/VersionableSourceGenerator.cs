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
        private static readonly DiagnosticDescriptor InaccessibleStructRule = new DiagnosticDescriptor(
            id: "VER001",
            title: "Struct is not accessible",
            messageFormat: "The struct '{0}' or one of its parents is private or internal and cannot be versioned.",
            category: "Design",
            DiagnosticSeverity.Error, // Use Error if you want the build to fail; use Warning otherwise
            isEnabledByDefault: true
        );



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

                var containingTypes = new List<string>();
                var currentType = structSymbol; //.ContainingType;
                while (currentType != null)
                {
                    if (currentType.DeclaredAccessibility == Accessibility.Private ||
                        currentType.DeclaredAccessibility == Accessibility.Internal)
                    {
                        context.ReportDiagnostic(Diagnostic.Create(InaccessibleStructRule, structSymbol.Locations[0], structSymbol.Name));
                        goto NEXT_CANDIDATE_STRUCT;
                    }

                    containingTypes.Insert(0, currentType.Name);
                    currentType = currentType.ContainingType;
                }
                var fullTypeName = string.Join(".", containingTypes);// + "." : "") + structSymbol.Name;

                // Valid access, generate the class
                var generatedCode = GenerateVersionedClass(structSymbol, fullTypeName);
                context.AddSource($"{structSymbol.Name}_Versioned.g.cs", SourceText.From(generatedCode, Encoding.UTF8));

            NEXT_CANDIDATE_STRUCT:
                continue;
            }
        }

        private string GenerateVersionedClass(INamedTypeSymbol structSymbol, string fullTypeName)
        {
            var v_class_name = $"V_{structSymbol.Name}";

            var structNamespace =
                structSymbol.ContainingNamespace.IsGlobalNamespace ? string.Empty :
                structSymbol.ContainingNamespace?.ToDisplayString() ?? string.Empty;

            var structName = structSymbol.Name;
            var members = structSymbol.GetMembers()
                .Where(m => m.DeclaredAccessibility == Accessibility.Public && !m.IsStatic)
                .Where(m => m is IPropertySymbol || m is IFieldSymbol)
                .ToList();

            var builder = new StringBuilder();
            builder.AppendLine("using System;");
            builder.AppendLine("using Versionable;");
            builder.AppendLine("using System.Runtime.InteropServices;");
            builder.AppendLine("using System.Runtime.CompilerServices;");
            builder.AppendLine("namespace Versionable.Generated");
            builder.AppendLine("{");
            if (!string.IsNullOrEmpty(structNamespace))
            {
                builder.AppendLine($"    using {structNamespace};");
                builder.AppendLine();
            }
            builder.AppendLine($"    using {structName} = {structNamespace}{(string.IsNullOrEmpty(structNamespace) ? "" : ".")}{fullTypeName};");

            builder.AppendLine();
            builder.AppendLine($"    public class {v_class_name}");
            builder.AppendLine("    {");
            builder.AppendLine($"        private Versioned<{structName}> _versioned;");
            builder.AppendLine($"        public int VERSION => _versioned.VERSION;");
            builder.AppendLine($"        private IntPtr? _data;");
            builder.AppendLine();
            builder.AppendLine($"        public {v_class_name}(Versioned<{structName}> versioned) => _versioned = versioned;");
            builder.AppendLine($"        public {v_class_name}(ref {structName} item) => _versioned = new Versioned<{structName}>(ref item);");
            builder.AppendLine();

            var constructorParameters = new List<string>();
            var constructorAssignments = new List<string>();

            // setup properties and store contructor params
            foreach (var member in members)
            {
                string propName = member.Name;
                string propNameLowered = propName.ToLower();
                string propType;
                bool hasGetter = true, hasSetter = true;

                if (member is IPropertySymbol propertySymbol)
                {
                    propType = propertySymbol.Type.ToDisplayString();

                    hasGetter = propertySymbol.GetMethod != null &&
                                propertySymbol.GetMethod.DeclaredAccessibility == Accessibility.Public;

                    hasSetter = propertySymbol.SetMethod != null &&
                                propertySymbol.SetMethod.DeclaredAccessibility == Accessibility.Public;
                }
                else if (member is IFieldSymbol fieldSymbol) propType = fieldSymbol.Type.ToDisplayString();
                else throw new InvalidOperationException("Unexpected member type");

                builder.AppendLine($"        public {propType} {propName}");
                builder.AppendLine("        {");
                if (hasGetter) builder.AppendLine($"            get => _versioned.Peek.{propName};");
                if (hasSetter) builder.AppendLine($"            set => _versioned.Update((ref {structName} i) => i.{propName} = value);");
                builder.AppendLine("        }");
                builder.AppendLine();
            }

            // generate construtors ////////////////////////////////////////////////////////
            var constructors = structSymbol.InstanceConstructors
                .Where(ctor => ctor.DeclaredAccessibility == Accessibility.Public) // only public constructors
                .ToList();

            foreach (var ctor in constructors)
            {
                var parameters = ctor.Parameters;
                string parameterList = string.Join(", ", parameters.Select(p => $"{p.Type.ToDisplayString()} {p.Name}"));
                string parameterUsage = string.Join(", ", parameters.Select(p => p.Name));

                builder.AppendLine($@"
        public unsafe {v_class_name}({parameterList})
        {{
            _data = Marshal.AllocHGlobal(sizeof({structName}));
            {structName}* item = ({structName}*)_data;
            *item = new {structSymbol.Name}({parameterUsage});
            _versioned = new Versioned<{structName}>(ref Unsafe.AsRef<{structName}>(item));
        }}
    ");
            }

            // generate destructor
            builder.AppendLine($"        ~{v_class_name}() {{ if (_data.HasValue) Marshal.FreeHGlobal(_data.Value); }}");

            // close class & namespace
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
