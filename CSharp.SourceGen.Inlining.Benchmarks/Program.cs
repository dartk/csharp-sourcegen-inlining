using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using CSharp.SourceGen.Inlining.Attributes;
using CSharp.SourceGen.Inlining.Benchmarks;


BenchmarkRunner.Run<Benchmarks>();


[MemoryDiagnoser]
public partial class Benchmarks
{
    [GenerateInlined]
    public static int CalculateSum(Span<int> values)
    {
        var count = 0;
        Methods.ForEach(values, [Inline](x) => { count += x; });
        return count;
    }


    [Params(10_000, 100_000, 1_000_000)]
    public int N { get; set; }


    [GlobalSetup]
    public void GlobalSetup()
    {
        var random = new Random(0);
        this._array = new int[N];
        for (var i = 0; i < N; ++i)
        {
            this._array[i] = random.Next();
        }
    }


    [Benchmark]
    public void Regular()
    {
        CalculateSum(this._array);
    }


    [Benchmark(Baseline = true)]
    public void Inlined()
    {
        CalculateSum_inlined(this._array);
    }


    private int[] _array = null!;
}