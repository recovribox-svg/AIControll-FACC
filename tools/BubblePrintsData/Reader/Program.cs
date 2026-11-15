using BlueprintExplorer;
using System.Text.Json;

if (args.Length != 3) throw new ArgumentException("Usage: BubblePrintsReader <database.binz> <targets.json> <output-directory>");

// Use the supplied engine's chunk/decompression reader, without its Windows UI, network manager or settings.
var targets = JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(args[1]))!;
var output = args[2];
Directory.CreateDirectory(output);
var reader = new BPFile.Reader(args[0]);
using var context = reader.CreateReadContext();
using var header = context.Open(reader.Handle.GetChunk(32).Chunks[0]);
var version = $"{header.ReadInt32()}.{header.ReadInt32()}.{header.ReadInt32()}{header.ReadString()}";
int expected = header.ReadInt32(), total = 0;
var found = new List<object>();
foreach (var bundle in reader.Handle.GetChunks(33))
{
    using var chunk = reader.CreateReadContext();
    using var data = chunk.Open(bundle.Chunks[0]);
    while(data.BaseStream.Position < data.BaseStream.Length)
    {
        string guid = data.ReadString(), name = data.ReadString(), type = data.ReadString(), raw = data.ReadString();
        total++;
        if(!targets.TryGetValue(guid,out var supplied)) continue;
        File.WriteAllText(Path.Combine(output,guid+".raw.json"),raw);
        found.Add(new { guid, name, type, supplied });
    }
}
if(total != expected) throw new InvalidDataException($"Header {expected}, read {total}");
var receipt = new { version, expected, total, requested=targets.Count, found, missing=targets.Keys.Except(found.Select(x => (string)x.GetType().GetProperty("guid")!.GetValue(x)!)).ToArray() };
File.WriteAllText(Path.Combine(output,"receipt.json"),JsonSerializer.Serialize(receipt,new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine($"Uploaded engine read {total} records for Wrath {version}; selected {found.Count}/{targets.Count}. No game/mod initialization or Windows UI.");
