using ShiftClub.Shared.Contracts.ClientLauncher;
using ShiftClub.Shared.Contracts.Customers;
using ShiftClub.Shared.Contracts.News;
using ShiftClub.Shared.Contracts.Sessions;

namespace ShiftClub.Application.Abstractions;

public interface IClientLauncherService
{
    Task<ClientCustomerAuthDto> LoginAsync(
        Guid computerId,
        ClientCustomerLoginRequest request,
        CancellationToken cancellationToken = default);

    Task<ClientCustomerAuthDto> LoginByCustomerIdAsync(
        Guid computerId,
        Guid customerId,
        CancellationToken cancellationToken = default);

    Task<ClientCustomerAuthDto> UpdateComfortAsync(
        Guid computerId,
        Guid customerId,
        string customerToken,
        ClientComfortSettingsRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Сброс привязки входа на этом ПК (после выхода из Shell).</summary>
    Task LogoutAsync(
        Guid computerId,
        string customerToken,
        CancellationToken cancellationToken = default);

    /// <summary>Бронь, которая сейчас держит ПК (для экрана входа Shell).</summary>
    Task<ClientBookingHoldDto?> GetBookingHoldAsync(
        Guid computerId,
        Guid? customerId = null,
        CancellationToken cancellationToken = default);

    Task<ClientCustomerAuthDto?> GetAccountAsync(
        Guid computerId,
        Guid customerId,
        string customerToken,
        CancellationToken cancellationToken = default);

    Task<ClientCustomerAuthDto> ChangeCredentialsAsync(
        Guid computerId,
        Guid customerId,
        string customerToken,
        ClientChangeCredentialsRequest request,
        CancellationToken cancellationToken = default);

    Task<ClientCustomerAuthDto> UpdateProfileAsync(
        Guid computerId,
        Guid customerId,
        string customerToken,
        ClientUpdateProfileRequest request,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CustomerBalanceTransactionDto>> GetAccountTransactionsAsync(
        Guid customerId,
        int take,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CustomerTimeBankTransactionDto>> GetAccountTimeBankTransactionsAsync(
        Guid customerId,
        int take,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ClientAccountSessionDto>> GetAccountSessionsAsync(
        Guid customerId,
        int take,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ClientBarOrderDto>> GetAccountOrdersAsync(
        Guid customerId,
        int take,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ClientAccountBookingDto>> GetAccountBookingsAsync(
        Guid customerId,
        int take,
        CancellationToken cancellationToken = default);

    Task<SessionDto?> GetCurrentSessionAsync(Guid computerId, CancellationToken cancellationToken = default);

    Task<SessionDto> StartBalanceSessionAsync(
        Guid computerId,
        Guid customerId,
        ClientStartSessionRequest request,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SoftwareAppDto>> GetAppsAsync(Guid computerId, CancellationToken cancellationToken = default);

    Task<SessionDto> ExtendActiveSessionAsync(
        Guid computerId,
        ClientExtendSessionRequest request,
        CancellationToken cancellationToken = default);

    Task<SessionDto> EndActiveSessionAsync(
        Guid computerId,
        ClientEndSessionRequest request,
        CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<ClientBarCategoryDto> Categories, IReadOnlyList<ClientBarProductDto> Products)> GetBarCatalogAsync(
        Guid computerId,
        CancellationToken cancellationToken = default);

    Task<ClientBarOrderDto> PlaceBarOrderAsync(
        Guid computerId,
        Guid? customerId,
        ClientPlaceBarOrderRequest request,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ClientNewsDto>> GetNewsAsync(
        Guid computerId,
        CancellationToken cancellationToken = default);

    /// <summary>JWT + привязка ПК + login_epoch. null = недействителен / вытеснен.</summary>
    Task<Guid?> ValidateCustomerTokenAsync(string token, Guid computerId, CancellationToken cancellationToken = default);

    /// <summary>Только разбор JWT (без проверки epoch). Для совместимости — предпочитайте async.</summary>
    Guid? ValidateCustomerToken(string token, Guid computerId);
}
