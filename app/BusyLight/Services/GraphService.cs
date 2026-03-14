using BusyLight.Models;
using Microsoft.Graph;
using Microsoft.Graph.Models.ODataErrors;
using Microsoft.Identity.Client;
using Microsoft.Identity.Client.Extensions.Msal;
using Microsoft.Kiota.Abstractions.Authentication;

namespace BusyLight.Services;

/// <summary>
/// Authenticates with Microsoft Entra ID using MSAL.NET and polls
/// the Microsoft Graph presence endpoint at a configurable interval.
/// Fires <see cref="PresenceChanged"/> only when the availability status changes.
/// </summary>
public sealed class GraphService : IDisposable
{
    // ── Events ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Raised on the thread pool whenever the Teams presence availability
    /// changes. The event argument is the Graph API Availability string
    /// (e.g. "Available", "Busy", "DoNotDisturb").
    /// </summary>
    public event EventHandler<string>? PresenceChanged;

    /// <summary>
    /// Raised when a non-fatal error occurs (auth failure, API error, etc.)
    /// so the tray application can display a balloon notification.
    /// </summary>
    public event EventHandler<string>? ErrorOccurred;

    // ── Private state ─────────────────────────────────────────────────────────

    private readonly AppSettings _settings;
    private IPublicClientApplication? _msalClient;
    private GraphServiceClient? _graphClient;
    private PeriodicTimer? _timer;
    private Task? _pollingTask;
    private CancellationTokenSource? _cts;
    private string _lastPresence = string.Empty;

    private static readonly string[] Scopes = ["User.Read", "Presence.Read"];

    // MSAL token cache file is stored alongside the configuration
    private static readonly string TokenCacheDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                     "BusyLight");
    private const string TokenCacheFileName = "msal_token_cache.bin";

    // ── Constructor ───────────────────────────────────────────────────────────

    public GraphService(AppSettings settings)
    {
        _settings = settings;
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Perform OAuth2 authentication (silent first, then interactive).
    /// Must be called before <see cref="StartPollingAsync"/>.
    /// </summary>
    public async Task AuthenticateAsync()
    {
        if (string.IsNullOrWhiteSpace(_settings.AzureAd.ClientId))
            throw new InvalidOperationException(
                "ClientId is not configured. Please fill in appsettings.json.");

        // Build the public client application
        _msalClient = PublicClientApplicationBuilder
            .Create(_settings.AzureAd.ClientId)
            .WithAuthority(AzureCloudInstance.AzurePublic, _settings.AzureAd.TenantId)
            .WithRedirectUri("http://localhost")
            .Build();

        // Register persistent token cache (DPAPI-encrypted on Windows)
        var storageProps = new StorageCreationPropertiesBuilder(
                TokenCacheFileName, TokenCacheDir)
            .Build();

        var cacheHelper = await MsalCacheHelper
            .CreateAsync(storageProps)
            .ConfigureAwait(false);

        cacheHelper.RegisterCache(_msalClient.UserTokenCache);

        // Attempt silent acquisition first; fall back to interactive on first run
        await AcquireTokenAsync().ConfigureAwait(false);

        // Build the Graph client using a token provider that wraps MSAL
        var tokenProvider = new MsalAccessTokenProvider(_msalClient, Scopes);
        var authProvider  = new BaseBearerTokenAuthenticationProvider(tokenProvider);
        _graphClient = new GraphServiceClient(authProvider);
    }

    /// <summary>
    /// Start polling the Graph presence endpoint at the configured interval.
    /// </summary>
    public void StartPolling()
    {
        if (_graphClient is null)
            throw new InvalidOperationException("Call AuthenticateAsync() first.");

        _cts         = new CancellationTokenSource();
        _timer       = new PeriodicTimer(TimeSpan.FromSeconds(_settings.Polling.GraphIntervalSeconds));
        _pollingTask = RunPollingLoopAsync(_cts.Token);
    }

    /// <summary>
    /// Trigger an immediate presence poll outside the normal timer cycle.
    /// The result fires <see cref="PresenceChanged"/> if the status differs from last known.
    /// </summary>
    public Task FetchNowAsync() => PollPresenceAsync();

    /// <summary>Stop polling and release resources.</summary>
    public void StopPolling()
    {
        // Guard against Cancel() on an already-disposed CTS
        // (can happen if StopPolling is called after Dispose)
        try { _cts?.Cancel(); } catch (ObjectDisposedException) { }
        _timer?.Dispose();
        _timer = null;
    }

    // ── Polling loop ──────────────────────────────────────────────────────────

    private async Task RunPollingLoopAsync(CancellationToken ct)
    {
        // Fire one poll immediately so the tray icon shows the correct colour at startup
        await PollPresenceAsync().ConfigureAwait(false);

        try
        {
            while (await _timer!.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                await PollPresenceAsync().ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }
    }

    private async Task PollPresenceAsync()
    {
        try
        {
            var presence = await _graphClient!.Me.Presence
                .GetAsync()
                .ConfigureAwait(false);

            string availability = presence?.Availability ?? "PresenceUnknown";

            if (availability != _lastPresence)
            {
                _lastPresence = availability;
                PresenceChanged?.Invoke(this, availability);
                Debug.WriteLine($"[Graph] Presence changed → {availability}");
            }
        }
        catch (ODataError ex)
        {
            string msg = $"Graph API error: {ex.Error?.Message ?? ex.Message}";
            Debug.WriteLine($"[Graph] {msg}");
            ErrorOccurred?.Invoke(this, msg);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Silently re-authenticate if the token has expired
            if (ex.Message.Contains("401") || ex.Message.Contains("Unauthorized"))
            {
                try { await AcquireTokenAsync().ConfigureAwait(false); }
                catch (Exception authEx)
                {
                    ErrorOccurred?.Invoke(this, $"Re-authentication failed: {authEx.Message}");
                }
            }
            else
            {
                ErrorOccurred?.Invoke(this, $"Presence poll error: {ex.Message}");
            }
        }
    }

    // ── MSAL token acquisition ────────────────────────────────────────────────

    private async Task AcquireTokenAsync()
    {
        var accounts = await _msalClient!.GetAccountsAsync().ConfigureAwait(false);

        try
        {
            await _msalClient
                .AcquireTokenSilent(Scopes, accounts.FirstOrDefault())
                .ExecuteAsync()
                .ConfigureAwait(false);

            Debug.WriteLine("[Auth] Token acquired silently.");
        }
        catch (MsalUiRequiredException)
        {
            Debug.WriteLine("[Auth] Silent acquisition failed — launching interactive flow.");

            await _msalClient
                .AcquireTokenInteractive(Scopes)
                .ExecuteAsync()
                .ConfigureAwait(false);

            Debug.WriteLine("[Auth] Interactive authentication succeeded.");
        }
    }

    // ── IDisposable ───────────────────────────────────────────────────────────

    private bool _disposed;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        StopPolling();
        _cts?.Dispose();
        _cts = null;
    }

    // ── Inner: MSAL access-token provider for Graph SDK v5 ───────────────────

    /// <summary>
    /// Wraps an <see cref="IPublicClientApplication"/> as an
    /// <see cref="IAccessTokenProvider"/> so it can be used with
    /// <see cref="BaseBearerTokenAuthenticationProvider"/>.
    /// </summary>
    private sealed class MsalAccessTokenProvider : IAccessTokenProvider
    {
        private readonly IPublicClientApplication _app;
        private readonly string[] _scopes;

        public MsalAccessTokenProvider(IPublicClientApplication app, string[] scopes)
        {
            _app    = app;
            _scopes = scopes;
        }

        public AllowedHostsValidator AllowedHostsValidator { get; } =
            new(["graph.microsoft.com"]);

        public async Task<string> GetAuthorizationTokenAsync(
            Uri uri,
            Dictionary<string, object>? additionalAuthenticationContext = null,
            CancellationToken cancellationToken = default)
        {
            var accounts = await _app.GetAccountsAsync().ConfigureAwait(false);

            AuthenticationResult result;
            try
            {
                result = await _app
                    .AcquireTokenSilent(_scopes, accounts.FirstOrDefault())
                    .ExecuteAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (MsalUiRequiredException)
            {
                result = await _app
                    .AcquireTokenInteractive(_scopes)
                    .ExecuteAsync(cancellationToken)
                    .ConfigureAwait(false);
            }

            return result.AccessToken;
        }
    }
}
