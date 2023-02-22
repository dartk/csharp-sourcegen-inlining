using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using CSharp.SourceGen.Inlining.Attributes;
using static Methods;


BenchmarkRunner.Run<Benchmarks>();


[MemoryDiagnoser]
public partial class Benchmarks
{
    [Params(10, 1_000, 100_000)]
    public int N { get; set; }


    [GlobalSetup]
    public void GlobalSetup()
    {
        this._array = new int[N];
        for (var i = 0; i < N; ++i)
        {
            this._array[i] = i;
        }
    }


    [Benchmark(Baseline = true)]
    public void Inlined()
    {
        var result = CalculateSum_Inlined(this._array);
        Assert.Equal((this.N - 1) * this.N / 2, result);
    }


    [Benchmark]
    public void Original()
    {
        var result = CalculateSum_Original(this._array);
        Assert.Equal((this.N - 1) * this.N / 2, result);
    }


    private int[] _array = null!;


    [GenerateInlined(nameof(CalculateSum_Inlined))]
    public static int CalculateSum_Original(Span<int> values)
    {
        var count = 0;
        values.ForEach([Inline](x) => { count += x; });
        return count;
    }
}


public static class Methods
{
    [SupportsInlining("""
    foreach (var {action.arg0} in @this)
    {
        {action.body}
    }
    """)]
    public static void ForEach<T>(this Span<T> @this, Action<T> action)
    {
        foreach (var item in @this)
        {
            action(item);
        }
    }
}


public static class Assert
{
    public static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new AssertionException(expected, actual);
        }
    }
}


public class AssertionException : Exception
{
    public AssertionException(object? expected, object? actual)
        : base(
            "Assert Failed" + Environment.NewLine +
            "Expected: " + (expected ?? "null") + Environment.NewLine +
            "Actual: " + (actual ?? "null"))
    {
    }
}