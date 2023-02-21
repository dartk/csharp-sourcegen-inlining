using CSharp.SourceGen.Inlining.Attributes;


namespace CSharp.SourceGen.Inlining.Benchmarks;


public static class Methods
{
    [SupportsInlining("""
        foreach (var {name0} in arg)
        {
            {body}
        }
    """)]
    public static void ForEach<T>(Span<T> arg, Action<T> action)
    {
        foreach (var item in arg)
        {
            action(item);
        }
    }
    
}