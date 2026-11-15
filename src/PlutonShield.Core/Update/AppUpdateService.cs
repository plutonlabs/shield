using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using PlutonShield.Core.Logging;

namespace PlutonShield.Core.Update;

public enum AppUpdateState
{
    /// <summary>Nothing newer is known.</summary>
    Idle,
    Checking,
    /// <summary>A newer release exists (<see cref="AppUpdateService.Info"/>); its installer is not downloaded yet.</summary>
    Available,
    Downloading,
    /// <summary>The installer is downloaded and checked: installing is one step away.</summary>
    Ready,
}

public enum CheckOutcome { UpToDate, Available, Failed }

/// <summary>
/// Finds out whether a newer release of the app exists, downloads its installer and starts it. The window and the
/// notification bell only read <see cref="State"/> and listen to <see cref="Changed"/>.
/// </summary>
public sealed class AppUpdateService
{
    /// <summary>Quiet install of the new version, which Setup then starts again (see /RELAUNCH in installer\PlutonShield.iss).</summary>
    public const string SetupArguments = "/SILENT /SUPPRESSMSGBOXES /NORESTART /RELAUNCH=1";

    private readonly HttpClient _http;
    private readonly string _dir;
    private readonly string _arch;
    private readonly string _feedUrl;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private double _lastReported = -1;

    public AppUpdateService(HttpClient http, string downloadDir, SemVer? current = null, string? arch = null, string? feedUrl = null)
    {
        _http = http;
        _dir = downloadDir;
        Current = current ?? AppVersion.Current;
        _arch = arch ?? ReleaseFeed.ArchName(RuntimeInformation.ProcessArchitecture);
        _feedUrl = feedUrl ?? ReleaseFeed.LatestUrl;
    }

    public SemVer Current { get; }

    public AppUpdateState State { get; private set; }

    /// <summary>The newer release, while <see cref="State"/> is Available, Downloading or Ready.</summary>
    public AppUpdateInfo? Info { get; private set; }

    /// <summary>0 to 1 while downloading.</summary>
    public double Progress { get; private set; }

    /// <summary>What went wrong last time (a check or a download); cleared by the next attempt.</summary>
    public string? Error { get; private set; }

    /// <summary>When the last check or download attempt ended, and whether it failed. For the schedule.</summary>
    public DateTime? LastAttempt { get; private set; }

    public bool LastAttemptFailed { get; private set; }

    /// <summary>When the last check that reached GitHub ended.</summary>
    public DateTime? LastCheck { get; private set; }

    public DateTime? LastDownloadFailure { get; private set; }

    public string? SetupPath { get; private set; }

    /// <summary>Raised on every change. May come from a background thread.</summary>
    public event EventHandler? Changed;

    public async Task<CheckOutcome> CheckAsync(CancellationToken ct = default)
    {
        if (State is AppUpdateState.Downloading or AppUpdateState.Ready) return CheckOutcome.Available;

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (State is AppUpdateState.Downloading or AppUpdateState.Ready) return CheckOutcome.Available;

            Error = null;
            var before = State;
            Set(AppUpdateState.Checking);
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, _feedUrl);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
                request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
                using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);

                AppUpdateInfo? found = null;
                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    Log.Info("app update: no release published yet");
                }
                else
                {
                    response.EnsureSuccessStatusCode();
                    var json = await ReadLimitedAsync(response, 1024 * 1024, ct).ConfigureAwait(false);
                    found = ReleaseFeed.Parse(json, _arch, out var reason);
                    if (found is null) Log.Info("app update: nothing to install: " + reason);
                }

                LastCheck = DateTime.Now;
                Finished(failed: false);
                if (found is not null && found.Version > Current)
                {
                    if (Info is null || Info.Version != found.Version) Info = found;
                    Log.Info($"app update: {found.Version} is available (this is {Current})");
                    Set(AppUpdateState.Available);
                    return CheckOutcome.Available;
                }

                Info = null;
                Set(AppUpdateState.Idle);
                return CheckOutcome.UpToDate;
            }
            catch (OperationCanceledException)
            {
                Set(before == AppUpdateState.Checking ? AppUpdateState.Idle : before);
                throw;
            }
            catch (Exception ex)
            {
                Log.Warn("app update: check failed: " + ex.Message);
                Error = ex.Message;
                Finished(failed: true);
                Set(Info is null ? AppUpdateState.Idle : AppUpdateState.Available);
                return CheckOutcome.Failed;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Downloads and verifies the installer of <see cref="Info"/>. Returns true when it is ready (also when it already was).</summary>
    public async Task<bool> DownloadAsync(CancellationToken ct = default)
    {
        if (State == AppUpdateState.Ready) return true;
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (State == AppUpdateState.Ready) return true;
            if (Info is not { } info) return false;

            Error = null;
            Progress = 0;
            _lastReported = -1;
            Set(AppUpdateState.Downloading);
            try
            {
                SetupPath = await AppUpdateDownloader.DownloadAsync(_http, info, _dir, OnProgress, ct).ConfigureAwait(false);
                Finished(failed: false);
                Log.Info($"app update: {info.Version} downloaded and verified");
                Set(AppUpdateState.Ready);
                return true;
            }
            catch (OperationCanceledException)
            {
                Set(AppUpdateState.Available);
                throw;
            }
            catch (Exception ex)
            {
                Log.Warn("app update: download failed: " + ex.Message);
                Error = ex.Message;
                LastDownloadFailure = DateTime.Now;
                Finished(failed: true);
                Set(AppUpdateState.Available);
                return false;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Starts the downloaded installer (quietly). The caller then closes the app; Setup would ask it to anyway.</summary>
    public bool StartSetup()
    {
        if (State != AppUpdateState.Ready || SetupPath is null || !File.Exists(SetupPath)) return false;
        try
        {
            Process.Start(new ProcessStartInfo(SetupPath, SetupArguments) { UseShellExecute = false });
            Log.Info("app update: installer started");
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("app update: could not start the installer: " + ex.Message);
            Error = ex.Message;
            Raise();
            return false;
        }
    }

    /// <summary>Removes installers of this and older versions and half-finished downloads (run at start: this version is installed).</summary>
    public int CleanDownloads()
    {
        var removed = 0;
        try
        {
            if (!Directory.Exists(_dir)) return 0;
            foreach (var path in Directory.EnumerateFiles(_dir))
            {
                var name = Path.GetFileName(path);
                var stale = name.EndsWith(".part", StringComparison.OrdinalIgnoreCase)
                            || (name.StartsWith("PlutonShield-v", StringComparison.Ordinal) && VersionOfInstaller(name) is { } v && v <= Current);
                if (!stale) continue;
                try
                {
                    File.Delete(path);
                    removed++;
                }
                catch (Exception)
                {
                    // in use: next time
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warn("app update: cleaning old downloads failed: " + ex.Message);
        }

        return removed;
    }

    private static SemVer? VersionOfInstaller(string fileName)
    {
        // PlutonShield-v0.4.0-win-x64-Setup.exe (.sha256)
        var start = "PlutonShield-v".Length;
        var end = fileName.IndexOf("-win-", StringComparison.Ordinal);
        return end > start && SemVer.TryParse(fileName[start..end], out var v) ? v : null;
    }

    private void OnProgress(double p)
    {
        Progress = Math.Clamp(p, 0, 1);
        // Tell the window about every whole percent, not about every chunk.
        if (Progress - _lastReported >= 0.01 || Progress >= 1)
        {
            _lastReported = Progress;
            Raise();
        }
    }

    private void Finished(bool failed)
    {
        LastAttempt = DateTime.Now;
        LastAttemptFailed = failed;
    }

    private void Set(AppUpdateState state)
    {
        State = state;
        Raise();
    }

    private void Raise() => Changed?.Invoke(this, EventArgs.Empty);

    private static async Task<string> ReadLimitedAsync(HttpResponseMessage response, int maxBytes, CancellationToken ct)
    {
        if (response.Content.Headers.ContentLength > maxBytes) throw new InvalidDataException("the answer is too large");
        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var ms = new MemoryStream();
        var buffer = new byte[16384];
        int n;
        while ((n = await stream.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            ms.Write(buffer, 0, n);
            if (ms.Length > maxBytes) throw new InvalidDataException("the answer is too large");
        }

        return System.Text.Encoding.UTF8.GetString(ms.ToArray());
    }
}
