using ShiftClub.Shared.Contracts.Bar;
using ShiftClub.Shared.Contracts.Cash;

namespace ShiftClub.Application.Abstractions;

public interface IBarService
{
    Task<IReadOnlyList<ProductCategoryDto>> GetCategoriesAsync(
        Guid? branchId,
        bool includeInactive = false,
        CancellationToken cancellationToken = default);

    Task<ProductCategoryDto> CreateCategoryAsync(
        Guid branchId,
        CreateProductCategoryRequest request,
        Guid employeeId,
        CancellationToken cancellationToken = default);

    Task<ProductCategoryDto> UpdateCategoryAsync(
        Guid categoryId,
        UpdateProductCategoryRequest request,
        Guid employeeId,
        CancellationToken cancellationToken = default);

    Task DeleteCategoryAsync(Guid categoryId, Guid employeeId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProductDto>> GetProductsAsync(
        Guid? branchId,
        Guid? categoryId,
        bool includeInactive = false,
        CancellationToken cancellationToken = default);

    Task<ProductDto> CreateProductAsync(Guid branchId, CreateProductRequest request, Guid employeeId, CancellationToken cancellationToken = default);
    Task<ProductDto> UpdateProductAsync(Guid productId, UpdateProductRequest request, Guid employeeId, CancellationToken cancellationToken = default);
    Task DeleteProductAsync(Guid productId, Guid employeeId, CancellationToken cancellationToken = default);
    Task<ProductDto> SetProductImageUrlAsync(Guid productId, string? imageUrl, Guid employeeId, CancellationToken cancellationToken = default);
    Task<ProductDto> AdjustStockAsync(Guid productId, AdjustStockRequest request, Guid employeeId, CancellationToken cancellationToken = default);
    Task<BarOrderDto> CreateOrderAsync(CreateBarOrderRequest request, Guid? employeeId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<BarOrderDto>> GetOpenOrdersAsync(Guid? branchId, CancellationToken cancellationToken = default);
    Task<BarOrderDto> UpdateOrderStatusAsync(Guid orderId, UpdateBarOrderStatusRequest request, Guid employeeId, CancellationToken cancellationToken = default);
    Task<ReceiptDto> SellItemsAsync(SellBarItemsRequest request, Guid employeeId, CancellationToken cancellationToken = default);
}
