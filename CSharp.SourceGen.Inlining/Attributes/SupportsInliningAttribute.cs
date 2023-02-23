namespace CSharp.SourceGen.Inlining;


[AttributeUsage(AttributeTargets.Method)]
internal class SupportsInliningAttribute : Attribute
{
    public SupportsInliningAttribute(string template)
    {
    }
}