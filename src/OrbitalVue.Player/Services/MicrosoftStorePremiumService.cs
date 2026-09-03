using System.Reflection;
using System.Security.Principal;
using Windows.ApplicationModel;
using Windows.Services.Store;

namespace OrbitalVue.Player.Services;

public sealed record PremiumStoreConfiguration(
    OrbitalVueDistributionMode DistributionMode,
    string? ProductId)
{
    public bool IsStoreBuild => DistributionMode == OrbitalVueDistributionMode.Store;

    public static PremiumStoreConfiguration Current
    {
        get
        {
#if ORBITALVUE_STORE_BUILD
            const string mode = "store";
#else
            const string mode = "personal";
#endif
            var productId = typeof(PremiumStoreConfiguration).Assembly
                .GetCustomAttributes<AssemblyMetadataAttribute>()
                .SingleOrDefault(attribute =>
                    attribute.Key.Equals("OrbitalVuePremiumProductId", StringComparison.Ordinal))
                ?.Value;
            return Evaluate(mode, productId);
        }
    }

    public static PremiumStoreConfiguration Evaluate(string? mode, string? productId)
    {
        var distributionMode = mode?.Trim().ToLowerInvariant() switch
        {
            "personal" => OrbitalVueDistributionMode.Personal,
            "store" => OrbitalVueDistributionMode.Store,
            _ => OrbitalVueDistributionMode.Unknown
        };
        var candidate = productId?.Trim();
        if (string.IsNullOrWhiteSpace(candidate) || candidate.Length is < 3 or > 256 ||
            !candidate.All(character =>
                char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-'))
            candidate = null;
        return new PremiumStoreConfiguration(distributionMode, candidate);
    }
}

public sealed record PremiumPurchaseState(
    PremiumAccessSnapshot Access,
    string? ConfiguredProductId = null,
    string? ProductTitle = null,
    string? FormattedPrice = null,
    bool IsBusy = false,
    bool CanPurchase = false,
    bool CanRestore = false,
    string? Message = null)
{
    public static PremiumPurchaseState Initial(
        PremiumStoreConfiguration? configuration = null,
        PremiumAccessSnapshot? access = null)
    {
        configuration ??= PremiumStoreConfiguration.Current;
        access ??= PremiumAccessPolicy.Evaluate(
            configuration.DistributionMode switch
            {
                OrbitalVueDistributionMode.Personal => "personal",
                OrbitalVueDistributionMode.Store => "store",
                _ => "unknown"
            },
            hasVerifiedStorePurchase: false,
            configuration.ProductId);
        var message = access.CanUseMediaCenters
            ? access.Explanation
            : configuration.ProductId is null
                ? "The Microsoft Store durable add-on has not been configured for this build."
                : "Checking the Microsoft Store for a verified lifetime purchase…";
        return new PremiumPurchaseState(
            access,
            configuration.ProductId,
            IsBusy: configuration.IsStoreBuild && configuration.ProductId is not null,
            Message: message);
    }
}

public sealed record MicrosoftStoreProduct(
    string ProductId,
    string StoreId,
    string Title,
    string FormattedPrice);

public enum MicrosoftStorePurchaseOutcome
{
    Succeeded,
    AlreadyPurchased,
    NotPurchased,
    NetworkError,
    ServerError,
    Unknown
}

public interface IMicrosoftStoreClient : IDisposable
{
    event EventHandler? LicenseChanged;

    Task<MicrosoftStoreProduct?> GetDurableProductAsync(string productId);

    Task<bool> OwnsDurableProductAsync(string productId);

    Task<MicrosoftStorePurchaseOutcome> PurchaseAsync(string productId);
}

public sealed class MicrosoftStorePremiumService : IDisposable
{
    private readonly PremiumStoreConfiguration _configuration;
    private readonly Func<nint, IMicrosoftStoreClient> _clientFactory;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private SynchronizationContext? _synchronizationContext;
    private IMicrosoftStoreClient? _client;
    private MicrosoftStoreProduct? _product;
    private bool _disposed;

    public MicrosoftStorePremiumService(
        PremiumStoreConfiguration? configuration = null,
        Func<nint, IMicrosoftStoreClient>? clientFactory = null)
    {
        _configuration = configuration ?? PremiumStoreConfiguration.Current;
        _clientFactory = clientFactory ?? (ownerWindow => new WindowsMicrosoftStoreClient(ownerWindow));
        State = PremiumPurchaseState.Initial(_configuration);
    }

    public PremiumPurchaseState State { get; private set; }

    public event EventHandler<PremiumPurchaseState>? StateChanged;

    public async Task StartAsync(nint ownerWindow)
    {
        ThrowIfDisposed();
        _synchronizationContext ??= SynchronizationContext.Current;
        if (!_configuration.IsStoreBuild || _configuration.ProductId is null) return;
        await _operationGate.WaitAsync();
        try
        {
            if (_client is null)
            {
                _client = _clientFactory(ownerWindow);
                _client.LicenseChanged += Client_LicenseChanged;
            }
            await RefreshCoreAsync(restoring: false, loadProduct: true);
        }
        catch (Exception exception)
        {
            PublishFailure(SafeStoreError(exception));
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task PurchaseAsync()
    {
        ThrowIfDisposed();
        if (_client is null || _product is null || _configuration.ProductId is null)
        {
            PublishFailure("The Microsoft Store lifetime product is not ready to purchase.");
            return;
        }
        await _operationGate.WaitAsync();
        try
        {
            Publish(State with
            {
                IsBusy = true,
                CanPurchase = false,
                CanRestore = false,
                Message = "Opening the Microsoft Store…"
            });
            var outcome = await _client.PurchaseAsync(_configuration.ProductId);
            switch (outcome)
            {
                case MicrosoftStorePurchaseOutcome.Succeeded:
                case MicrosoftStorePurchaseOutcome.AlreadyPurchased:
                    await RefreshCoreAsync(restoring: true, loadProduct: false);
                    break;
                case MicrosoftStorePurchaseOutcome.NotPurchased:
                    Publish(State with
                    {
                        IsBusy = false,
                        CanPurchase = !State.Access.CanUseMediaCenters && _product is not null,
                        CanRestore = true,
                        Message = "Purchase canceled. Nothing was charged."
                    });
                    break;
                case MicrosoftStorePurchaseOutcome.NetworkError:
                    PublishFailure("The Microsoft Store could not complete the purchase while offline.");
                    break;
                case MicrosoftStorePurchaseOutcome.ServerError:
                    PublishFailure("The Microsoft Store purchase service is temporarily unavailable.");
                    break;
                default:
                    PublishFailure("The Microsoft Store returned an unsupported purchase result.");
                    break;
            }
        }
        catch (Exception exception)
        {
            PublishFailure(SafeStoreError(exception));
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task RestoreAsync()
    {
        ThrowIfDisposed();
        if (_client is null || _configuration.ProductId is null)
        {
            PublishFailure("The Microsoft Store license service is not available in this build.");
            return;
        }
        await _operationGate.WaitAsync();
        try
        {
            await RefreshCoreAsync(restoring: true, loadProduct: _product is null);
        }
        catch (Exception exception)
        {
            PublishFailure(SafeStoreError(exception));
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private async Task RefreshCoreAsync(bool restoring, bool loadProduct)
    {
        var client = _client ?? throw new InvalidOperationException("The Microsoft Store client is not initialized.");
        var productId = _configuration.ProductId ?? throw new InvalidOperationException("No Microsoft Store product is configured.");
        Publish(State with
        {
            IsBusy = true,
            CanPurchase = false,
            CanRestore = false,
            Message = restoring ? "Restoring Microsoft Store purchases…" : "Checking Microsoft Store ownership…"
        });

        var ownsProduct = await client.OwnsDurableProductAsync(productId);
        if (loadProduct)
            _product = await client.GetDurableProductAsync(productId);

        var access = PremiumAccessPolicy.Evaluate("store", ownsProduct, productId);
        var message = access.CanUseMediaCenters
            ? restoring
                ? "Lifetime premium access restored from the Microsoft Store."
                : "Verified Microsoft Store lifetime purchase active."
            : _product is null
                ? "The configured Microsoft Store durable add-on is not available for this app."
                : restoring
                    ? "No verified lifetime purchase was found for this Microsoft Store account."
                    : "Buy once from the Microsoft Store or restore a lifetime purchase already owned by this account.";
        Publish(new PremiumPurchaseState(
            access,
            productId,
            _product?.Title,
            _product?.FormattedPrice,
            IsBusy: false,
            CanPurchase: !access.CanUseMediaCenters && _product is not null,
            CanRestore: true,
            Message: message));
    }

    private async void Client_LicenseChanged(object? sender, EventArgs e)
    {
        try
        {
            await RestoreAsync();
        }
        catch
        {
            // RestoreAsync converts provider errors into a safe, fail-closed state.
        }
    }

    private void PublishFailure(string message)
    {
        var access = State.Access.CanUseMediaCenters
            ? State.Access
            : PremiumAccessPolicy.Evaluate("store", false, _configuration.ProductId);
        Publish(State with
        {
            Access = access,
            IsBusy = false,
            CanPurchase = !access.CanUseMediaCenters && _product is not null,
            CanRestore = _client is not null && _configuration.ProductId is not null,
            Message = message
        });
    }

    private void Publish(PremiumPurchaseState state)
    {
        void Apply()
        {
            State = state;
            StateChanged?.Invoke(this, state);
        }

        if (_synchronizationContext is not null && SynchronizationContext.Current != _synchronizationContext)
            _synchronizationContext.Post(_ => Apply(), null);
        else
            Apply();
    }

    private static string SafeStoreError(Exception exception) => exception switch
    {
        InvalidOperationException invalid when !string.IsNullOrWhiteSpace(invalid.Message) => invalid.Message,
        UnauthorizedAccessException => "Microsoft Store purchases are unavailable while OrbitalVue is running elevated.",
        _ => "The Microsoft Store license service is unavailable. Your existing playlist sources remain available."
    };

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_client is not null)
        {
            _client.LicenseChanged -= Client_LicenseChanged;
            _client.Dispose();
        }
    }
}

public sealed class WindowsMicrosoftStoreClient : IMicrosoftStoreClient
{
    private readonly StoreContext _context;
    private readonly Dictionary<string, StoreProduct> _products = new(StringComparer.Ordinal);
    private bool _disposed;

    public WindowsMicrosoftStoreClient(nint ownerWindow)
    {
        if (ownerWindow == 0) throw new InvalidOperationException("The Microsoft Store purchase window is not ready.");
        if (IsElevated())
            throw new UnauthorizedAccessException("Microsoft Store purchases cannot run elevated.");
        RequirePackageIdentity();
        _context = StoreContext.GetDefault();
        WinRT.Interop.InitializeWithWindow.Initialize(_context, ownerWindow);
        _context.OfflineLicensesChanged += Context_OfflineLicensesChanged;
    }

    public event EventHandler? LicenseChanged;

    public async Task<MicrosoftStoreProduct?> GetDurableProductAsync(string productId)
    {
        ThrowIfDisposed();
        var result = await _context.GetAssociatedStoreProductsAsync(["Durable"]);
        if (result.ExtendedError is not null)
            throw new InvalidOperationException("The Microsoft Store could not load the configured durable add-on.");
        var matches = result.Products.Values
            .Where(product =>
                product.ProductKind.Equals("Durable", StringComparison.OrdinalIgnoreCase) &&
                product.InAppOfferToken.Equals(productId, StringComparison.Ordinal))
            .ToList();
        if (matches.Count > 1)
            throw new InvalidOperationException("Partner Center returned more than one durable add-on for the configured product ID.");
        var product = matches.SingleOrDefault();
        if (product is null) return null;
        _products[productId] = product;
        return new MicrosoftStoreProduct(
            productId,
            product.StoreId,
            product.Title,
            product.Price.FormattedPrice);
    }

    public async Task<bool> OwnsDurableProductAsync(string productId)
    {
        ThrowIfDisposed();
        var license = await _context.GetAppLicenseAsync();
        return license.AddOnLicenses.Values.Any(addOn =>
            addOn.InAppOfferToken.Equals(productId, StringComparison.Ordinal));
    }

    public async Task<MicrosoftStorePurchaseOutcome> PurchaseAsync(string productId)
    {
        ThrowIfDisposed();
        if (!_products.TryGetValue(productId, out var product))
        {
            _ = await GetDurableProductAsync(productId);
            if (!_products.TryGetValue(productId, out product))
                throw new InvalidOperationException("The Microsoft Store lifetime product is unavailable.");
        }
        var result = await product.RequestPurchaseAsync();
        return result.Status switch
        {
            StorePurchaseStatus.Succeeded => MicrosoftStorePurchaseOutcome.Succeeded,
            StorePurchaseStatus.AlreadyPurchased => MicrosoftStorePurchaseOutcome.AlreadyPurchased,
            StorePurchaseStatus.NotPurchased => MicrosoftStorePurchaseOutcome.NotPurchased,
            StorePurchaseStatus.NetworkError => MicrosoftStorePurchaseOutcome.NetworkError,
            StorePurchaseStatus.ServerError => MicrosoftStorePurchaseOutcome.ServerError,
            _ => MicrosoftStorePurchaseOutcome.Unknown
        };
    }

    private void Context_OfflineLicensesChanged(StoreContext sender, object args) =>
        LicenseChanged?.Invoke(this, EventArgs.Empty);

    private static void RequirePackageIdentity()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(Package.Current.Id.Name)) throw new InvalidOperationException();
        }
        catch
        {
            throw new InvalidOperationException(
                "Microsoft Store purchases require the MSIX-packaged Store build. This direct-download build stays locked.");
        }
    }

    private static bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _context.OfflineLicensesChanged -= Context_OfflineLicensesChanged;
        _products.Clear();
    }
}
