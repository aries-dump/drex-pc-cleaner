using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;
namespace Drex;

public sealed record Check(string Name, string Status, string Detail);
public sealed class SecurityReport { public List<Check> Checks = new(); public int Score; public bool DefenderKnown, Av, Rtp, DefsOk; }

// Talks to the Windows Defender that is already built into Windows. DREX has no malware engine of its own.
public static class Security
{
    const string StatusScript = @"
$ErrorActionPreference='SilentlyContinue'
$o=[ordered]@{}
$o['ok']=$false
try{
  $s=Get-MpComputerStatus -ErrorAction Stop
  $o['ok']=$true
  $o['av']=[bool]$s.AntivirusEnabled
  $o['rtp']=[bool]$s.RealTimeProtectionEnabled
  $o['sigAge']=[int]$s.AntivirusSignatureAge
  if($s.AntivirusSignatureLastUpdated -and $s.AntivirusSignatureLastUpdated.Year -gt 2000){$o['sigDate']=$s.AntivirusSignatureLastUpdated.ToString('yyyy-MM-dd HH:mm')}else{$o['sigDate']=''}
  if($s.QuickScanEndTime -and $s.QuickScanEndTime.Year -gt 2000){$o['quick']=$s.QuickScanEndTime.ToString('yyyy-MM-dd HH:mm')}else{$o['quick']=''}
  if($s.FullScanEndTime -and $s.FullScanEndTime.Year -gt 2000){$o['full']=$s.FullScanEndTime.ToString('yyyy-MM-dd HH:mm')}else{$o['full']=''}
}catch{ $o['err']=$_.Exception.Message }
try{
  $fw=@()
  foreach($p in (Get-NetFirewallProfile -ErrorAction Stop)){ $fw+=[ordered]@{ n=[string]$p.Name; e=([string]$p.Enabled -eq 'True') } }
  $o['fw']=$fw
}catch{}
$o | ConvertTo-Json -Compress -Depth 4
";

    public const string QuickScan = "try{Start-MpScan -ScanType QuickScan -ErrorAction Stop;'OK'}catch{'ERR '+$_.Exception.Message}";
    public const string FullScan = "try{Start-MpScan -ScanType FullScan -ErrorAction Stop;'OK'}catch{'ERR '+$_.Exception.Message}";
    public const string UpdateDefs = "try{Update-MpSignature -ErrorAction Stop;'OK'}catch{'ERR '+$_.Exception.Message}";

    public static (int Code, string Out, string Err) RunPs(string script, Action<Process>? onStart = null)
    {
        var psi = new ProcessStartInfo("powershell.exe", "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " + Convert.ToBase64String(Encoding.Unicode.GetBytes(script)))
        {
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true
        };
        using var p = Process.Start(psi)!;
        onStart?.Invoke(p);
        var o = p.StandardOutput.ReadToEndAsync();
        var e = p.StandardError.ReadToEndAsync();
        p.WaitForExit();
        return (p.ExitCode, o.Result.Trim(), e.Result.Trim());
    }

    static bool B(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.True;
    static int I(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : 0;
    static string S(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    public static SecurityReport CheckAll()
    {
        var rep = new SecurityReport();
        var r = RunPs(StatusScript);
        JsonElement root = default; bool parsed = false;
        try { using var doc = JsonDocument.Parse(r.Out); root = doc.RootElement.Clone(); parsed = true; } catch { }

        if (!parsed)
            rep.Checks.Add(new Check("Windows Defender", "Warning", "Could not read status from PowerShell. " + (r.Err.Length > 0 ? r.Err.Split('\n')[0] : "")));
        else if (!B(root, "ok"))
            rep.Checks.Add(new Check("Windows Defender", "Warning", "Not available (" + (S(root, "err").Length > 0 ? S(root, "err").Split('\n')[0] : "another antivirus may be in control") + ")"));
        else
        {
            rep.Checks.Add(B(root, "av") ? new Check("Antivirus engine", "Good", "Windows Defender is active") : new Check("Antivirus engine", "Warning", "Windows Defender is off or replaced by another antivirus"));
            rep.Checks.Add(B(root, "rtp") ? new Check("Real-time protection", "Good", "On") : new Check("Real-time protection", "Risk", "Off — files are not scanned when opened"));
            int age = I(root, "sigAge"); string sd = S(root, "sigDate");
            rep.DefenderKnown = true; rep.Av = B(root, "av"); rep.Rtp = B(root, "rtp"); rep.DefsOk = age <= 7;
            rep.Checks.Add(new Check("Virus definitions", age <= 3 ? "Good" : age <= 7 ? "Warning" : "Risk", sd.Length == 0 ? "Unknown" : $"Updated {sd} ({age} day(s) old)"));
            string q = S(root, "quick");
            if (q.Length == 0) rep.Checks.Add(new Check("Last quick scan", "Warning", "No quick scan recorded"));
            else
            {
                bool ok = DateTime.TryParse(q, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt);
                int days = ok ? (int)(DateTime.Now - dt).TotalDays : 0;
                rep.Checks.Add(new Check("Last quick scan", !ok || days <= 14 ? "Good" : "Warning", $"{q}" + (ok ? $" ({days} day(s) ago)" : "")));
            }
        }

        if (parsed && root.TryGetProperty("fw", out var fw) && (fw.ValueKind == JsonValueKind.Array || fw.ValueKind == JsonValueKind.Object))
        {
            var off = new List<string>();
            var items = fw.ValueKind == JsonValueKind.Array ? fw.EnumerateArray().ToList() : new List<JsonElement> { fw };
            foreach (var p in items) if (!B(p, "e")) off.Add(S(p, "n"));
            rep.Checks.Add(off.Count == 0 ? new Check("Windows Firewall", "Good", "On for all network profiles") : new Check("Windows Firewall", "Risk", "Off for: " + string.Join(", ", off)));
        }

        try
        {
            var uac = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "EnableLUA", null);
            if (uac != null) rep.Checks.Add(Convert.ToInt32(uac) == 1 ? new Check("User Account Control", "Good", "On") : new Check("User Account Control", "Risk", "Off — programs can change the system without asking"));
            var rdp = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\Terminal Server", "fDenyTSConnections", null);
            if (rdp != null) rep.Checks.Add(Convert.ToInt32(rdp) == 1 ? new Check("Remote Desktop", "Good", "Disabled") : new Check("Remote Desktop", "Warning", "Enabled — turn off if you do not use it"));
        }
        catch { }

        var scored = rep.Checks.Where(c => c.Status != "Info").ToList();
        double pts = scored.Sum(c => c.Status == "Good" ? 1.0 : c.Status == "Warning" ? 0.5 : 0.0);
        rep.Score = scored.Count == 0 ? 0 : (int)Math.Round(100 * pts / scored.Count);
        return rep;
    }
}
