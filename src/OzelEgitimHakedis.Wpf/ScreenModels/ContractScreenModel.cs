using OzelEgitimHakedis.Core.Models;

namespace OzelEgitimHakedis.Wpf.ScreenModels;

/// <summary>Contract presentation rules without controls, persistence or window state.</summary>
internal static class ContractScreenModel
{
    internal static string WorkName(ContractDraftData contract, IEnumerable<string> assignedRouteNames)
    {
        var names = assignedRouteNames.Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => OfficialTemplateProfile.DeriveWorkName(name))
            .Distinct(StringComparer.Ordinal).ToArray();
        return names.Length == 0 ? contract.WorkName : string.Join(Environment.NewLine, names);
    }
}
