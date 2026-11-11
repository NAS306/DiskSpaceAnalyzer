using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace DiskSpaceAnalyzer.Core;

internal static class Verification
{
    public static async Task<int> RunAsync(string[] args)
    {
        string output = Value(args, "--output") ?? System.IO.Path.Combine(AppContext.BaseDirectory, "verification.json");
        string root = Value(args, "--path") ?? Environment.CurrentDirectory;
        try
        {
            using var client = new EverythingClient();
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            var clock = Stopwatch.StartNew();
            await client.EnsureRunningAsync(null, cts.Token);
            double startup = clock.Elapsed.TotalSeconds; clock.Restart();
            var filter = new AnalysisFilter(args.Contains("--exclude-windows"));
            if (args.Contains("--filter-test"))
            {
                var filterTest = await VerifyFilterAsync(client, cts.Token);
                File.WriteAllText(output, JsonSerializer.Serialize(new { Passed = true, FilterTest = filterTest }, new JsonSerializerOptions { WriteIndented = true }));
                return 0;
            }
            if (args.Contains("--search-test"))
            {
                var searchTest = await VerifySearchAsync(client, System.IO.Path.GetDirectoryName(output)!, cts.Token);
                File.WriteAllText(output, JsonSerializer.Serialize(new { Passed = true, SearchTest = searchTest }, new JsonSerializerOptions { WriteIndented = true }));
                return 0;
            }
            if (Value(args, "--search") is { } query)
            {
                var result = await client.SearchAsync(query, null, cts.Token, filter);
                File.WriteAllText(output, JsonSerializer.Serialize(new { Passed = true, result.Query, filter.ExcludeWindows, Files = result.Files.Count, result.Bytes, result.Unknown, result.ExcludedFolders, Seconds = clock.Elapsed.TotalSeconds, Results = result.Files }, new JsonSerializerOptions { WriteIndented = true }));
                return 0;
            }
            var index = await client.ScanAsync(root, null, cts.Token, filter);
            double scan = clock.Elapsed.TotalSeconds;
            var files = await client.ReadFilesAsync(root, cts.Token, filter);
            object? comparison = null;
            if (args.Contains("--compare"))
            {
                var expected = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Where(p => !filter.IsExcluded(p)).Select(p => new FileInfo(p)).ToArray();
                long bytes = expected.Sum(f => f.Length);
                if (bytes != index.Root.Bytes || expected.LongLength != index.Root.Files) throw new InvalidOperationException($"집계 불일치: 실제 {expected.LongLength}/{bytes}, 인덱스 {index.Root.Files}/{index.Root.Bytes}");
                var direct = Directory.EnumerateFiles(root).Where(p => !filter.IsExcluded(p)).Select(System.IO.Path.GetFileName).Order().ToArray();
                if (!direct.SequenceEqual(files.Select(f => f.Name).Order())) throw new InvalidOperationException("직접 파일 목록 불일치");
                comparison = new { Passed = true, Files = expected.LongLength, Bytes = bytes };
            }
            object? liveTest = args.Contains("--watch-test") ? await VerifyLiveAsync(client, System.IO.Path.GetDirectoryName(output)!, cts.Token) : null;
            File.WriteAllText(output, JsonSerializer.Serialize(new
            {
                Passed = true, Root = index.Root.Path, filter.ExcludeWindows, StartupSeconds = startup, ScanSeconds = scan,
                Files = index.Root.Files, Folders = index.Root.Folders, Bytes = index.Root.Bytes, UnknownSizes = index.Root.Unknown,
                DirectFiles = files.Count, DirectoryNodes = index.Nodes.Count,
                WorkingSetMB = Process.GetCurrentProcess().WorkingSet64 / 1048576.0,
                ManagedMB = GC.GetTotalMemory(false) / 1048576.0, Comparison = comparison, LiveTest = liveTest
            }, new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }
        catch (Exception ex)
        {
            File.WriteAllText(output, JsonSerializer.Serialize(new { Passed = false, Error = ex.ToString() }, new JsonSerializerOptions { WriteIndented = true }));
            return 1;
        }
    }
    private static async Task<object> VerifySearchAsync(EverythingClient client, string outputDirectory, CancellationToken token)
    {
        string root = System.IO.Path.Combine(outputDirectory, "search-fixture-" + Guid.NewGuid().ToString("N"));
        string nested = System.IO.Path.Combine(root, "한글 자료 & (하위)");
        Directory.CreateDirectory(nested);
        File.WriteAllBytes(System.IO.Path.Combine(root, "large.zip"), new byte[4096]);
        File.WriteAllBytes(System.IO.Path.Combine(root, "small.zip"), new byte[1024]);
        File.WriteAllBytes(System.IO.Path.Combine(root, "unrelated.bin"), new byte[8192]);
        File.WriteAllBytes(System.IO.Path.Combine(root, "zero.zip"), []);
        File.WriteAllBytes(System.IO.Path.Combine(nested, "large.zip"), new byte[6144]);
        string scope = $"\"{root}\\\"";
        SearchResults result;
        var clock = Stopwatch.StartNew();
        do
        {
            await Task.Delay(500, token);
            result = await client.SearchAsync(SearchQueryBuilder.Build(new("zip", ">", "2", "KB", root)), null, token);
        } while (result.Files.Count != 2 && clock.Elapsed < TimeSpan.FromSeconds(15));
        if (result.Files.Count != 2 || result.Bytes != 10240 || result.Files.Any(f => f.Bytes <= 2048 || System.IO.Path.GetExtension(f.Path) != ".zip"))
            throw new InvalidOperationException("Everything 검색의 확장자·크기 조건 또는 합계가 일치하지 않습니다.");
        if (result.Files.Select(f => f.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != 2)
            throw new InvalidOperationException("동일한 파일명의 서로 다른 경로가 손실되었습니다.");
        var either = await client.SearchAsync($"{scope} <*.zip|*.bin> size:>2kb", null, token);
        if (either.Files.Count != 3 || either.Bytes != 18432) throw new InvalidOperationException("Everything OR 그룹 검색 결과 불일치");
        var zero = await client.SearchAsync($"{scope} *.zip size:0", null, token);
        if (zero.Files.Count != 1 || zero.Bytes != 0) throw new InvalidOperationException("0 B 검색 결과 손실");
        var none = await client.SearchAsync($"{scope} *.no-match", null, token);
        if (none.Files.Count != 0 || none.Bytes != 0) throw new InvalidOperationException("결과 없는 검색에 이전 결과가 남았습니다.");
        var folders = await client.SearchAsync($"folder: parent:\"{root}\"", null, token);
        if (folders.Files.Count != 0 || folders.ExcludedFolders != 1) throw new InvalidOperationException("폴더 결과가 전체 용량으로 포함되었습니다.");
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        bool cancelledCorrectly = false;
        try { await client.SearchAsync(scope, null, cancelled.Token); } catch (OperationCanceledException) { cancelledCorrectly = true; }
        if (!cancelledCorrectly) throw new InvalidOperationException("검색 취소 토큰 무시됨");
        var directory = await client.ScanAsync(root, null, token);
        if (directory.Root.Files != 5 || directory.Root.Bytes != 19456) throw new InvalidOperationException("검색 후 일반 폴더 스캔으로 조건이 누출되었습니다.");
        return new { Fixture = root, Files = result.Files.Count, result.Bytes, Cases = new[] { "wildcard-size", "same-name-different-path", "nested-unicode", "or-group", "zero-size", "no-results", "exclude-folders", "cancellation", "normal-scan-after-search" } };
    }
    private static async Task<object> VerifyFilterAsync(EverythingClient client, CancellationToken token)
    {
        var filter = new AnalysisFilter(true);
        var clock = Stopwatch.StartNew();
        var scan = await client.ScanAsync(@"C:\", null, token, filter);
        if (scan.Nodes.Keys.Any(p => filter.IsExcluded(p)) || scan.Root.Bytes != scan.Nodes.Values.Sum(n => n.OwnBytes) || scan.Root.Files != scan.Nodes.Values.Sum(n => n.OwnFiles))
            throw new InvalidOperationException("제외된 Windows 노드 또는 그 용량이 전체 합계에 포함되었습니다.");
        var normalRoot = await client.ReadDirectoryAsync(@"C:\", token);
        var filteredRoot = await client.ReadDirectoryAsync(@"C:\", token, filter);
        if (!normalRoot.Folders.Any(p => p.Equals(@"C:\Windows", StringComparison.OrdinalIgnoreCase)) || filteredRoot.Folders.Any(p => filter.IsExcluded(p)))
            throw new InvalidOperationException("직계 폴더/증분 갱신의 Windows 제외 규칙 불일치");
        var inside = await client.ScanAsync(@"C:\Windows\System32", null, token, filter);
        var insideFiles = await client.ReadFilesAsync(@"C:\Windows", token, filter);
        var insideSlice = await client.ReadDirectoryAsync(@"C:\Windows", token, filter);
        if (inside.Root.Bytes != 0 || inside.Root.Files != 0 || inside.Root.Folders != 0 || insideFiles.Count != 0 || insideSlice.Bytes != 0 || insideSlice.Folders.Count != 0)
            throw new InvalidOperationException("제외된 경로 직접 분석에서 데이터가 노출되었습니다.");
        const string query = "*.dll size:>10mb";
        var all = await client.SearchAsync(query, null, token);
        var filtered = await client.SearchAsync(query, null, token, filter);
        var expected = new SearchResults(query, all.Files.Where(f => !filter.IsExcluded(f.Path)).ToList(), 0);
        if (filtered.Files.Any(f => filter.IsExcluded(f.Path)) || !filtered.SameResults(expected) || filtered.Bytes != expected.Bytes)
            throw new InvalidOperationException("검색 결과 또는 검색 용량 합계의 Windows 제외 불일치");
        var includedAgain = await client.SearchAsync(query, null, token);
        if (!includedAgain.Files.Any(f => filter.IsExcluded(f.Path))) throw new InvalidOperationException("제외 옵션 해제 후 Windows 검색 결과가 복구되지 않았습니다.");
        return new { Seconds = clock.Elapsed.TotalSeconds, FilteredDriveFiles = scan.Root.Files, FilteredDriveBytes = scan.Root.Bytes, DirectoryNodes = scan.Nodes.Count, SearchFilesBefore = all.Files.Count, SearchFilesAfter = filtered.Files.Count, SearchBytesBefore = all.Bytes, SearchBytesAfter = filtered.Bytes, Cases = new[] { "drive-scan-no-windows", "aggregate-excludes-all-windows-bytes", "incremental-directory-filter", "excluded-root-empty", "excluded-direct-files-empty", "filtered-search-exact-bytes", "disable-restores-results" } };
    }
    private static async Task<object> VerifyLiveAsync(EverythingClient client, string outputDirectory, CancellationToken token)
    {
        string root = System.IO.Path.Combine(outputDirectory, "live-fixture-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(System.IO.Path.Combine(root, "한글 폴더", "nested"));
        Directory.CreateDirectory(System.IO.Path.Combine(root, "empty"));
        File.WriteAllBytes(System.IO.Path.Combine(root, "root.bin"), new byte[128]);
        File.WriteAllBytes(System.IO.Path.Combine(root, "한글 폴더", "nested", "child.bin"), new byte[512]);
        File.WriteAllBytes(System.IO.Path.Combine(root, "zero.bin"), []);
        await Task.Delay(1500, token);
        var scan = await client.ScanAsync(root, null, token);
        if (scan.Root.Files != 3 || scan.Root.Bytes != 640) throw new InvalidOperationException("실시간 검증 초기 인덱스가 아직 준비되지 않았습니다.");
        using var monitor = new LiveMonitor(root);
        if (!monitor.IsWatching) throw new InvalidOperationException("실시간 감시 시작 실패");
        var clock = Stopwatch.StartNew();
        File.WriteAllBytes(System.IO.Path.Combine(root, "new.bin"), new byte[4096]);
        // Preserve the previous file before exercising a size update, per project backup rules.
        string original = System.IO.Path.Combine(root, "root.bin");
        File.Copy(original, System.IO.Path.Combine(root, "root_BU.bin"), true);
        File.WriteAllBytes(original, new byte[1024]);
        Directory.Move(System.IO.Path.Combine(root, "한글 폴더"), System.IO.Path.Combine(root, "이름 변경"));
        // Only a file created by this isolated test is removed.
        File.Delete(System.IO.Path.Combine(root, "zero.bin"));
        bool matched = false;
        while (clock.Elapsed < TimeSpan.FromSeconds(15))
        {
            await Task.Delay(500, token);
            foreach (string dirty in monitor.Drain())
            {
                string path = dirty;
                while (!scan.Nodes.ContainsKey(path)) path = System.IO.Path.GetDirectoryName(path) ?? scan.Root.Path;
                var slice = await client.ReadDirectoryAsync(path, token);
                var additions = new List<DirectoryIndex>();
                foreach (string folder in slice.Folders) if (!scan.Nodes.ContainsKey(folder)) additions.Add(await client.ScanAsync(folder, null, token));
                scan.ReconcileDirectory(path, slice, additions);
            }
            var expected = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Select(p => new FileInfo(p)).ToArray();
            matched = expected.LongLength == scan.Root.Files && expected.Sum(f => f.Length) == scan.Root.Bytes && scan.Nodes.ContainsKey(System.IO.Path.Combine(root, "이름 변경", "nested")) && !scan.Nodes.ContainsKey(System.IO.Path.Combine(root, "한글 폴더"));
            if (matched) break;
        }
        if (!matched) throw new InvalidOperationException("생성·크기 변경·삭제·폴더 이름 변경의 실시간 집계 불일치");
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        bool cancellation = false;
        try { await client.ScanAsync(root, null, cancelled.Token); } catch (OperationCanceledException) { cancellation = true; }
        if (!cancellation) throw new InvalidOperationException("취소 토큰 무시됨");
        return new { Passed = true, Fixture = root, Seconds = clock.Elapsed.TotalSeconds, Files = scan.Root.Files, Bytes = scan.Root.Bytes, Cancellation = cancellation, Cases = new[] { "create", "resize", "delete", "rename-directory", "unicode", "empty-folder", "zero-byte" } };
    }
    private static string? Value(string[] args, string key)
    {
        int index = Array.IndexOf(args, key); return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
}
