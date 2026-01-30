using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

public class SrtEntry
{
    public int Index {get; set;}
    public string Start {get; set;} = "";
    public string End {get; set;} = "";
    public string Text {get; set;} = "";
}

public static class SrtParser
{
    public static List<SrtEntry> Parse(string path)
    {
        var lines = File.ReadAllLines(path);
        var list = new List<SrtEntry>();
        int i=0;
        while (i<lines.Length)
        {
            if (string.IsNullOrWhiteSpace(lines[i])) { i++; continue; }
            int idx = int.TryParse(lines[i].Trim(), out var tmp) ? tmp : 0;
            i++;
            if (i>=lines.Length) break;
            var time = lines[i].Trim();
            var m = Regex.Match(time, @"(?<start>[\d:,]+)\s*-->\s*(?<end>[\d:,]+)");
            var start = m.Success ? m.Groups["start"].Value : "";
            var end = m.Success ? m.Groups["end"].Value : "";
            i++;
            var sb = new StringBuilder();
            while (i<lines.Length && !string.IsNullOrWhiteSpace(lines[i]))
            {
                if (sb.Length>0) sb.AppendLine();
                sb.Append(lines[i]);
                i++;
            }
            list.Add(new SrtEntry { Index = idx, Start = start, End = end, Text = sb.ToString()});
        }
        return list;
    }

    public static void Write(string path, List<SrtEntry> entries)
    {
        using var sw = new StreamWriter(path, false, Encoding.UTF8);
        foreach (var e in entries)
        {
            sw.WriteLine(e.Index);
            sw.WriteLine($"{e.Start} --> {e.End}");
            sw.WriteLine(e.Text);
            sw.WriteLine();
        }
    }
}
