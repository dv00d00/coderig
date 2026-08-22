using System.IO.Hashing;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Rig.Analysis.Extraction;

// Canonical token hashing shared by declared-symbol surfaces and project-only inputs. Token.Text preserves
// punctuation/operator/literal spelling; an unambiguous separator preserves token boundaries while dropping
// every trivia node, so formatting and comments do not move a hash.
internal static class SurfaceHashing
{
    private const char TokenSeparator = '\u001f';

    public static string Declaration(ISymbol symbol, SyntaxNode node)
    {
        if (node is BaseNamespaceDeclarationSyntax || symbol is INamespaceSymbol)
        {
            return "";
        }

        var tokens = new List<SyntaxToken>();
        switch (node)
        {
            case TypeDeclarationSyntax type:
                Add(tokens, type.AttributeLists);
                Add(tokens, type.Modifiers);
                Add(tokens, type.Keyword);
                if (type is RecordDeclarationSyntax record)
                {
                    Add(tokens, record.ClassOrStructKeyword);
                }
                Add(tokens, type.Identifier);
                Add(tokens, type.TypeParameterList);
                Add(tokens, type.ParameterList);
                Add(tokens, type.BaseList);
                Add(tokens, type.ConstraintClauses);
                break;

            case EnumDeclarationSyntax @enum:
                Add(tokens, @enum.AttributeLists);
                Add(tokens, @enum.Modifiers);
                Add(tokens, @enum.EnumKeyword);
                Add(tokens, @enum.Identifier);
                Add(tokens, @enum.BaseList);
                break;

            case DelegateDeclarationSyntax @delegate:
                Add(tokens, @delegate.AttributeLists);
                Add(tokens, @delegate.Modifiers);
                Add(tokens, @delegate.DelegateKeyword);
                Add(tokens, @delegate.ReturnType);
                Add(tokens, @delegate.Identifier);
                Add(tokens, @delegate.TypeParameterList);
                Add(tokens, @delegate.ParameterList);
                Add(tokens, @delegate.ConstraintClauses);
                break;

            case VariableDeclaratorSyntax variable when symbol is IFieldSymbol or IEventSymbol:
                AddField(tokens, variable, symbol);
                break;

            case EnumMemberDeclarationSyntax member:
                Add(tokens, member.AttributeLists);
                Add(tokens, member.Identifier);
                Add(tokens, member.EqualsValue);
                break;

            case BaseMethodDeclarationSyntax method:
                AddExcluding(tokens, method, method.Body, method.ExpressionBody, (method as ConstructorDeclarationSyntax)?.Initializer);
                break;

            case AccessorDeclarationSyntax accessor:
                AddExcluding(tokens, accessor, accessor.Body, accessor.ExpressionBody);
                break;

            case PropertyDeclarationSyntax property:
                AddProperty(tokens, property, property.ExpressionBody, property.Initializer);
                break;

            case IndexerDeclarationSyntax indexer:
                AddProperty(tokens, indexer, indexer.ExpressionBody);
                break;

            case EventDeclarationSyntax @event:
                AddProperty(tokens, @event);
                break;

            default:
                // Unknown declaration shapes fail conservative: hash their tokens. Synthetic lambdas never
                // enter here (they are created separately with an explicitly empty surface).
                Add(tokens, node);
                break;
        }

        return HashTokens(tokens);
    }

    public static string Tokens(SyntaxNode node) => HashTokens(node.DescendantTokens(descendIntoTrivia: false));

    public static string CanonicalItems(IEnumerable<string> items) => Rig.Domain.ProjectContentHash.Compute(items);

    private static void AddField(List<SyntaxToken> tokens, VariableDeclaratorSyntax variable, ISymbol symbol)
    {
        var declaration = variable.FirstAncestorOrSelf<BaseFieldDeclarationSyntax>();
        if (declaration is null)
        {
            Add(tokens, variable.Identifier);
            if (symbol is IFieldSymbol { IsConst: true })
            {
                Add(tokens, variable.Initializer);
            }
            return;
        }

        Add(tokens, declaration.AttributeLists);
        Add(tokens, declaration.Modifiers);
        if (declaration is EventFieldDeclarationSyntax eventField)
        {
            Add(tokens, eventField.EventKeyword);
        }
        Add(tokens, declaration.Declaration.Type);
        Add(tokens, variable.Identifier);
        Add(tokens, variable.ArgumentList);
        if (symbol is IFieldSymbol { IsConst: true })
        {
            Add(tokens, variable.Initializer);
        }
    }

    private static void AddProperty(List<SyntaxToken> tokens, BasePropertyDeclarationSyntax property, params SyntaxNode?[] extra)
    {
        var exclusions = new List<SyntaxNode?>(extra);
        foreach (var accessor in property.AccessorList?.Accessors ?? default)
        {
            exclusions.Add(accessor.Body);
            exclusions.Add(accessor.ExpressionBody);
        }
        AddExcluding(tokens, property, exclusions.ToArray());
    }

    private static void AddExcluding(List<SyntaxToken> tokens, SyntaxNode node, params SyntaxNode?[] excluded)
    {
        var spans = excluded.Where(n => n is not null).Select(n => n!.FullSpan).ToArray();
        foreach (var token in node.DescendantTokens(descendIntoTrivia: false))
        {
            if (!spans.Any(span => span.Contains(token.Span)))
            {
                tokens.Add(token);
            }
        }
    }

    private static string HashTokens(IEnumerable<SyntaxToken> tokens)
    {
        var canonical = new StringBuilder();
        foreach (var token in tokens)
        {
            canonical.Append(token.Text);
            canonical.Append(TokenSeparator);
        }
        if (canonical.Length == 0)
        {
            return "";
        }

        Span<byte> hash = stackalloc byte[8];
        XxHash3.Hash(Encoding.UTF8.GetBytes(canonical.ToString()), hash);
        return Convert.ToHexStringLower(hash);
    }

    private static void Add(List<SyntaxToken> destination, SyntaxNode? node)
    {
        if (node is not null)
        {
            destination.AddRange(node.DescendantTokens(descendIntoTrivia: false));
        }
    }

    private static void Add<TNode>(List<SyntaxToken> destination, SyntaxList<TNode> nodes)
        where TNode : SyntaxNode
    {
        foreach (var node in nodes)
        {
            Add(destination, node);
        }
    }

    private static void Add(List<SyntaxToken> destination, SyntaxTokenList tokens) => destination.AddRange(tokens);

    private static void Add(List<SyntaxToken> destination, SyntaxToken token)
    {
        if (token.RawKind != 0)
        {
            destination.Add(token);
        }
    }
}
