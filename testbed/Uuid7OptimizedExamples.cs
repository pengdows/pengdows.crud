using System.Diagnostics;
using pengdows.crud;

/// <summary>
/// Usage examples and performance testing
/// </summary>
public static class Uuid7OptimizedExamples
{
    public static void DemoUsage()
    {
        Console.WriteLine("=== Optimized UUIDv7 Generator Demo ===\n");

        // Generate some UUIDs
        for (int i = 0; i < 10; i++)
        {
            var uuid = Uuid7Optimized.NewUuid7();
            Console.WriteLine($"{i + 1:D2}: {uuid}");
        }

        // Show thread state
        var (lastMs, counter, bufferIndex) = Uuid7Optimized.GetThreadState();
        Console.WriteLine("\nThread State:");
        Console.WriteLine($"  Last MS: {lastMs}");
        Console.WriteLine($"  Counter: {counter}");
        Console.WriteLine($"  Buffer Index: {bufferIndex}");
        Console.WriteLine($"  Global Epoch: {Uuid7Optimized.GetGlobalEpoch()}");

        // Test ordering
        TestOrdering();
    }

    private static void TestOrdering()
    {
        Console.WriteLine("\n=== Ordering Test ===");
        
        var uuids = new Guid[100];
        for (int i = 0; i < uuids.Length; i++)
        {
            uuids[i] = Uuid7Optimized.NewUuid7();
        }

        // Verify ordering
        bool isOrdered = true;
        for (int i = 1; i < uuids.Length; i++)
        {
            if (uuids[i].CompareTo(uuids[i - 1]) <= 0)
            {
                isOrdered = false;
                Console.WriteLine($"Ordering violation at index {i}");
                break;
            }
        }

        Console.WriteLine($"Generated {uuids.Length} UUIDs");
        Console.WriteLine($"Ordering: {(isOrdered ? "✓ PASS" : "✗ FAIL")}");
    }

    public static void BenchmarkPerformance(int iterations = 1_000_000)
    {
        Console.WriteLine($"\n=== Performance Benchmark ({iterations:N0} iterations) ===");

        // Warmup
        for (int i = 0; i < 10_000; i++)
        {
            Uuid7Optimized.NewUuid7();
        }

        var sw = Stopwatch.StartNew();
        
        for (int i = 0; i < iterations; i++)
        {
            Uuid7Optimized.NewUuid7();
        }
        
        sw.Stop();

        var totalMs = sw.ElapsedMilliseconds;
        var uuidsPerSecond = iterations * 1000.0 / totalMs;
        var nsPerUuid = (double)sw.ElapsedTicks / iterations * 1_000_000_000.0 / Stopwatch.Frequency;

        Console.WriteLine($"Time: {totalMs:N0} ms");
        Console.WriteLine($"Rate: {uuidsPerSecond:N0} UUIDs/second");
        Console.WriteLine($"Cost: {nsPerUuid:F0} ns/UUID");
        
        // Thread state after benchmark
        var (lastMs, counter, bufferIndex) = Uuid7Optimized.GetThreadState();
        Console.WriteLine($"Final counter value: {counter}");
        Console.WriteLine($"Random buffer usage: {bufferIndex}/1024 bytes");
    }

    public static void TestMultiThreaded(int threadCount = 4, int uuidsPerThread = 100_000)
    {
        Console.WriteLine($"\n=== Multi-threaded Test ({threadCount} threads, {uuidsPerThread:N0} UUIDs each) ===");

        var allUuids = new Guid[threadCount * uuidsPerThread];
        var tasks = new Task[threadCount];
        
        var sw = Stopwatch.StartNew();

        for (int t = 0; t < threadCount; t++)
        {
            int threadIndex = t;
            tasks[t] = Task.Run(() =>
            {
                var startIndex = threadIndex * uuidsPerThread;
                for (int i = 0; i < uuidsPerThread; i++)
                {
                    allUuids[startIndex + i] = Uuid7Optimized.NewUuid7();
                }
            });
        }

        Task.WaitAll(tasks);
        sw.Stop();

        // Check for duplicates - optimized version
        var totalGenerated = threadCount * uuidsPerThread;
        var uniqueSet = new HashSet<Guid>(totalGenerated);
        foreach (var guid in allUuids)
        {
            uniqueSet.Add(guid);
        }
        var uniqueCount = uniqueSet.Count;
        
        Console.WriteLine($"Time: {sw.ElapsedMilliseconds:N0} ms");
        Console.WriteLine($"Total UUIDs: {totalGenerated:N0}");
        Console.WriteLine($"Unique UUIDs: {uniqueCount:N0}");
        Console.WriteLine($"Collisions: {totalGenerated - uniqueCount}");
        Console.WriteLine($"Rate: {totalGenerated * 1000.0 / sw.ElapsedMilliseconds:N0} UUIDs/second");
        Console.WriteLine($"Success: {(uniqueCount == totalGenerated ? "✓ PASS" : "✗ FAIL")}");
    }

    public static void TestTryNewUuid7()
    {
        Console.WriteLine("\n=== TryNewUuid7 No-Wait Test ===");

        int successCount = 0;
        int failCount = 0;
        var results = new List<Guid>(10_000);

        // Generate many UUIDs quickly to potentially hit counter limit
        for (int i = 0; i < 10000; i++)
        {
            if (Uuid7Optimized.TryNewUuid7(out var result))
            {
                successCount++;
                results.Add(result);
            }
            else
            {
                failCount++;
                // In real code, you might back off here
                Thread.Yield();
                
                // Retry after yield
                if (Uuid7Optimized.TryNewUuid7(out result))
                {
                    successCount++;
                    results.Add(result);
                }
            }
        }

        Console.WriteLine($"Successful generations: {successCount}");
        Console.WriteLine($"Failed attempts (counter exhausted): {failCount}");
        Console.WriteLine($"Unique UUIDs: {results.Distinct().Count()}");
        Console.WriteLine($"Success rate: {(double)successCount / (successCount + failCount) * 100:F1}%");
    }

    public static void TestByteGeneration()
    {
        Console.WriteLine("\n=== Byte Generation Test ===");

        Span<byte> buffer = stackalloc byte[16];
        
        // Test direct byte generation
        Uuid7Optimized.NewUuid7Bytes(buffer);
        
        // Convert to Guid for display
        var guid = new Guid(buffer);
        Console.WriteLine($"Generated via bytes: {guid}");
        
        // Compare with regular generation
        var directGuid = Uuid7Optimized.NewUuid7();
        Console.WriteLine($"Generated directly: {directGuid}");
        
        // Test RFC byte generation
        Span<byte> rfcBuffer = stackalloc byte[16];
        Uuid7Optimized.NewUuid7RfcBytes(rfcBuffer);
        var rfcGuid = new Guid(rfcBuffer);
        Console.WriteLine($"Generated RFC bytes: {rfcGuid}");
        
        Console.WriteLine("All should be valid UUIDv7 format");
    }

    public static void TestBitCompliance()
    {
        Console.WriteLine("\n=== Bit Compliance Test ===");

        var testCount = 1000;
        var versionFailures = 0;
        var variantFailures = 0;

        for (int i = 0; i < testCount; i++)
        {
            var guid = Uuid7Optimized.NewUuid7();
            var bytes = guid.ToByteArray();
            
            // Check version: upper nibble of byte 7 should be 0x70 (version 7)
            var versionBits = bytes[7] & 0xF0;
            if (versionBits != 0x70)
            {
                versionFailures++;
                if (versionFailures <= 3) // Only report first few failures
                {
                    Console.WriteLine($"Version failure #{versionFailures}: expected 0x70, got 0x{versionBits:X2} in {guid}");
                }
            }
            
            // Check variant: upper 2 bits of byte 8 should be 0x80 (10xxxxxx)
            var variantBits = bytes[8] & 0xC0;
            if (variantBits != 0x80)
            {
                variantFailures++;
                if (variantFailures <= 3) // Only report first few failures
                {
                    Console.WriteLine($"Variant failure #{variantFailures}: expected 0x80, got 0x{variantBits:X2} in {guid}");
                }
            }
        }

        Console.WriteLine($"Tested {testCount} UUIDs");
        Console.WriteLine($"Version compliance: {testCount - versionFailures}/{testCount} ({(double)(testCount - versionFailures)/testCount*100:F1}%)");
        Console.WriteLine($"Variant compliance: {testCount - variantFailures}/{testCount} ({(double)(testCount - variantFailures)/testCount*100:F1}%)");
        Console.WriteLine($"Overall: {(versionFailures == 0 && variantFailures == 0 ? "✓ PASS" : "✗ FAIL")}");
    }

    public static void TestMonotonicityUnderChurn()
    {
        Console.WriteLine("\n=== Monotonicity Under Churn Test ===");

        const int threadCount = 8;
        const int uuidsPerThread = 50_000;
        var totalUuids = threadCount * uuidsPerThread;
        
        var allUuids = new Guid[totalUuids];
        var tasks = new Task[threadCount];
        var sw = Stopwatch.StartNew();

        // Generate UUIDs under high concurrency
        for (int t = 0; t < threadCount; t++)
        {
            int threadIndex = t;
            tasks[t] = Task.Run(() =>
            {
                var startIndex = threadIndex * uuidsPerThread;
                for (int i = 0; i < uuidsPerThread; i++)
                {
                    allUuids[startIndex + i] = Uuid7Optimized.NewUuid7();
                }
            });
        }

        Task.WaitAll(tasks);
        sw.Stop();

        // Sort and check monotonicity
        var sortedUuids = allUuids.OrderBy(g => g).ToArray();
        
        var violations = 0;
        for (int i = 1; i < sortedUuids.Length; i++)
        {
            if (sortedUuids[i].CompareTo(sortedUuids[i - 1]) <= 0)
            {
                violations++;
                if (violations <= 3)
                {
                    Console.WriteLine($"Monotonicity violation #{violations} at index {i}");
                }
            }
        }

        Console.WriteLine($"Generated {totalUuids:N0} UUIDs in {sw.ElapsedMilliseconds:N0}ms");
        Console.WriteLine($"Rate: {totalUuids * 1000.0 / sw.ElapsedMilliseconds:N0} UUIDs/second");
        Console.WriteLine($"Monotonicity violations: {violations}");
        Console.WriteLine($"Monotonicity: {(violations == 0 ? "✓ PASS" : "✗ FAIL")}");
    }
}