using BenchmarkDotNet.Running;

namespace DynamicWhere.Benchmarks;

/// <summary>Entry point for the benchmark run.</summary>
public static class Program
{
    /// <summary>Runs every benchmark in the assembly.</summary>
    /// <param name="args">Passed through to BenchmarkDotNet's switcher.</param>
    public static void Main(string[] args) =>
        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
}
