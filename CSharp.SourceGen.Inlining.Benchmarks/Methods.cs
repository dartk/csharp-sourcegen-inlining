using CSharp.SourceGen.Inlining.Attributes;


namespace CSharp.SourceGen.Inlining.Benchmarks;


public static class Methods
{
    [SupportsInlining("""
    foreach (var {action.arg0} in span)
    {
        {action.body}
    }
    """)]
    public static void ForEach<T>(Span<T> span, Action<T> action)
    {
        foreach (var item in span)
        {
            action(item);
        }
    }
}
