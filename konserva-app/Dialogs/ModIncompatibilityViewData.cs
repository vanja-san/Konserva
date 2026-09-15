using Konserva.Localization;
using Konserva.Utilities;
using System.Collections.Generic;
using System.Linq;
using Wpf.Ui.Controls;

namespace Konserva.Dialogs;

/// <summary>Строка решения (Install/Remove/Replace) для списка в XAML</summary>
public sealed class SolutionItem
{
    public required string Name { get; init; }
    public required string? Detail { get; init; }
    public required SymbolRegular Icon { get; init; }
    public required string IconKind { get; init; }
}

/// <summary>Сегмент текста причины: обычный или акцентная зависимость</summary>
public sealed class ReasonSegment
{
    public required string Text { get; init; }
    public required bool IsAccent { get; init; }
}

/// <summary>Строка подробности о конкретном моде</summary>
public sealed class IssueItem
{
    public required string Name { get; init; }
    public required SymbolRegular Icon { get; init; }
    public required string IconKind { get; init; }
    public required IReadOnlyList<ReasonSegment> Segments { get; init; }
}

/// <summary>Нераспознанная строка (решение или подробность)</summary>
public sealed class RawItem
{
    public required string Text { get; init; }
    public required SymbolRegular Icon { get; init; }
    public required string IconKind { get; init; }
}

/// <summary>Модель данных для окна «Кажется, чего-то не хватает…»</summary>
public sealed class ModIncompatibilityViewData
{
    public required bool HasSolutions { get; init; }
    public required bool HasDetails { get; init; }
    public required bool ShowDivider { get; init; }
    public required string DepsHeader { get; init; }
    public required string DetailsHeader { get; init; }
    public required IReadOnlyList<object> Solutions { get; init; }
    public required IReadOnlyList<object> Issues { get; init; }
    public required bool HasMoreDetails { get; init; }
    public required string MoreDetailsText { get; init; }
}

/// <summary>
/// Преобразует структурированные данные о несовместимости модов
/// в модель для XAML-вьюхи окна.
/// </summary>
public static class ModIncompatibilityViewBuilder
{
    private const int MaxIssues = 20;

    public static ModIncompatibilityViewData From(ModIncompatibilityInfo info)
    {
        var solutions = new List<object>();
        foreach (var solution in info.Solutions)
            solutions.Add(MapSolution(solution));
        foreach (var raw in info.RawSolutions)
            solutions.Add(MapRaw(raw, SymbolRegular.Warning16));

        var issues = new List<object>();
        var shown = 0;
        foreach (var issue in info.Issues)
        {
            if (shown >= MaxIssues) break;
            issues.Add(MapIssue(issue));
            shown++;
        }
        foreach (var raw in info.RawDetails)
        {
            if (shown >= MaxIssues) break;
            issues.Add(MapRaw(raw, SymbolRegular.Warning16));
            shown++;
        }

        var total = info.Issues.Count + info.RawDetails.Count;
        var hasMore = total > MaxIssues;
        var hasSolutions = solutions.Count > 0;
        var hasDetails = issues.Count > 0;

        return new ModIncompatibilityViewData
        {
            HasSolutions = hasSolutions,
            HasDetails = hasDetails,
            ShowDivider = hasSolutions && hasDetails,
            DepsHeader = LocalizationManager.Get("ModsIncompat_DepsHeader"),
            DetailsHeader = LocalizationManager.Get("ModsIncompat_Details"),
            Solutions = solutions,
            Issues = issues,
            HasMoreDetails = hasMore,
            MoreDetailsText = hasMore
                ? LocalizationManager.Get("ModsIncompat_MoreDetails", total - MaxIssues)
                : ""
        };
    }

    private static SolutionItem MapSolution(DependencySuggestion suggestion)
    {
        var (icon, iconKind) = suggestion.Kind switch
        {
            SuggestionKind.Install => (SymbolRegular.ArrowDownload24, "Success"),
            SuggestionKind.Remove => (SymbolRegular.Delete24, "Critical"),
            SuggestionKind.Replace => (SymbolRegular.ArrowSync24, "Caution"),
            _ => (SymbolRegular.Warning24, "Neutral")
        };

        return new SolutionItem
        {
            Name = suggestion.Name,
            Detail = RenderSolutionDetail(suggestion),
            Icon = icon,
            IconKind = iconKind
        };
    }

    private static IssueItem MapIssue(ModIssue issue)
    {
        var (icon, iconKind) = issue.Kind switch
        {
            IssueKind.MissingDependency => (SymbolRegular.ErrorCircle16, "Critical"),
            IssueKind.WrongVersion => (SymbolRegular.Warning16, "Caution"),
            IssueKind.ConflictsWith => (SymbolRegular.DismissCircle16, "Critical"),
            IssueKind.DependsOn => (SymbolRegular.LinkSquare16, "Neutral"),
            _ => (SymbolRegular.Warning16, "Neutral")
        };

        return new IssueItem
        {
            Name = $"{issue.ModName} ({issue.ModId}) {issue.ModVersion}",
            Icon = icon,
            IconKind = iconKind,
            Segments = BuildReasonSegments(issue)
        };
    }

    private static RawItem MapRaw(string text, SymbolRegular icon)
    {
        return new RawItem { Text = text, Icon = icon, IconKind = "Neutral" };
    }

    private static IReadOnlyList<ReasonSegment> BuildReasonSegments(ModIssue issue)
    {
        var dependency = RenderDependency(issue.DependencyName, issue.DependencyId);
        var requirement = RenderRequirement(issue.Requirement);
        var present = issue.PresentVersion ?? "";

        var fragments = new List<ReasonSegment>();

        switch (issue.Kind)
        {
            case IssueKind.MissingDependency:
                fragments.Add(Segment(LocalizationManager.Get("ModsIncompat_Detail_Missing_Pre")));
                fragments.Add(Accent(dependency));
                fragments.Add(Segment(LocalizationManager.Get("ModsIncompat_Detail_Missing_Suf", requirement)));
                break;

            case IssueKind.WrongVersion:
                fragments.Add(Segment(LocalizationManager.Get("ModsIncompat_Detail_WrongVersion_Pre")));
                fragments.Add(Accent(dependency));
                fragments.Add(Segment(LocalizationManager.Get("ModsIncompat_Detail_WrongVersion_Mid", requirement, present)));
                break;

            case IssueKind.ConflictsWith:
                fragments.Add(Segment(LocalizationManager.Get("ModsIncompat_Detail_Conflicts_Pre")));
                fragments.Add(Accent(dependency));
                break;

            case IssueKind.DependsOn:
                fragments.Add(Segment(LocalizationManager.Get("ModsIncompat_Detail_Depends_Pre")));
                fragments.Add(Accent(dependency));
                fragments.Add(Segment(LocalizationManager.Get("ModsIncompat_Detail_Depends_Suf")));
                break;

            default:
                fragments.Add(Segment(issue.Raw));
                break;
        }

        return fragments.Where(s => !string.IsNullOrEmpty(s.Text)).ToList();
    }

    private static ReasonSegment Segment(string text) => new() { Text = text ?? "", IsAccent = false };

    private static ReasonSegment Accent(string text) => new() { Text = text ?? "", IsAccent = true };

    private static string? RenderSolutionDetail(DependencySuggestion suggestion)
    {
        switch (suggestion.Kind)
        {
            case SuggestionKind.Install:
                return RenderRequirement(suggestion.Requirement);

            case SuggestionKind.Replace:
            {
                var requirement = ModIncompatibilityParser.ParseVersionRequirement(suggestion.NewVersion);
                var required = requirement.Kind == RequirementKind.Unknown
                    ? (suggestion.NewVersion ?? "")
                    : RenderRequirement(requirement);
                return $"{suggestion.OldVersion}  →  {required}";
            }

            case SuggestionKind.Remove:
                return "";

            default:
                return suggestion.Raw;
        }
    }

    private static string RenderRequirement(DependencyRequirement? requirement)
    {
        if (requirement == null)
            return "";

        return requirement.Kind switch
        {
            RequirementKind.Any => LocalizationManager.Get("ModsIncompat_Req_Any"),
            RequirementKind.AtLeast => LocalizationManager.Get("ModsIncompat_Req_AtLeast", requirement.Version ?? ""),
            RequirementKind.AtMost => LocalizationManager.Get("ModsIncompat_Req_AtMost", requirement.Version ?? ""),
            RequirementKind.Exactly => LocalizationManager.Get("ModsIncompat_Req_Exactly", requirement.Version ?? ""),
            RequirementKind.Range => LocalizationManager.Get("ModsIncompat_Req_Range", requirement.Version ?? "", requirement.VersionTo ?? ""),
            RequirementKind.AnyMajor => LocalizationManager.Get("ModsIncompat_Req_AnyMajor", requirement.Version ?? ""),
            _ => requirement.Raw ?? ""
        };
    }

    private static string RenderDependency(string? name, string? id)
    {
        if (string.IsNullOrEmpty(name))
            return "";

        return !string.IsNullOrEmpty(id) && !string.Equals(name, id, System.StringComparison.OrdinalIgnoreCase)
            ? $"{name} ({id})"
            : name;
    }
}