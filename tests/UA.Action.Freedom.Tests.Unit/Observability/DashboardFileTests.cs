using System.Text.Json;
using AwesomeAssertions;

namespace UA.Action.Freedom.Tests.Unit.Observability;

/// <summary>
/// The Grafana dashboards are provisioned from JSON files, and nothing else checks them: a typo in a
/// datasource uid or a panel with an empty query loads fine and shows "No data" in production. These
/// tests are the only guard between a bad edit and a blank dashboard.
/// </summary>
public class DashboardFileTests
{
    private static readonly string[] KnownDatasourceUids = ["prometheus", "loki", "tempo"];

    private static readonly string[] ExpectedDashboardUids =
    [
        "freedom-dotnet",
        "freedom-api",
        "freedom-customs-worker",
        "freedom-manifest-worker",
        "freedom-manifest-pipeline",
        "freedom-convoy-operations",
        "freedom-access-security",
    ];

    private sealed record Dashboard(string FileName, JsonElement Root)
    {
        public string Uid => Root.GetProperty("uid").GetString() ?? string.Empty;

        public string Title => Root.GetProperty("title").GetString() ?? string.Empty;

        public IEnumerable<JsonElement> Panels => Root.GetProperty("panels").EnumerateArray();

        public IEnumerable<JsonElement> ContentPanels => Panels.Where(panel => panel.GetProperty("type").GetString() != "row");

        public IEnumerable<JsonElement> Targets => Panels
            .Where(panel => panel.TryGetProperty("targets", out _))
            .SelectMany(panel => panel.GetProperty("targets").EnumerateArray());
    }

    private static string DashboardDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !directory.EnumerateFiles("UA.Action.Freedom.slnx").Any())
        {
            directory = directory.Parent;
        }

        directory.Should().NotBeNull("the tests run from inside the repository");

        return Path.Combine(directory!.FullName, "iac", "local", "grafana", "dashboards");
    }

    private static IReadOnlyList<Dashboard> Dashboards() =>
    [
        .. Directory.EnumerateFiles(DashboardDirectory(), "*.json")
            .Order()
            .Select(path => new Dashboard(Path.GetFileName(path), JsonDocument.Parse(File.ReadAllText(path)).RootElement)),
    ];

    private static IEnumerable<string> DatasourceUidsOf(JsonElement element) =>
        element.TryGetProperty("datasource", out var datasource) && datasource.ValueKind == JsonValueKind.Object
            ? [datasource.GetProperty("uid").GetString() ?? string.Empty]
            : [];

    [Fact]
    public void Every_dashboard_file_is_valid_json_with_a_uid_and_a_title()
    {
        var dashboards = Dashboards();

        dashboards.Should().NotBeEmpty();
        dashboards.Should().OnlyContain(dashboard => dashboard.Uid != string.Empty && dashboard.Title != string.Empty);
    }

    [Fact]
    public void Every_uid_is_unique_and_matches_its_file_name_so_deep_links_and_the_provisioner_stay_stable()
    {
        var dashboards = Dashboards();

        dashboards.Select(dashboard => dashboard.Uid).Should().OnlyHaveUniqueItems();
        dashboards.Select(dashboard => dashboard.Title).Should().OnlyHaveUniqueItems();
        dashboards.Should().OnlyContain(dashboard => dashboard.FileName == dashboard.Uid + ".json");
    }

    [Fact]
    public void The_expected_dashboards_all_exist()
    {
        Dashboards().Select(dashboard => dashboard.Uid).Should().Contain(ExpectedDashboardUids);
    }

    [Fact]
    public void Every_datasource_is_one_the_local_stack_provisions()
    {
        foreach (var dashboard in Dashboards())
        {
            var uids = dashboard.Panels.SelectMany(DatasourceUidsOf)
                .Concat(dashboard.Targets.SelectMany(DatasourceUidsOf))
                .Concat(dashboard.Root.GetProperty("templating").GetProperty("list").EnumerateArray().SelectMany(DatasourceUidsOf));

            uids.Should().OnlyContain(uid => KnownDatasourceUids.Contains(uid), $"{dashboard.FileName} may only use prometheus, loki or tempo");
        }
    }

    [Fact]
    public void Every_panel_that_queries_something_has_a_target_and_none_of_them_is_empty()
    {
        foreach (var dashboard in Dashboards())
        {
            var queryingPanels = dashboard.ContentPanels.Where(panel => panel.GetProperty("type").GetString() != "text");

            queryingPanels.Select(TargetCount).Should().OnlyContain(
                count => count > 0,
                $"{dashboard.FileName}: every panel needs a query");

            dashboard.Targets.Select(QueryOf).Should().OnlyContain(
                query => !string.IsNullOrWhiteSpace(query),
                $"{dashboard.FileName}: no target may have an empty expr or query");
        }
    }

    [Fact]
    public void Panel_ids_are_unique_within_a_dashboard()
    {
        foreach (var dashboard in Dashboards())
        {
            dashboard.Panels.Select(panel => panel.GetProperty("id").GetInt32())
                .Should().OnlyHaveUniqueItems($"{dashboard.FileName} panel ids");
        }
    }

    [Fact]
    public void Panels_fit_the_24_column_grid_and_do_not_overlap()
    {
        foreach (var dashboard in Dashboards())
        {
            var boxes = dashboard.Panels
                .Select(panel => panel.GetProperty("gridPos"))
                .Select(grid => (
                    X: grid.GetProperty("x").GetInt32(),
                    Y: grid.GetProperty("y").GetInt32(),
                    W: grid.GetProperty("w").GetInt32(),
                    H: grid.GetProperty("h").GetInt32()))
                .ToList();

            boxes.Should().OnlyContain(box => box.X >= 0 && box.W > 0 && box.X + box.W <= 24, $"{dashboard.FileName} must stay inside 24 columns");

            var overlapping = boxes
                .SelectMany((first, index) => boxes.Skip(index + 1).Select(second => (first, second)))
                .Where(pair => Overlaps(pair.first, pair.second));

            overlapping.Should().BeEmpty($"{dashboard.FileName} panels must not overlap");
        }
    }

    [Fact]
    public void Dashboards_that_take_a_service_declare_the_variable_they_use()
    {
        foreach (var dashboard in Dashboards())
        {
            var usesService = dashboard.Targets.Select(QueryOf).Any(query => query.Contains("$service"));
            var declared = dashboard.Root.GetProperty("templating").GetProperty("list").EnumerateArray()
                .Any(variable => variable.GetProperty("name").GetString() == "service");

            declared.Should().Be(usesService, $"{dashboard.FileName}: $service is used exactly when it is declared");
        }
    }

    private static int TargetCount(JsonElement panel) =>
        panel.TryGetProperty("targets", out var targets) ? targets.GetArrayLength() : 0;

    private static string QueryOf(JsonElement target) =>
        target.TryGetProperty("expr", out var expr) ? expr.GetString() ?? string.Empty
        : target.TryGetProperty("query", out var query) ? query.GetString() ?? string.Empty
        : string.Empty;

    private static bool Overlaps((int X, int Y, int W, int H) a, (int X, int Y, int W, int H) b) =>
        a.X < b.X + b.W && b.X < a.X + a.W && a.Y < b.Y + b.H && b.Y < a.Y + a.H;
}
