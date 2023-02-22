#nullable  enable
namespace CSharp.SourceGen.Inlining.Attributes;


[AttributeUsage(AttributeTargets.Method)]
internal class GenerateInlinedAttribute : Attribute
{
    public GenerateInlinedAttribute(string? name = null)
    {
    }
}