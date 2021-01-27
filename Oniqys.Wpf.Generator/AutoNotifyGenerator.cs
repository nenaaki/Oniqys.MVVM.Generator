// 参考 : https://github.com/dotnet/roslyn-sdk/blob/master/samples/CSharp/SourceGenerators/SourceGeneratorSamples/AutoNotifyGenerator.cs

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Oniqys.Wpf.Generator
{
    /// <summary>
    /// 通知型プロパティを自動生成します。
    /// </summary>
    [Generator]
    public partial class AutoNotifyGenerator : ISourceGenerator
    {
        /// <summary>
        /// 初期化します。
        /// </summary>
        public void Initialize(GeneratorInitializationContext context)
            => context.RegisterForSyntaxNotifications(() => new SyntaxReceiver());

        /// <summary>
        /// <see cref="INotiryPropertyChanged"/>の実装を付与し、通知型として動作します。
        /// </summary>
        public void Execute(GeneratorExecutionContext context)
        {
            context.AddSource("AutoNotifyAttribute", SourceText.From(attributeText, Encoding.UTF8));

            // SyntaxReceiverのみ受け付けます。
            if (!(context.SyntaxReceiver is SyntaxReceiver receiver))
                return;

            // 属性を含む新しいシンタックスツリーを作成します。
            // TODO: この手順が不要になるように、初期化中にソースジェネレータがソースを提供できるようにする必要があります。
            CSharpParseOptions options = (context.Compilation as CSharpCompilation).SyntaxTrees[0].Options as CSharpParseOptions;
            Compilation compilation = context.Compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText(SourceText.From(attributeText, Encoding.UTF8), options));

            // namespaceを含んだ名称からシンボルを取得します。
            INamedTypeSymbol attributeSymbol = compilation.GetTypeByMetadataName("Oniqys.Wpf.Generator.NotifiablePropertyAttribute");
            INamedTypeSymbol notifySymbol = compilation.GetTypeByMetadataName("System.ComponentModel.INotifyPropertyChanged");

            // 処理対象となるフィールドを収集します。
            List<IFieldSymbol> fieldSymbols = new List<IFieldSymbol>();
            foreach (FieldDeclarationSyntax field in receiver.CandidateFields)
            {
                SemanticModel model = compilation.GetSemanticModel(field.SyntaxTree);
                foreach (VariableDeclaratorSyntax variable in field.Declaration.Variables)
                {
                    // 属性が付与されているならそのフィールドのシンボルを保持します。
                    IFieldSymbol fieldSymbol = model.GetDeclaredSymbol(variable) as IFieldSymbol;
                    if (fieldSymbol.GetAttributes().Any(ad => ad.AttributeClass.Equals(attributeSymbol, SymbolEqualityComparer.Default)))
                    {
                        fieldSymbols.Add(fieldSymbol);
                    }
                }
            }

            // クラス単位で処理します。
            foreach (var group in fieldSymbols.GroupBy(f => f.ContainingType))
            {
                string classSource = ProcessClass(group.Key, group.ToList(), attributeSymbol, notifySymbol, context);
                context.AddSource($"{group.Key.Name}_autoNotify.cs", SourceText.From(classSource, Encoding.UTF8));
            }
        }

        private string ProcessClass(INamedTypeSymbol classSymbol, List<IFieldSymbol> fields, ISymbol attributeSymbol, ISymbol notifySymbol, GeneratorExecutionContext context)
        {
            if (!classSymbol.ContainingSymbol.Equals(classSymbol.ContainingNamespace, SymbolEqualityComparer.Default))
            {
                return null; //TODO: issue a diagnostic that it must be top level
            }

            string namespaceName = classSymbol.ContainingNamespace.ToDisplayString();

            // クラスの最初の部分
            StringBuilder source = new StringBuilder($@"
using System.ComponentModel;

namespace {namespaceName}
{{
    public partial class {classSymbol.Name} : {notifySymbol.ToDisplayString()}
    {{
");

            // インターフェースが実装されていない場合、実装する
            if (!classSymbol.Interfaces.Contains(notifySymbol))
            {
                source.Append("public event PropertyChangedEventHandler PropertyChanged;");
            }

            foreach (IFieldSymbol fieldSymbol in fields)
            {
                ProcessField(source, fieldSymbol, attributeSymbol);
            }

            source.Append("} }");
            return source.ToString();
        }

        /// <summary>
        /// フィールドの処理
        /// </summary>
        private void ProcessField(StringBuilder source, IFieldSymbol fieldSymbol, ISymbol attributeSymbol)
        {
            string fieldName = fieldSymbol.Name;
            ITypeSymbol fieldType = fieldSymbol.Type;

            AttributeData attributeData = fieldSymbol.GetAttributes().Single(ad => ad.AttributeClass.Equals(attributeSymbol, SymbolEqualityComparer.Default));
            TypedConstant overridenNameOpt = attributeData.NamedArguments.SingleOrDefault(kvp => kvp.Key == "PropertyName").Value;

            string propertyName = chooseName(fieldName, overridenNameOpt);
            if (propertyName.Length == 0 || propertyName == fieldName)
            {
                //TODO: プロパティ名が空か、フィールド名と同名を指定した場合、自動生成できません。
                return;
            }

            var xmlDocument = new XmlDocument();
            xmlDocument.LoadXml(fieldSymbol.GetDocumentationCommentXml());

            // TODO: 最初が summary とは限らないので後で正す
            var comment = xmlDocument.FirstChild?.InnerText?.Replace("\r\n", "");
            if (!string.IsNullOrWhiteSpace(comment))
            {
                source.Append($@"
/// <summary>
/// {comment}
/// </summary>");
            }
            source.Append($@"
public {fieldType} {propertyName} 
{{
    get => this.{fieldName};
    set
    {{
        this.{fieldName} = value;
        this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof({propertyName})));
    }}
}}

");

            string chooseName(string fieldName, TypedConstant overridenNameOpt)
            {
                if (!overridenNameOpt.IsNull)
                {
                    return overridenNameOpt.Value.ToString();
                }

                fieldName = fieldName.TrimStart('_');
                if (fieldName.Length == 0)
                    return string.Empty;

                if (fieldName.Length == 1)
                    return fieldName.ToUpper();

                return fieldName.Substring(0, 1).ToUpper() + fieldName.Substring(1);
            }

        }

        /// <summary>
        /// 生成パスの前にオンデマンドでシンタックスを受け取るクラス
        /// </summary>
        class SyntaxReceiver : ISyntaxReceiver
        {
            public List<FieldDeclarationSyntax> CandidateFields { get; } = new List<FieldDeclarationSyntax>();

            /// <summary>
            /// シンタックスノードのうち属性を持つフィールドのみを収集します。
            /// </summary>
            public void OnVisitSyntaxNode(SyntaxNode syntaxNode)
            {
                if (syntaxNode is FieldDeclarationSyntax fieldDeclarationSyntax
                    && fieldDeclarationSyntax.AttributeLists.Count > 0)
                {
                    CandidateFields.Add(fieldDeclarationSyntax);
                }
            }
        }
    }
}
