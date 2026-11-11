using System.Diagnostics;
using System.Text.Json;
using DiskSpaceAnalyzer.Core;

Localization.SetLanguage("ko", false);
var checks = new List<string>();
void Check(bool condition, string description) { if (!condition) throw new Exception(description); checks.Add(description); }
var index = new DirectoryIndex(@"C:\fixture");
var a = index.GetOrCreate(@"C:\fixture\A");
var b = index.GetOrCreate(@"C:\fixture\A\B");
var empty = index.GetOrCreate(@"C:\fixture\empty");
a.OwnBytes = 120; a.OwnFiles = 1; b.OwnBytes = 300; b.OwnFiles = 2; b.OwnUnknown = 1;
index.Root.OwnBytes = 80; index.Root.OwnFiles = 1; index.Aggregate();
Check(index.Root.Bytes == 500 && a.Bytes == 420 && index.Root.Files == 4 && index.Root.Folders == 3 && index.Root.Unknown == 1, "정확한 재귀 용량·개수·확인 불가 집계");
Check(index.Root.Children.Count == 2 && index.Root.Children.Values.All(c => c.Parent == index.Root) && b.Parent == a, "폴더 계층과 한 단계 상위 관계 보존");
Check(DirectoryIndex.Within(@"C:\fixture\A", @"C:\fixture") && !DirectoryIndex.Within(@"C:\fixture2", @"C:\fixture"), "경로 접두사 충돌 방지");
Check(ReferenceEquals(index.GetOrCreate(@"c:\FIXTURE\a"), a), "Windows 경로 대소문자 중복 제거");
var replacement = new DirectoryIndex(@"C:\fixture\A");
replacement.Root.OwnBytes = 55; replacement.Root.OwnFiles = 1;
replacement.GetOrCreate(@"C:\fixture\A\new").OwnBytes = 25;
replacement.Aggregate(); index.ReplaceSubtree(a.Path, replacement);
Check(index.Root.Bytes == 160 && index.Root.Files == 2 && !index.Nodes.ContainsKey(b.Path) && index.Root.Folders == 3, "하위 트리 변경·삭제 후 조상 용량 동기화");
index.ReplaceSubtree(replacement.Root.Path, null);
Check(index.Root.Bytes == 80 && index.Root.Folders == 1 && index.Nodes.Count == 2, "폴더 삭제 후 오래된 노드 제거");
Check(empty.Bytes == 0 && index.Root.Children.ContainsKey(empty.Path), "빈 폴더 보존");
var added = new DirectoryIndex(@"C:\fixture\added"); added.Root.OwnFiles = 2; added.Root.OwnBytes = 720; added.Aggregate();
index.ReconcileDirectory(index.Root.Path, new DirectorySlice(200, 3, 0, [added.Root.Path]), [added]);
Check(index.Root.Bytes == 920 && index.Root.Files == 5 && index.Root.Folders == 1 && !index.Nodes.ContainsKey(empty.Path), "직계 항목 갱신: 추가·삭제·용량 변경을 조상에 반영");
index.ReconcileDirectory(added.Root.Path, new DirectorySlice(300, 1, 1, []), []);
Check(index.Root.Bytes == 500 && index.Root.Files == 4 && index.Root.Unknown == 1, "하위 폴더 증분 갱신은 전체 트리 재조회 없이 반영");

var random = new Random(710);
for (int run = 0; run < 100; run++)
{
    var sizes = Enumerable.Range(0, random.Next(1, 220)).Select(_ => (long)random.Next(0, 1_000_000)).OrderDescending().ToArray();
    double w = random.Next(1, 1600), h = random.Next(1, 1000);
    var tiles = TreemapLayout.Create(sizes, n => n, new MapRect(0, 0, w, h));
    double area = tiles.Sum(t => t.Bounds.Area), total = sizes.Sum();
    if (Math.Abs(area - w * h) > 0.001) throw new Exception("트리맵 전체 면적 누락");
    foreach (var t in tiles)
    {
        if (!double.IsFinite(t.Bounds.Area) || t.Bounds.X < -0.001 || t.Bounds.Y < -0.001 || t.Bounds.X + t.Bounds.Width > w + 0.001 || t.Bounds.Y + t.Bounds.Height > h + 0.001) throw new Exception("트리맵 경계 오류");
        if (Math.Abs(t.Bounds.Area / (w * h) - t.Item / total) > 0.00001) throw new Exception("용량 면적 비례 오류");
    }
    for (int i = 0; i < tiles.Count; i++) for (int j = i + 1; j < tiles.Count; j++)
    {
        var r = tiles[i].Bounds; var s = tiles[j].Bounds;
        if (Math.Min(r.X + r.Width, s.X + s.Width) - Math.Max(r.X, s.X) > 0.0001 && Math.Min(r.Y + r.Height, s.Y + s.Height) - Math.Max(r.Y, s.Y) > 0.0001) throw new Exception("트리맵 영역 중복");
    }
}
checks.Add("무작위 트리맵 100개: 면적 보존·비례·경계·중복 검사");
Check(TreemapLayout.Create(new long[] { 0, 0 }, n => n, new MapRect(0, 0, 100, 100)).Count == 0, "0 B 항목 면적 처리");
var tinyEntries = Enumerable.Range(0, 100_000).Select(i => new ViewEntry($"f{i}", $@"C:\fixture\f{i}", 1, false)).ToArray();
var grouped = TreemapGrouping.Create(tinyEntries, new MapRect(0, 0, 1000, 800), @"C:\fixture");
Check(grouped.Count == 1 && grouped[0].IsGroup && grouped[0].Bytes == 100_000 && grouped[0].GroupCount == 100_000, "픽셀보다 작은 10만 항목 묶음의 개수·용량 보존");
var budgeted = TreemapGrouping.Create(tinyEntries, new MapRect(0, 0, 10000, 10000), @"C:\fixture");
Check(budgeted.Count <= TreemapGrouping.MaxTilesPerLevel && budgeted.Sum(e => e.Bytes) == 100_000 && budgeted.Sum(e => e.IsGroup ? e.GroupCount : 1) == 100_000, "렌더링 예산 적용 후에도 원래 개수·용량 보존");
Check(TreemapGrouping.Create(tinyEntries.Take(2), new MapRect(0, 0, 1000, 800), @"C:\fixture").All(e => !e.IsGroup), "충분히 큰 항목은 개별 표시");

var watch = Stopwatch.StartNew();
var large = new DirectoryIndex(@"C:\large");
var paths = Enumerable.Range(0, 50_000).Select(i => $@"C:\large\group{i % 200}\folder{i}").ToArray();
for (int i = 0; i < 1_820_000; i++)
{
    var node = large.GetOrCreate(paths[i % paths.Length]); node.OwnBytes += 4096; node.OwnFiles++;
}
large.Aggregate();
double aggregationSeconds = watch.Elapsed.TotalSeconds;
Check(large.Root.Files == 1_820_000 && large.Root.Bytes == 1_820_000L * 4096 && large.Root.Folders == 50_200, "182만 항목 합성 집계의 정확성");
var report = new { Passed = true, Checks = checks, Synthetic = new { Entries = 1_820_000, Folders = large.Root.Folders, AggregationSeconds = aggregationSeconds, ManagedMB = GC.GetTotalMemory(false) / 1048576.0 }, Note = "합성 집계 성능이며 Everything 최초 인덱싱·IPC 전송·UI 시간은 포함하지 않음" };
Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
