using System.Buffers;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;

// ─── Entry point ────────────────────────────────────────────────────────────
BenchmarkRunner.Run<ByteAllocationBenchmark>(
    DefaultConfig.Instance
        .WithSummaryStyle(SummaryStyle.Default.WithRatioStyle(RatioStyle.Trend))
        .AddJob(Job.Default.WithId("net10"))
);

// ─── Benchmark class ────────────────────────────────────────────────────────

/// <summary>
/// Compares heap allocation via <c>new byte[N]</c> against renting from
/// <see cref="ArrayPool{T}.Shared"/> for several representative buffer sizes.
///
/// Key metrics to watch
/// ─────────────────────
/// • Mean       – raw execution time per operation
/// • Allocated  – managed bytes allocated per operation (MemoryDiagnoser)
/// • Gen0/Gen1  – GC collection pressure
/// </summary>
[MemoryDiagnoser]                          // tracks Allocated / Gen0 / Gen1 / Gen2
[HideColumns(Column.StdDev, Column.RatioSD)]
[MarkdownExporterAttribute.GitHub]         // exports BenchmarkDotNet-report.md
[CsvMeasurementsExporter]
//[SimpleJob(RuntimeMoniker.Net10_0)]
public class ByteAllocationBenchmark
{
    // ── Parameters ────────────────────────────────────────────────────────
    [Params(64, 256, 1024, 4096, 16_384, 65_536)]
    public int BufferSize { get; set; }

    private ArrayPool<byte> _pool = null!;

    [GlobalSetup]
    public void Setup() => _pool = ArrayPool<byte>.Shared;

    // ── Benchmarks ────────────────────────────────────────────────────────

    /// <summary>
    /// Baseline: allocates a brand-new array on the managed heap every call.
    /// The GC must collect it eventually → allocation cost + GC pressure.
    /// </summary>
    [Benchmark(Baseline = true, Description = "new byte[N]")]
    public byte[] NewArray()
    {
        var buffer = new byte[BufferSize];
        // Simulate minimal work so the JIT cannot dead-code-eliminate the array.
        buffer[0] = 1;
        return buffer;
    }

    /// <summary>
    /// Rents a buffer from <see cref="ArrayPool{T}.Shared"/>.
    /// The returned array may be <em>larger</em> than requested (next power of two).
    /// After use the caller must return it; no GC allocation in the steady state.
    /// </summary>
    [Benchmark(Description = "ArrayPool.Rent/Return")]
    public void ArrayPoolRentReturn()
    {
        byte[] buffer = _pool.Rent(BufferSize);
        try
        {
            buffer[0] = 1;          // same minimal work
        }
        finally
        {
            _pool.Return(buffer, clearArray: false);
        }
    }

    /// <summary>
    /// Same as above but <c>clearArray: true</c> – mirrors patterns where
    /// security/correctness requires zeroing the buffer before returning.
    /// </summary>
    [Benchmark(Description = "ArrayPool.Rent/Return (clear)")]
    public void ArrayPoolRentReturnClear()
    {
        byte[] buffer = _pool.Rent(BufferSize);
        try
        {
            buffer[0] = 1;
        }
        finally
        {
            _pool.Return(buffer, clearArray: true);
        }
    }

    /// <summary>
    /// Uses <c>stackalloc</c> for small buffers (≤ 256 B).
    /// Completely avoids both heap and pool; not applicable for large sizes.
    /// </summary>
    [Benchmark(Description = "stackalloc (≤256 B only)")]
    public void StackAllocSmall()
    {
        if (BufferSize > 256)
        {
            // For large sizes we fall back to pool so the benchmark still runs,
            // but the important comparison point is the ≤256 rows.
            byte[] buffer = _pool.Rent(BufferSize);
            buffer[0] = 1;
            _pool.Return(buffer);
            return;
        }

        Span<byte> span = stackalloc byte[BufferSize];
        span[0] = 1;
    }
}
