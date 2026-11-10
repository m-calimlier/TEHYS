using System.IO.Compression;
using System.Text.Json;
using OzelEgitimHakedis.Core.Models;

namespace OzelEgitimHakedis.Core.Services;

public sealed record BackupManifest(int Version, DateTime CreatedAt, string ApplicationVersion, IReadOnlyDictionary<string,string> Files);
public sealed record BackupSummary(DateTime CreatedAt, int FileCount, int PdfCount, int ArchiveCount, int ContractCount, int RouteCount, int PaymentCount, string PackageHash);

/// <summary>Portable full backups. All input is verified before either live root is replaced.</summary>
public sealed class OehsBackupService
{
    private readonly string _data;
    private readonly string _outputs;
    private const int MaxFiles = 20000;
    private const long MaxBytes = 2L * 1024 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly HashSet<string> DataNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "oehs-special-education.json", "oehs-basic-secondary.json", "official-document-settings.json",
        "window-layout.json", "window-layout-preference.json", ".fresh-start-version", "period-info.json"
    };
    public OehsBackupService(string dataRoot, string outputRoot)
    {
        _data = Path.GetFullPath(dataRoot); _outputs = Path.GetFullPath(outputRoot);
        if (IsInside(_data, _outputs) || IsInside(_outputs, _data) || string.Equals(_data, _outputs, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Veri ve çıktı kökleri ayrı olmalıdır.");
    }
    private static bool IsInside(string root, string path) => Path.GetFullPath(path).StartsWith(
        Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    public void Create(string packagePath)
    {
        packagePath = Path.GetFullPath(packagePath);
        if (IsInside(_data,packagePath) || IsInside(_outputs,packagePath))
            throw new IOException("Yedek dosyasını OEHS veri/çıktı klasörünün dışında kaydedin.");
        var files = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        if (Directory.Exists(_data)) foreach(var path in Directory.EnumerateFiles(_data))
            if (DataNames.Contains(Path.GetFileName(path))) files.Add("data/"+Path.GetFileName(path),path);
        if (Directory.Exists(_outputs)) foreach(var path in Directory.EnumerateFiles(_outputs,"*",SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(_outputs,path).Replace('\\','/');
            if (relative.Split('/').Any(p=>p.StartsWith(".oehs-pdf-staging-",StringComparison.OrdinalIgnoreCase)) ||
                Path.GetExtension(path) is ".tmp" or ".pending" or ".bak" or ".rollback") continue;
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("Bağlantılı dosya yedeklenemez.");
            files.Add("outputs/"+relative,path);
        }
        if (files.Count>MaxFiles || files.Values.Sum(p=>new FileInfo(p).Length)>MaxBytes) throw new IOException("Yedek boyutu sınırı aşıldı.");
        Directory.CreateDirectory(Path.GetDirectoryName(packagePath)!);
        var temp = packagePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var hashes = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
            using(var zip = ZipFile.Open(temp,ZipArchiveMode.Create))
            {
                foreach(var pair in files)
                {
                    var entry=zip.CreateEntry(pair.Key,CompressionLevel.Fastest);
                    using(var source=File.OpenRead(pair.Value)) using(var target=entry.Open()) source.CopyTo(target);
                    // Hash the bytes actually stored, not a later read of the live source.
                }
            }
            using(var zip=ZipFile.Open(temp,ZipArchiveMode.Update))
            {
                foreach(var entry in zip.Entries) { using var stream=entry.Open(); hashes.Add(entry.FullName,Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream))); }
                using var writer=new StreamWriter(zip.CreateEntry("manifest.json").Open());
                writer.Write(JsonSerializer.Serialize(new BackupManifest(1,DateTime.UtcNow,
                    typeof(OehsBackupService).Assembly.GetName().Version?.ToString() ?? "1",hashes)));
            }
            _ = Inspect(temp);
            File.Move(temp,packagePath,true);
        }
        finally { if(File.Exists(temp)) File.Delete(temp); }
    }
    public static BackupSummary Inspect(string path, string? workingRoot = null)
    {
        var stage=CreateWorkingDirectory(workingRoot ?? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, "Calisma"), "backup-check");
        try { return ExtractVerified(path,stage); }
        finally { Directory.Delete(stage,true); }
    }
    private static BackupSummary ExtractVerified(string path,string stage)
    {
        try
        {
            using var zip=ZipFile.OpenRead(path);
            if(zip.Entries.Count>MaxFiles+1 || zip.Entries.Sum(e=>e.Length)>MaxBytes) throw new InvalidDataException("Paket sınırları aşıldı.");
            var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach(var e in zip.Entries)
            {
                if(!names.Add(e.FullName) || e.FullName.Contains('\\', StringComparison.Ordinal) || e.FullName.Split('/').Any(p=>p is ".." or "." or "") ||
                   e.FullName != "manifest.json" && !e.FullName.StartsWith("data/",StringComparison.Ordinal) && !e.FullName.StartsWith("outputs/",StringComparison.Ordinal))
                    throw new InvalidDataException("Geçersiz/tekrarlanan paket yolu.");
                _=OutputEvidenceStore.ResolveInside(stage,e.FullName);
            }
            var manifestEntry=zip.GetEntry("manifest.json") ?? throw new InvalidDataException("Yedek bildirimi yok.");
            if(manifestEntry.Length>4*1024*1024) throw new InvalidDataException("Bildirim çok büyük.");
            using var manifestStream=manifestEntry.Open();
            var manifest=JsonSerializer.Deserialize<BackupManifest>(manifestStream,JsonOptions);
            if(manifest is null || manifest.Version!=1 || manifest.Files is null || manifest.Files.Count!=zip.Entries.Count-1)
                throw new InvalidDataException("Desteklenmeyen veya eksik yedek bildirimi.");
            var contracts=0;var routes=0;var payments=0;var archives=0;var pdfs=0;long copied=0;
            foreach(var pair in manifest.Files)
            {
                var entry=zip.GetEntry(pair.Key) ?? throw new InvalidDataException("Paket dosyası eksik.");
                var target=OutputEvidenceStore.ResolveInside(stage,pair.Key);
                if(pair.Key.StartsWith("data/",StringComparison.Ordinal) && !DataNames.Contains(pair.Key[5..]))
                    throw new InvalidDataException("Bilinmeyen canlı veri dosyası.");
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                using(var source=entry.Open()) using(var destination=File.Create(target))
                {
                    var buffer=new byte[81920];int length;
                    while((length=source.Read(buffer))>0) {copied+=length;if(copied>MaxBytes) throw new InvalidDataException("Açılan paket çok büyük.");destination.Write(buffer,0,length);}
                }
                if(OutputEvidenceStore.FileHash(target)!=pair.Value) throw new InvalidDataException("Yedek dosya özeti uyuşmuyor.");
                if(pair.Key.EndsWith(".json",StringComparison.OrdinalIgnoreCase))
                {
                    using var json=JsonDocument.Parse(File.ReadAllText(target));
                    if(pair.Key is "data/oehs-special-education.json" or "data/oehs-basic-secondary.json")
                    {
                        var data=JsonSerializer.Deserialize<OehsApplicationData>(File.ReadAllText(target),JsonOptions)
                            ?? throw new InvalidDataException("Boş uygulama verisi.");
                        if(data.Routes is null || data.Contractors is null) throw new InvalidDataException("Eksik uygulama verisi.");
                        if(data.CalculationConstants is not null && !data.CalculationConstants.TryValidate(out var error))
                            throw new InvalidDataException("Geçersiz hesaplama oranları: "+error);
                        var expected=pair.Key=="data/oehs-special-education.json"?TransportationProgramType.SpecialEducation:TransportationProgramType.BasicOrSecondary;
                        if(data.Routes.Any(r=>r is null || r.ProgramType!=expected) ||
                           data.Contractors.Any(c=>c is null || c.ProgramType!=expected) ||
                           data.Contracts?.Any(c=>c is null || c.ProgramType!=expected)==true ||
                           data.ProgressPayments?.Any(p=>p is null || p.Input is null || p.Calculation is null || p.Input.ProgramType!=expected)==true)
                            throw new InvalidDataException("Paketin program/ilişki verileri tutarsız.");
                        contracts+=(data.Contracts?.Count ?? (data.Contract is null?0:1));routes+=data.Routes.Count;payments+=data.ProgressPayments?.Count ?? 0;
                    }
                    if(pair.Key.Contains("/Arsiv/Hakedisler/",StringComparison.OrdinalIgnoreCase))
                    { _=ProgressPaymentArchiveStore.Load(target); archives++; }
                }
                if(pair.Key.EndsWith(".pdf",StringComparison.OrdinalIgnoreCase)) pdfs++;
            }
            return new(manifest.CreatedAt,manifest.Files.Count,pdfs,archives,contracts,routes,payments,OutputEvidenceStore.FileHash(path));
        }
        catch(Exception ex) when(ex is JsonException or ProgressPaymentArchiveException or ArgumentException)
        {throw new InvalidDataException("Yedek içeriği doğrulanamadı.",ex);}
    }
    public string Restore(string path,Action<string>? beforeReplace=null)
    {
        // Stage on each target volume; renames and rollback never cross volumes.
        var token=Guid.NewGuid().ToString("N");
        var stageData=_data+".restore-"+token; var stageOutput=_outputs+".restore-"+token;
        var check=CreateWorkingDirectory(Path.Combine(Path.GetDirectoryName(_data)!, "Calisma"), "restore");
        var moved=new List<(string Target,string Old,bool HadOld)>();
        var recovery=Path.Combine(Path.GetDirectoryName(_data)!,"Yedekler","Kurtarma","OEHS-Recovery-"+DateTime.Now.ToString("yyyyMMdd-HHmmss",System.Globalization.CultureInfo.InvariantCulture)+"-"+token+".oehs-backup.zip");
        var rollbackFailed=false;
        try
        {
            _=ExtractVerified(path,check);
            Create(recovery); // Must succeed before any target is touched.
            CopyTree(Path.Combine(check,"data"),stageData); CopyTree(Path.Combine(check,"outputs"),stageOutput);
            foreach(var item in new[]{(Key:"data",Target:_data,Stage:stageData),(Key:"outputs",Target:_outputs,Stage:stageOutput)})
            {
                beforeReplace?.Invoke(item.Key);
                var old=Path.Combine(Path.GetDirectoryName(item.Target)!,"Yedekler","GeriYukleme",token,item.Key);var hadOld=Directory.Exists(item.Target);
                Directory.CreateDirectory(Path.GetDirectoryName(old)!);
                if(hadOld) Directory.Move(item.Target,old);
                moved.Add((item.Target,old,hadOld));
                Directory.Move(item.Stage,item.Target);
            }
            return recovery;
        }
        catch(Exception ex)
        {
            foreach(var item in moved.AsEnumerable().Reverse())
            {
                try {if(Directory.Exists(item.Target)) Directory.Delete(item.Target,true);if(item.HadOld) Directory.Move(item.Old,item.Target);}
                catch(IOException) {rollbackFailed=true;}
                catch(UnauthorizedAccessException) {rollbackFailed=true;}
            }
            if(rollbackFailed) throw new IOException("Geri yükleme geri alınamadı. Kurtarma yedeği: "+recovery+"; .before-"+token+" klasörlerini koruyun.",ex);
            throw;
        }
        finally
        {
            TryDelete(check);TryDelete(stageData);TryDelete(stageOutput);
            // Keep .before-* directories as a second recovery copy. They may contain
            // older reset backups or unknown application files omitted from this package.
        }
    }
    private static void CopyTree(string source,string target)
    {
        Directory.CreateDirectory(target);
        if(!Directory.Exists(source)) return;
        foreach(var file in Directory.EnumerateFiles(source,"*",SearchOption.AllDirectories))
        {
            var destination=OutputEvidenceStore.ResolveInside(target,Path.GetRelativePath(source,file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);File.Copy(file,destination);
        }
    }
    internal static string CreateWorkingDirectory(string root, string purpose)
    {
        var path = Path.Combine(root, purpose + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
    private static void TryDelete(string path)
    {
        try {if(Directory.Exists(path)) Directory.Delete(path,true);}
        catch(IOException) { } catch(UnauthorizedAccessException) { }
    }
}
