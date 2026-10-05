using ShiftClub.Shared.Contracts.Settings;

namespace ShiftClub.Application.Abstractions;

public interface IClubSettingsService
{
    Task<ShellAdminPasswordStatusDto> GetShellAdminStatusAsync(CancellationToken cancellationToken = default);

    Task SetShellAdminPasswordAsync(string password, Guid? updatedBy, CancellationToken cancellationToken = default);

    Task<bool> VerifyShellAdminPasswordAsync(string password, CancellationToken cancellationToken = default);

    Task<LoginBackgroundDto> GetLoginBackgroundAsync(CancellationToken cancellationToken = default);

    Task<LoginBackgroundDto> SetLoginBackgroundUrlAsync(string url, Guid? updatedBy, CancellationToken cancellationToken = default);

    Task ClearLoginBackgroundAsync(Guid? updatedBy, CancellationToken cancellationToken = default);

    Task<TelegramBotSettingsDto> GetTelegramBotSettingsAsync(CancellationToken cancellationToken = default);

    Task<TelegramBotStoredSettings> GetTelegramBotStoredAsync(CancellationToken cancellationToken = default);

    Task<TelegramBotSettingsDto> UpdateTelegramBotSettingsAsync(
        UpdateTelegramBotSettingsRequest request,
        Guid? updatedBy,
        CancellationToken cancellationToken = default);

    Task PersistTelegramBotUsernameAsync(string? username, CancellationToken cancellationToken = default);

    Task<TelegramUsersAdminDto> GetTelegramUsersAdminAsync(CancellationToken cancellationToken = default);

    Task UnlinkCustomerTelegramAsync(Guid customerId, CancellationToken cancellationToken = default);

    Task UnlinkEmployeeTelegramAsync(Guid employeeId, CancellationToken cancellationToken = default);

    Task MakeStaffFromTelegramAsync(TelegramMakeStaffRequest request, CancellationToken cancellationToken = default);

    Task<EngagementSettingsDto> GetEngagementSettingsAsync(CancellationToken cancellationToken = default);

    Task<EngagementStoredSettings> GetEngagementStoredAsync(CancellationToken cancellationToken = default);

    Task<EngagementSettingsDto> UpdateEngagementSettingsAsync(
        UpdateEngagementSettingsRequest request,
        Guid? updatedBy,
        CancellationToken cancellationToken = default);

    Task<MarketingPromoStoredSettings> GetMarketingPromoStoredAsync(CancellationToken cancellationToken = default);

    Task<MarketingPromoSettingsDto> GetMarketingPromoSettingsAsync(CancellationToken cancellationToken = default);

    Task<MarketingPromoSettingsDto> UpdateMarketingPromoSettingsAsync(
        UpdateMarketingPromoRequest request,
        Guid? updatedBy,
        CancellationToken cancellationToken = default);

    Task<TelegramCrmStoredSettings> GetTelegramCrmStoredAsync(CancellationToken cancellationToken = default);

    Task<TelegramCrmSettingsDto> GetTelegramCrmSettingsAsync(CancellationToken cancellationToken = default);

    Task<TelegramCrmSettingsDto> UpdateTelegramCrmSettingsAsync(
        UpdateTelegramCrmSettingsRequest request,
        Guid? updatedBy,
        CancellationToken cancellationToken = default);
}
