namespace CSharp.SourceGen.Inlining.Attributes;


[AttributeUsage(AttributeTargets.Method)]
internal class SupportsInliningAttribute : Attribute
{
    public SupportsInliningAttribute(string template)
    {
    }
}