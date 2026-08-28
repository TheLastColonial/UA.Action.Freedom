#:package YamlDotNet@16.2.1
//
// Preprocesses a published HMRC OpenAPI document before NSwag runs.
//
//   dotnet run build/nswag/PreprocessSpec.cs -- <rawSpec.yaml> <outSpec.json> [<config.json>]
//
// The published HMRC specs are RAML -> OpenAPI conversions: schemas are inlined and duplicated
// 3-5 times, enums are sometimes modelled as `oneOf` of single-value enum subschemas, and the
// operationIds / component keys carry conversion noise. Fed to NSwag as-is the worst of them
// (Goods Vehicle Movements) yields ~170 near-duplicate classes. This tool rewrites the spec
// into a conventional $ref-based document:
//
//   1. Pin operationIds to explicit PascalCase names (we own the method names).
//   2. Drop the explicit Accept / Authorization / Content-Type header parameters.
//   3. Strip `not` / `not.anyOf` blocks (NSwag cannot express them; it drops them silently).
//   4. Collapse `oneOf`-of-single-value-enum schemas into a single `type: string` + `enum`.
//   5. Rename / unwrap the conversion-artifact component keys to clean type names.
//   6. De-duplicate: hoist every distinct object/enum schema into components/schemas (seeded
//      from the spec's existing named component schemas) and replace occurrences with $ref.
//   7. Prune component schemas that nothing references.
//
// Passes 1 and 5 are spec-specific and driven by the optional <config.json> sidecar:
//
//   {
//     "operationIds":         { "<raw operationId>": "<PascalCase name>", ... },
//     "unwrapArrayComponents":{ "<array component key>": "<element component name>", ... },
//     "componentRenames":     { "<old component key>": "<new component key>", ... }
//   }
//
// With no sidecar (or empty maps) only the generic passes 2-4, 6, 7 run.
//
// Output is JSON (NSwag reads OpenAPI JSON natively) written to <outSpec.json>.
//
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using YamlDotNet.RepresentationModel;

if (args.Length < 2)
{
    Console.Error.WriteLine("usage: dotnet run PreprocessSpec.cs -- <rawSpec.yaml> <outSpec.json> [<config.json>]");
    return 1;
}

var inPath = args[0];
var outPath = args[1];
var configPath = args.Length > 2 ? args[2] : null;

if (!File.Exists(inPath))
{
    Console.Error.WriteLine($"input spec not found: {inPath}");
    return 1;
}

if (configPath is not null && !File.Exists(configPath))
{
    Console.Error.WriteLine($"config not found: {configPath}");
    return 1;
}

var config = PreprocessConfig.Load(configPath);

var root = YamlToJson(inPath) as JsonObject
           ?? throw new InvalidOperationException("spec root is not a mapping");

PinOperationIds(root, config);
DropTransportHeaderParameters(root);
StripNot(root);
CollapseOneOfEnums(root);
NormaliseArtifactComponents(root, config);
DeduplicateIntoComponents(root);
PruneOrphanComponents(root);

Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
File.WriteAllText(
    outPath,
    root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }),
    new UTF8Encoding(false));

var schemaCount = (root["components"]?["schemas"] as JsonObject)?.Count ?? 0;
Console.WriteLine($"preprocessed spec -> {outPath}");
Console.WriteLine($"  components/schemas: {schemaCount}");
return 0;

// ---------------------------------------------------------------------------------------------
// Pass 1: operationId pinning (spec-specific, from config.operationIds)
// ---------------------------------------------------------------------------------------------
static void PinOperationIds(JsonObject root, PreprocessConfig config)
{
    if (config.OperationIds.Count == 0) return;

    foreach (var op in Operations(root))
    {
        if (op["operationId"] is JsonValue v &&
            v.TryGetValue(out string? id) &&
            id is not null &&
            config.OperationIds.TryGetValue(id, out var pinned))
        {
            op["operationId"] = pinned;
        }
    }
}

// ---------------------------------------------------------------------------------------------
// Pass 2: drop transport header parameters
// ---------------------------------------------------------------------------------------------
static void DropTransportHeaderParameters(JsonObject root)
{
    var drop = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Accept", "Authorization", "Content-Type" };

    foreach (var op in Operations(root))
    {
        if (op["parameters"] is not JsonArray ps) continue;

        var kept = new JsonArray();
        foreach (var p in ps)
        {
            var isHeader = p?["in"]?.GetValue<string>() == "header";
            var name = p?["name"]?.GetValue<string>();
            if (isHeader && name is not null && drop.Contains(name)) continue;
            kept.Add(p?.DeepClone());
        }
        op["parameters"] = kept;
    }
}

// ---------------------------------------------------------------------------------------------
// Pass 3: strip `not`
// ---------------------------------------------------------------------------------------------
static void StripNot(JsonNode? node)
{
    switch (node)
    {
        case JsonObject o:
            o.Remove("not");
            foreach (var kv in o.ToArray()) StripNot(kv.Value);
            break;
        case JsonArray a:
            foreach (var item in a) StripNot(item);
            break;
    }
}

// ---------------------------------------------------------------------------------------------
// Pass 4: collapse `oneOf` of single-value enums
// ---------------------------------------------------------------------------------------------
static void CollapseOneOfEnums(JsonNode? node)
{
    switch (node)
    {
        case JsonObject o:
            if (o["oneOf"] is JsonArray branches && branches.Count > 0 && IsSingleValueEnumUnion(branches))
            {
                var values = new List<string>();
                foreach (var b in branches)
                {
                    var val = b!["enum"]!.AsArray()[0]!.GetValue<string>();
                    if (!values.Contains(val)) values.Add(val);
                }
                o.Remove("oneOf");
                o["type"] = "string";
                o["enum"] = new JsonArray(values.Select(v => (JsonNode)JsonValue.Create(v)).ToArray());
            }
            foreach (var kv in o.ToArray()) CollapseOneOfEnums(kv.Value);
            break;
        case JsonArray a:
            foreach (var item in a) CollapseOneOfEnums(item);
            break;
    }

    static bool IsSingleValueEnumUnion(JsonArray branches)
    {
        foreach (var b in branches)
        {
            if (b is not JsonObject bo) return false;
            if (bo["type"]?.GetValue<string>() != "string") return false;
            if (bo["enum"] is not JsonArray e || e.Count != 1) return false;
            if (e[0] is not JsonValue ev || !ev.TryGetValue(out string? _)) return false;
            foreach (var k in bo.Select(kv => kv.Key))
            {
                if (k is not ("type" or "enum" or "title" or "description")) return false;
            }
        }
        return true;
    }
}

// ---------------------------------------------------------------------------------------------
// Pass 5: de-duplicate inline schemas into components/schemas via $ref
// ---------------------------------------------------------------------------------------------
static void DeduplicateIntoComponents(JsonObject root)
{
    var components = root["components"] as JsonObject ?? (JsonObject)(root["components"] = new JsonObject());
    var schemas = components["schemas"] as JsonObject ?? (JsonObject)(components["schemas"] = new JsonObject());

    var byHash = new Dictionary<string, string>(StringComparer.Ordinal);
    // Only reserve names that NSwag will actually emit as a type. Primitive leaf schemas
    // (gmrId, createdDateTime, ...) never become classes and are pruned later, so a hoisted
    // object is free to take the clean PascalCase form of that name.
    var usedNames = new HashSet<string>(
        schemas.Where(kv => kv.Value is JsonObject o && IsNameable(o)).Select(kv => kv.Key),
        StringComparer.OrdinalIgnoreCase);

    // Seed the structural index from the spec's own named schemas, then resolve their children
    // so nested inline objects inside the seeds are also promoted.
    foreach (var key in schemas.Select(kv => kv.Key).ToArray())
    {
        ResolveChildren((JsonObject)schemas[key]!, key, schemas, byHash, usedNames);
        var h = CanonicalHash(schemas[key]!);
        byHash.TryAdd(h, key);
    }

    // Walk every operation payload / parameter schema.
    foreach (var (op, opName) in OperationsWithNames(root))
    {
        if (op["parameters"] is JsonArray ps)
        {
            foreach (var p in ps)
                if (p is JsonObject po && po["schema"] is JsonNode s)
                    Reassign(po, "schema", Resolve(s, opName + Pascal(po["name"]?.GetValue<string>() ?? "Param"), schemas, byHash, usedNames));
        }

        if (op["requestBody"]?["content"] is JsonObject reqContent)
            foreach (var media in reqContent)
                if (media.Value is JsonObject mo && mo["schema"] is JsonNode s)
                    Reassign(mo, "schema", Resolve(s, opName + "Request", schemas, byHash, usedNames));

        if (op["responses"] is JsonObject responses)
            foreach (var resp in responses)
                if (resp.Value?["content"] is JsonObject respContent)
                    foreach (var media in respContent)
                        if (media.Value is JsonObject mo && mo["schema"] is JsonNode s)
                            Reassign(mo, "schema", Resolve(s, opName + "Response", schemas, byHash, usedNames));
    }
}

static JsonNode Resolve(
    JsonNode node, string nameHint, JsonObject schemas,
    Dictionary<string, string> byHash, HashSet<string> usedNames)
{
    if (node is JsonArray arr)
    {
        for (var i = 0; i < arr.Count; i++)
        {
            var replaced = Resolve(arr[i]!, nameHint, schemas, byHash, usedNames);
            if (!ReferenceEquals(replaced, arr[i])) arr[i] = replaced;
        }
        return node;
    }

    if (node is not JsonObject obj) return node;
    if (obj.ContainsKey("$ref")) return node;

    ResolveChildren(obj, nameHint, schemas, byHash, usedNames);

    if (!IsNameable(obj)) return node;

    var hash = CanonicalHash(obj);
    if (byHash.TryGetValue(hash, out var existing))
        return Ref(existing);

    var name = UniqueName(nameHint, usedNames);
    byHash[hash] = name;
    schemas[name] = obj.DeepClone();
    return Ref(name);
}

static void ResolveChildren(
    JsonObject obj, string nameHint, JsonObject schemas,
    Dictionary<string, string> byHash, HashSet<string> usedNames)
{
    if (obj["properties"] is JsonObject props)
        foreach (var name in props.Select(kv => kv.Key).ToArray())
            Reassign(props, name, Resolve(props[name]!, Pascal(name), schemas, byHash, usedNames));

    if (obj["items"] is JsonNode items)
        Reassign(obj, "items", Resolve(items, Singular(nameHint), schemas, byHash, usedNames));

    if (obj["additionalProperties"] is JsonObject ap && ap.Count > 0)
        Reassign(obj, "additionalProperties", Resolve(ap, nameHint + "Value", schemas, byHash, usedNames));

    foreach (var comb in new[] { "allOf", "anyOf", "oneOf" })
        if (obj[comb] is JsonArray combArr)
            for (var i = 0; i < combArr.Count; i++)
            {
                var replaced = Resolve(combArr[i]!, nameHint, schemas, byHash, usedNames);
                if (!ReferenceEquals(replaced, combArr[i])) combArr[i] = replaced;
            }
}

static bool IsNameable(JsonObject obj)
{
    if (obj["enum"] is JsonArray e && e.Count > 0 &&
        obj["type"]?.GetValue<string>() is "string" or null)
        return true;

    var type = obj["type"]?.GetValue<string>();
    if (type is "object" && obj["properties"] is JsonObject p && p.Count > 0) return true;
    if (type is null && obj["properties"] is JsonObject p2 && p2.Count > 0) return true;
    return false;
}

static JsonObject Ref(string name) => new() { ["$ref"] = $"#/components/schemas/{name}" };

// ---------------------------------------------------------------------------------------------
// Pass 5a: give the RAML conversion-artifact component schemas clean names, and unwrap the
// array-typed `definitions` schema down to its element object. Runs before de-duplication so
// the structural index is seeded under these names and inline payloads $ref straight to them.
// ---------------------------------------------------------------------------------------------
static void NormaliseArtifactComponents(JsonObject root, PreprocessConfig config)
{
    if (root["components"]?["schemas"] is not JsonObject schemas) return;

    // Unwrap array-typed component schemas down to their element object (e.g. the GVMS
    // `definitions` schema is `type: array` wrapping the real summary object).
    foreach (var (arrayKey, elementName) in config.UnwrapArrayComponents)
        if (schemas[arrayKey] is JsonObject def && def["items"] is JsonObject defItem)
        {
            schemas.Remove(arrayKey);
            schemas[elementName] = defItem.DeepClone();
        }

    foreach (var (oldKey, newKey) in config.ComponentRenames)
        Rename(schemas, oldKey, newKey);

    static void Rename(JsonObject schemas, string oldKey, string newKey)
    {
        if (schemas[oldKey] is not JsonNode value || schemas.ContainsKey(newKey)) return;
        schemas.Remove(oldKey);
        schemas[newKey] = value.DeepClone();
    }
}

// ---------------------------------------------------------------------------------------------
// Pass 7: drop component schemas that nothing references (to a fixed point). The spec ships 55
// named schemas that no operation used; after de-duplication the survivors are the ones the
// operations actually reference, so anything still unreferenced is dead weight NSwag would
// otherwise emit as a class.
// ---------------------------------------------------------------------------------------------
static void PruneOrphanComponents(JsonObject root)
{
    var schemas = (JsonObject)root["components"]!["schemas"]!;

    while (true)
    {
        var referenced = new HashSet<string>(StringComparer.Ordinal);
        CollectRefs(root, referenced);

        var orphans = schemas.Select(kv => kv.Key).Where(k => !referenced.Contains(k)).ToArray();
        if (orphans.Length == 0) break;
        foreach (var key in orphans) schemas.Remove(key);
    }

    static void CollectRefs(JsonNode? node, HashSet<string> acc)
    {
        switch (node)
        {
            case JsonObject o:
                if (o["$ref"] is JsonValue rv && rv.TryGetValue(out string? r) && r is not null)
                {
                    const string prefix = "#/components/schemas/";
                    if (r.StartsWith(prefix, StringComparison.Ordinal)) acc.Add(r[prefix.Length..]);
                }
                foreach (var kv in o) CollectRefs(kv.Value, acc);
                break;
            case JsonArray a:
                foreach (var item in a) CollectRefs(item, acc);
                break;
        }
    }
}

// ---------------------------------------------------------------------------------------------
// canonical structural hashing (ignores documentation-only keywords)
// ---------------------------------------------------------------------------------------------
static string CanonicalHash(JsonNode? node)
{
    var sb = new StringBuilder();
    Write(node, sb);
    return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())));

    static void Write(JsonNode? n, StringBuilder sb)
    {
        switch (n)
        {
            case JsonObject o:
                sb.Append('{');
                var entries = o
                    .Where(kv => !IgnoredKeyword(kv.Key))
                    .OrderBy(kv => kv.Key, StringComparer.Ordinal);
                var first = true;
                foreach (var (key, value) in entries)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    sb.Append(JsonValue.Create(key)!.ToJsonString()).Append(':');
                    if (key == "required" && value is JsonArray reqs)
                        WriteArray(reqs.OrderBy(x => x?.GetValue<string>(), StringComparer.Ordinal), sb);
                    else
                        Write(value, sb);
                }
                sb.Append('}');
                break;
            case JsonArray a:
                WriteArray(a, sb);
                break;
            case JsonValue v:
                sb.Append(v.ToJsonString());
                break;
            default:
                sb.Append("null");
                break;
        }
    }

    static void WriteArray(IEnumerable<JsonNode?> items, StringBuilder sb)
    {
        sb.Append('[');
        var first = true;
        foreach (var item in items)
        {
            if (!first) sb.Append(',');
            first = false;
            Write(item, sb);
        }
        sb.Append(']');
    }

    static bool IgnoredKeyword(string key) => key is
        "description" or "example" or "examples" or "title" or "default" or
        "externalDocs" or "deprecated" or "readOnly" or "writeOnly" or "xml";
}

// ---------------------------------------------------------------------------------------------
// naming helpers
// ---------------------------------------------------------------------------------------------
static string Pascal(string raw)
{
    var parts = new List<string>();
    var current = new StringBuilder();
    foreach (var ch in raw)
    {
        if (char.IsLetterOrDigit(ch))
        {
            if (current.Length > 0 && char.IsUpper(ch) && !char.IsUpper(current[^1]))
            {
                parts.Add(current.ToString());
                current.Clear();
            }
            current.Append(ch);
        }
        else if (current.Length > 0)
        {
            parts.Add(current.ToString());
            current.Clear();
        }
    }
    if (current.Length > 0) parts.Add(current.ToString());

    var sb = new StringBuilder();
    foreach (var part in parts)
        sb.Append(char.ToUpperInvariant(part[0])).Append(part[1..]);
    var name = sb.ToString();
    return name.Length == 0 ? "Schema" : name;
}

static string Singular(string hint)
{
    var p = Pascal(hint);
    if (p.EndsWith("ies", StringComparison.Ordinal)) return p[..^3] + "y";
    if (p.EndsWith("ses", StringComparison.Ordinal)) return p[..^2];
    if (p.EndsWith("s", StringComparison.Ordinal) && !p.EndsWith("ss", StringComparison.Ordinal)) return p[..^1];
    return p + "Item";
}

static string UniqueName(string hint, HashSet<string> used)
{
    var basis = Pascal(hint);
    if (used.Add(basis)) return basis;
    for (var i = 2; ; i++)
    {
        var candidate = basis + i;
        if (used.Add(candidate)) return candidate;
    }
}

// ---------------------------------------------------------------------------------------------
// small helpers
// ---------------------------------------------------------------------------------------------
static void Reassign(JsonObject parent, string key, JsonNode replacement)
{
    if (!ReferenceEquals(parent[key], replacement)) parent[key] = replacement;
}

static IEnumerable<JsonObject> Operations(JsonObject root)
    => OperationsWithNames(root).Select(x => x.op);

static IEnumerable<(JsonObject op, string name)> OperationsWithNames(JsonObject root)
{
    var methods = new[] { "get", "put", "post", "delete", "patch", "options", "head", "trace" };
    if (root["paths"] is not JsonObject paths) yield break;
    foreach (var path in paths)
    {
        if (path.Value is not JsonObject item) continue;
        foreach (var method in methods)
        {
            if (item[method] is not JsonObject op) continue;
            var name = op["operationId"]?.GetValue<string>() is { } id ? Pascal(id) : Pascal(method + path.Key);
            yield return (op, name);
        }
    }
}

// ---------------------------------------------------------------------------------------------
// YAML -> JSON (preserves scalar types using YAML 1.1 core-schema resolution for plain scalars)
// ---------------------------------------------------------------------------------------------
static JsonNode? YamlToJson(string path)
{
    var yaml = new YamlStream();
    using var reader = new StreamReader(path);
    yaml.Load(reader);
    return Convert(yaml.Documents[0].RootNode);

    static JsonNode? Convert(YamlNode node)
    {
        switch (node)
        {
            case YamlMappingNode map:
                var obj = new JsonObject();
                foreach (var (k, v) in map.Children)
                    obj[((YamlScalarNode)k).Value!] = Convert(v);
                return obj;
            case YamlSequenceNode seq:
                var arr = new JsonArray();
                foreach (var item in seq.Children) arr.Add(Convert(item));
                return arr;
            case YamlScalarNode scalar:
                return Scalar(scalar);
            default:
                return null;
        }
    }

    static JsonNode? Scalar(YamlScalarNode s)
    {
        var value = s.Value ?? "";
        if (s.Style is YamlDotNet.Core.ScalarStyle.SingleQuoted or YamlDotNet.Core.ScalarStyle.DoubleQuoted)
            return JsonValue.Create(value);

        if (value is "" or "~" or "null" or "Null" or "NULL") return null;
        if (value is "true" or "True" or "TRUE") return JsonValue.Create(true);
        if (value is "false" or "False" or "FALSE") return JsonValue.Create(false);
        if (long.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var l))
            return JsonValue.Create(l);
        if (double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d)
            && !value.EndsWith(".", StringComparison.Ordinal))
            return JsonValue.Create(d);
        return JsonValue.Create(value);
    }
}

// ---------------------------------------------------------------------------------------------
// spec-specific configuration, loaded from the optional <config.json> sidecar. Passes 1 and 5
// (operationId pinning, component rename / unwrap) are the only per-spec knobs; everything else
// in the pipeline is structural and runs unconditionally.
// ---------------------------------------------------------------------------------------------
sealed class PreprocessConfig
{
    public Dictionary<string, string> OperationIds { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> UnwrapArrayComponents { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> ComponentRenames { get; } = new(StringComparer.Ordinal);

    public static PreprocessConfig Load(string? path)
    {
        var config = new PreprocessConfig();
        if (path is null) return config;

        var root = JsonNode.Parse(File.ReadAllText(path)) as JsonObject
                   ?? throw new InvalidOperationException($"config root is not an object: {path}");

        Fill(config.OperationIds, root["operationIds"]);
        Fill(config.UnwrapArrayComponents, root["unwrapArrayComponents"]);
        Fill(config.ComponentRenames, root["componentRenames"]);
        return config;

        static void Fill(Dictionary<string, string> target, JsonNode? node)
        {
            if (node is not JsonObject obj) return;
            foreach (var (key, value) in obj)
                if (value is JsonValue v && v.TryGetValue(out string? s) && s is not null)
                    target[key] = s;
        }
    }
}
