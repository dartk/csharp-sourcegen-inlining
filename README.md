# CSharp.SourceGen.Inlining

C# source generator for inlining lambdas.


## How to use

Add support for inlining to a method by adding an attribute `[SupportsInlining]` with template that will be rendered for inlined version.

```c#
[SupportsInlining("""
foreach (var {action.arg0} in span)
{
    {action.body}
}
""")]
public static void InlinableForEach<T>(Span<T> span, Action<T> action)
{
    foreach (var item in span)
    {
        action(item);
    }
}
```

`[SupportsInlining]` attribute template will expand following values:
* `{action.arg0}` - name of the first argument of the `action` lambda
* `{action.arg0.type}` - type of the first argument of the `action` lambda
* `{action.body}` - body of the `action` lambda

To inline the method above make an outer type partial, use an attribute `[GenerateInlined]` on an invoking method and use an attribute `[Inline]` on a lambda argument.

```c#
public partial class Example
{
    [GenerateInlined(nameof(CalculateSum_Inlined))]
    public static int CalculateSum_Original(Span<int> values)
    {
        var count = 0;
        InlinableForEach(values, [Inline](x) => { count += x; });
        return count;
    }
}
```

From `CalculateSum_Original` the generator will create a method `CalculateSum_Inlined` with inlined lambda body.

> **Info**: Using `nameof()` instead of a string literal as an argument for `[GenerateInlined]` attribute will allow you to perform common IDE actions, such as going to definition and renaming the generated method.

```c#
#define SOURCEGEN

public partial class Example
{
    public static int CalculateSum_Inlined(Span<int> values)
    {
        var count = 0;

        {
            var span = values;
            foreach (var x in span)
            {
                {
                    count += x;
                }
            }
        }

        return count;
    }
}
```

## Limitations

`[SupportsInlining]` method requirements:

- Method must be static.
 
- For extensions methods receiver argument must be named `@this`.
 
    This will generate an invalid code:
 
    ```c#
    [SupportsInlining("""
    foreach (var {action.arg0} in span)
    {
        {action.body}
    }
    """)]
    public static void ForEach<T>(this Span<T> span, Action<T> action)  // receiver 'span'
    {
        foreach (var item in span)
        {
            action(item);
        }
    }
    ```

    Correct version:

    ```c#
    [SupportsInlining("""
    foreach (var {action.arg0} in @this)
    {
        {action.body}
    }
    """)]
    public static void ForEach<T>(this Span<T> @this, Action<T> action)  // receiver '@this'
    {
        foreach (var item in @this)
        {
            action(item);
        }
    }
    ```

- Extension methods can only be called as an extension.

    This will generate an invalid code:

    ```c#
    [GenerateInlined(nameof(Sum_Inlined))]
    public static int Sum_Original(Span<int> values)
    {
        var count = 0;
        ForEach(values, [Inline](x) => { count += x; });  // as a regular method
        return count;
    }
    ```

    Correct version:

    ```c#
    [GenerateInlined(nameof(Sum_Inlined))]
    public static int Sum_Original(Span<int> values)
    {
        var count = 0;
        values.ForEach([Inline](x) => { count += x; });  // as an extension
        return count;
    }
    ```


## Motivation

Using lambdas in C# provide a lot of convenience but at the same time it prevents compiler to perform certain optimizations and can lead to unnecessary memory allocations and garbage collection. In many cases an inlined version of a method is going to be significantly faster than the one using a lambda.

For the sum calculation example above the inlined version is 3x faster than the original on top of the original method allocating memory on the heap:

|   Method |      N |           Mean |       Error |      StdDev | Ratio | RatioSD |   Gen0 | Allocated | Alloc Ratio |
|--------- |------- |---------------:|------------:|------------:|------:|--------:|-------:|----------:|------------:|
|  Inlined |     10 |       5.495 ns |   0.1335 ns |   0.1537 ns |  1.00 |    0.00 |      - |         - |          NA |
| Original |     10 |      26.509 ns |   0.0727 ns |   0.0645 ns |  4.77 |    0.12 | 0.0105 |      88 B |          NA |
|          |        |                |             |             |       |         |        |           |             |
|  Inlined |   1000 |     250.785 ns |   1.1302 ns |   1.0571 ns |  1.00 |    0.00 |      - |         - |          NA |
| Original |   1000 |   1,448.734 ns |   2.1895 ns |   1.7094 ns |  5.78 |    0.03 | 0.0095 |      88 B |          NA |
|          |        |                |             |             |       |         |        |           |             |
|  Inlined | 100000 |  23,898.808 ns |  51.7429 ns |  45.8687 ns |  1.00 |    0.00 |      - |         - |          NA |
| Original | 100000 | 167,166.113 ns | 267.8199 ns | 237.4154 ns |  6.99 |    0.02 |      - |      88 B |          NA |

<details>
<summary>Benchmark code</summary>

```c#
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using CSharp.SourceGen.Inlining.Attributes;


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
        var result = Sum_Inlined(this._array);
        Assert.Equal((this.N - 1) * this.N / 2, result);
    }


    [Benchmark]
    public void Original()
    {
        var result = Sum_Original(this._array);
        Assert.Equal((this.N - 1) * this.N / 2, result);
    }
    
    
    [GenerateInlined(nameof(Sum_Inlined))]
    public static int Sum_Original(Span<int> span)
    {
        var count = 0;
        span.ForEach([Inline](x) => { count += x; });
        return count;
    }


    private int[] _array = null!;
}


public static class SpanExtensions
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
```

</details>