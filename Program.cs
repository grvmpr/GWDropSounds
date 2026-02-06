using System.IO;
using System.Media;
using System.Text;
using System.Text.RegularExpressions;
//using System;
//using System.Collections.Generic;
//using NAudio.Wave;
//using NAudio.Wave.SampleProviders;
using Microsoft.Extensions.Configuration;

AppConfig _cfg = new();

//Dictionary<string, DateTimeOffset> _plays = new();

var debounceMs = 10;
DateTime last = DateTime.MinValue;
object gate = new();

Console.OutputEncoding = Encoding.UTF8;

        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .Build();

        _cfg = configuration.Get<AppConfig>() ?? throw new Exception("Failed to load config.");
        
var matchPath = _cfg.RulesFile;
var csvPath = Helper.ResolveDailyCsvPath(_cfg.InputDirectory); // Path.Combine(_cfg.InputDirectory, "2026-02-04_drops.csv");

var rules = Helper.LoadRules(matchPath);

var lines = Helper.GetNewLines(csvPath);

using var matchWatcher = new FileSystemWatcher(Path.GetDirectoryName(matchPath)!, Path.GetFileName(matchPath))
{
    EnableRaisingEvents = true,
    NotifyFilter = NotifyFilters.LastWrite //| NotifyFilters.Size | NotifyFilters.FileName
};

matchWatcher.Changed += (_, __) =>
{
    try
    {
        lock (gate)
        {
            var now = DateTime.UtcNow;
            if ((now - last).TotalMilliseconds < debounceMs) return;
            last = now;
        }

        var matches = Helper.LoadRules(matchPath);

        Console.WriteLine("Match file changed");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[MatchConfig reload failed] {ex.Message}");
    }
};



using var csvWatcher = new FileSystemWatcher(Path.GetDirectoryName(csvPath)!, Path.GetFileName(csvPath))
{
    EnableRaisingEvents = true,
    NotifyFilter = NotifyFilters.LastWrite // | NotifyFilters.Size | NotifyFilters.FileName
};

csvWatcher.Changed += (_, __) =>
{
    try
    {
        lock (gate)
        {
            var now = DateTime.UtcNow;
            if ((now - last).TotalMilliseconds < debounceMs) return;
            last = now;
        }

        var lines = Helper.GetNewLines(csvPath);
        var matches = Helper.GetMatches(lines, rules);
        var distinctMatches = matches
            .OrderByDescending(m => m.Priority)
            .DistinctBy(m => m.Sound)
            .ToList();
            
        foreach (var match in distinctMatches)
        {
            Helper.PlayOverlapped(match.Sound);
            System.Threading.Thread.Sleep(50); // slight delay to allow overlap; adjust as needed
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[MatchConfig reload failed] {ex.Message}");
    }
};

Console.ReadLine();

internal sealed class AppConfig
{
    public string InputDirectory { get; set; } = "";
    public string RulesFile { get; set; } = "";

}
public class Helper
{
    private static long _offsetBytes = 0;

public static string ResolveDailyCsvPath(string inputDirectory)
    {
        var today = DateTime.Now.Date;
        var yesterday = today.AddDays(-1);

        string todayName = $"{today:yyyy-MM-dd}_drops.csv";
        string ydayName = $"{yesterday:yyyy-MM-dd}_drops.csv";

        string todayPath = Path.Combine(inputDirectory, todayName);
        string ydayPath = Path.Combine(inputDirectory, ydayName);

        if (File.Exists(todayPath)) return todayPath;

        if (File.Exists(ydayPath))
        {
            Console.WriteLine($"[{DateTime.Now}] Today's file not found. Using yesterday's: {ydayPath}");
            return ydayPath;
        }

        Directory.CreateDirectory(inputDirectory);

        if (!File.Exists(todayPath))
        {
            using (File.Create(todayPath)) { }
            Console.WriteLine($"[{DateTime.Now}] Neither today's nor yesterday's file exists. Created: {todayPath}. Restart your GW client to start logging drops.");
        }

        return todayPath;
    }

    public static List<Item> GetNewLines(string path)
    {
        var fi = new FileInfo(path);
        //if (!fi.Exists) yield break;
        if (fi.Length < _offsetBytes)
        {
            Console.WriteLine($"fi.Length {fi.Length} < _offsetBytes {_offsetBytes}");
            _offsetBytes = 0;
        }

        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        fs.Seek(_offsetBytes, SeekOrigin.Begin);

        using var reader = new StreamReader(fs, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 4096, leaveOpen: true);
        var text = reader.ReadToEnd();

        _offsetBytes = fs.Position;

        var lines = text.Split("\r\n");

        var items = new List<Item>();

        foreach (var line in lines)
        {
            var cols = line.Split(",");
            if (cols.Count() > 8)
            {
                var itemName = cols[3];
                var itemType = cols[6];
                var rarity = cols[7];

                items.Add(new Item
                {
                    ItemName = itemName,
                    ItemType = itemType,
                    Rarity = rarity,
                });
            }
        }

        return items;
    }

    public static List<Match> GetMatches(List<Item> items, List<Match> rules)
    {
        var matches = new List<Match>();

        foreach (var item in items)
        {
            var added = false;
            foreach (var rule in rules)
            {
                bool isMatch =
                    (rule.ItemName == null || rule.ItemName.Length == 0 || rule.ItemName.Contains(item.ItemName)) &&
                    (rule.ItemType == null || rule.ItemType.Length == 0 || rule.ItemType.Contains(item.ItemType)) &&
                    (rule.Rarity == null || rule.Rarity.Length == 0 || rule.Rarity.Contains(item.Rarity));

                if (isMatch)
                {
                    Console.WriteLine($"[{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")}] Matched rule: {rule.Sound} for item {item.ItemName} ({item.ItemType}, {item.Rarity})");
                }

                if (isMatch && !added)
                {
                    matches.Add(rule);
                    added = true;
                }
            }
        }

        return matches;
    }

    public static List<Match> GetMatches2(List<Item> items, List<Match> rules)
    {
        var matches = new List<Match>();

        foreach (var item in items)
        {
            bool added = false;
            foreach (var rule in rules)
            {
                //var matchName = 


                bool isMatch =
                    (rule.ItemName?.Contains(item.ItemName) ?? false) ||
                    (rule.ItemType?.Contains(item.ItemType) ?? false) ||
                    (rule.Rarity?.Contains(item.Rarity) ?? false);

                if (isMatch && added == false)
                {
                    matches.Add(rule);
                    added = true;
                }
            }
        }

        return matches;
    }

    public static List<Match> GetMatches3(List<Item> items, List<Match> rules)
    {
        var matches = new List<Match>();


        foreach (var item in items)
        {
            var isMatch = false;
            foreach (var rule in rules)
            {
                if (isMatch == false)
                {
                    var nameMatch = rule.ItemName == null ? -1 : 0;
                    var typeMatch = rule.ItemType == null ? -1 : 0;
                    var rarityMatch = rule.Rarity == null ? -1 : 0;


                    if (rule.ItemName != null)
                    {
                        foreach (var col in rule.ItemName)
                        {
                            isMatch = item.ItemName == col;
                        }
                    }
                    
                        
                    foreach (var col in rule.ItemName)
                    {
                        nameMatch = item.ItemName == col ? 1: 0;
                    }
                    foreach (var col in rule.ItemType)
                    {
                        typeMatch = item.ItemType == col ? 1 : 0;
                    }
                    foreach (var col in rule.Rarity)
                    {
                        rarityMatch = item.Rarity == col ? 1 : 0;
                    }

                    //var m = new int[nameMatch, typeMatch, rarityMatch];

                    isMatch = nameMatch != 0 && typeMatch != 0 && rarityMatch != 0;

                    if (isMatch == true)
                    {
                        matches.Add(rule);
                    }
                }
            }
        }

        return matches;
    }

    public static void PlayOverlapped(string wavPath)
    {
        // Fire-and-forget to avoid any chance of blocking file watcher thread
        _ = Task.Run(() =>
        {
            try
            {
                if (!File.Exists(wavPath))
                {
                    Console.WriteLine($"[Sound missing] {wavPath}");
                    return;
                }

                // Create a NEW player each time so sounds can overlap
                using var player = new SoundPlayer(wavPath);
                player.Load();   // load now to reduce latency
                player.Play();   // async play; overlapping occurs naturally

                Console.WriteLine($"[{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")}] Played sound {wavPath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Sound error] {ex.Message}");
            }
        });
    }

    public static List<Match> LoadRules(string filePath)
{
    var matches = new List<Match>();
    Match current = null;

    foreach (var rawLine in File.ReadLines(filePath))
    {
        var line = rawLine.Trim();
        if (string.IsNullOrWhiteSpace(line))
            continue;

        if (line.StartsWith("#"))
                continue;

        if (line == "Match")
        {
            if (current != null) {
                matches.Add(current);
            }

            current = new Match();
            continue;
        }

        if (current == null)
            continue;

        if (line.StartsWith("Sound"))
        {
            current.Sound = ExtractQuotedValues(line).FirstOrDefault();
        }
        else if (line.StartsWith("ItemName"))
        {
            current.ItemName = ExtractQuotedValues(line);
        }
        else if (line.StartsWith("Rarity"))
        {
            current.Rarity = ExtractQuotedValues(line);
        }
        else if (line.StartsWith("ItemType"))
        {
            current.ItemType = ExtractQuotedValues(line);
        }
        else if (line.StartsWith("Priority"))
        {
            current.Priority = int.TryParse(ExtractQuotedValues(line).FirstOrDefault(),out var parsed)
                ? parsed
                : 999;
        }
    }

    if (current != null) {
        matches.Add(current);
    }

    return matches;
}

private static string[] ExtractQuotedValues(string line)
    {
        return Regex.Matches(line, "\"([^\"]+)\"")
                 .Cast<System.Text.RegularExpressions.Match>()
                 .Select(m => m.Groups[1].Value)
                 .ToArray();
    }
}

public class Match
{
    public string Sound { get; set; }
    public string[] ItemName { get; set; }
    public string[] Rarity { get; set; }
    public string[] ItemType { get; set; }
    public int Priority { get; set; } = 999;
}

public class Item
{
    public string ItemName { get; set; }
    public string Rarity { get; set; }
    public string ItemType { get; set; }
}