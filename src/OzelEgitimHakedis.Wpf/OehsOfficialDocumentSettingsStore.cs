using System;
using System.IO;
using System.Text.Json;
using OzelEgitimHakedis.Core.Services;

namespace OzelEgitimHakedis.Wpf;

/// <summary>
/// Configurable signatory data shared by the four official document templates.
/// </summary>
internal sealed record OehsOfficialDocumentSignatories(
    string PreparerName,
    string PreparerRole,
    string ControllerName,
    string ControllerRole,
    string ApproverName,
    string ApproverRole)
{
    public static OehsOfficialDocumentSignatories Defaults => new(
        PreparerName: "",
        PreparerRole: "",
        ControllerName: "",
        ControllerRole: "",
        ApproverName: "",
        ApproverRole: "");

    public OehsOfficialDocumentSignatories Normalize()
    {
        var defaults = Defaults;
        return new OehsOfficialDocumentSignatories(
            Clean(PreparerName, defaults.PreparerName),
            Clean(PreparerRole, defaults.PreparerRole),
            Clean(ControllerName, defaults.ControllerName),
            Clean(ControllerRole, defaults.ControllerRole),
            Clean(ApproverName, defaults.ApproverName),
            Clean(ApproverRole, defaults.ApproverRole));
    }

    private static string Clean(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
}

/// <summary>
/// Loads signatory configuration from the desktop workspace. When absent, it
/// creates a blank user-editable JSON file. Invalid JSON
/// fails with a clear error rather than silently printing incorrect signatories.
/// </summary>
internal static class OehsOfficialDocumentSettingsStore
{
    public static string DefaultPath => OehsRuntimePaths.OfficialDocumentSettingsFilePath;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public static OehsOfficialDocumentSignatories Load(string? settingsPath = null)
    {
        var path = string.IsNullOrWhiteSpace(settingsPath)
            ? DefaultPath
            : Path.GetFullPath(settingsPath);

        if (!File.Exists(path))
            CreateDefaultFile(path);

        try
        {
            var json = File.ReadAllText(path);
            var signatories = JsonSerializer.Deserialize<OehsOfficialDocumentSignatories>(json, JsonOptions)
                ?? throw new JsonException("Ayar dosyası boş.");

            return signatories.Normalize();
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException(
                $"Resmî belge imza ayarları okunamadı. JSON biçimini kontrol edin: {path}", ex);
        }
    }

    public static void Save(OehsOfficialDocumentSignatories signatories, string? settingsPath = null)
    {
        ArgumentNullException.ThrowIfNull(signatories);

        var path = string.IsNullOrWhiteSpace(settingsPath)
            ? DefaultPath
            : Path.GetFullPath(settingsPath);
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var normalized = signatories.Normalize();
        var temporaryPath = path + ".tmp";
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(normalized, JsonOptions));
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    private static void CreateDefaultFile(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var temporaryPath = path + ".tmp";
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(OehsOfficialDocumentSignatories.Defaults, JsonOptions));
            try
            {
                File.Move(temporaryPath, path, overwrite: false);
            }
            catch (IOException) when (File.Exists(path))
            {
                // Another OEHS instance created the settings file first.
            }
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }
}
