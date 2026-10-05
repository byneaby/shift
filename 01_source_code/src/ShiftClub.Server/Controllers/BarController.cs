using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShiftClub.Application.Abstractions;
using ShiftClub.Infrastructure.Persistence;
using ShiftClub.Server.Auth;
using ShiftClub.Shared.Contracts;
using ShiftClub.Shared.Contracts.Bar;
using ShiftClub.Shared.Contracts.Cash;
using ShiftClub.Shared.ErrorCodes;
using ShiftClub.Shared.Permissions;

namespace ShiftClub.Server.Controllers;

[ApiController]
[Authorize]
[Route("api/bar")]
public class BarController : ControllerBase
{
    private readonly IBarService _bar;
    private readonly ShiftClubDbContext _db;

    public BarController(IBarService bar, ShiftClubDbContext db)
    {
        _bar = bar;
        _db = db;
    }

    [HttpGet("categories")]
    [RequirePermission(PermissionCodes.BarView, PermissionCodes.BarSell, PermissionCodes.InventoryManage)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<ProductCategoryDto>>>> Categories(
        [FromQuery] bool includeInactive = false,
        CancellationToken cancellationToken = default)
    {
        var list = await _bar.GetCategoriesAsync(null, includeInactive, cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<ProductCategoryDto>>.Ok(list));
    }

    [HttpPost("categories")]
    [RequirePermission(PermissionCodes.InventoryManage)]
    public async Task<ActionResult<ApiResponse<ProductCategoryDto>>> CreateCategory(
        [FromBody] CreateProductCategoryRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var branchId = await this.ResolveBranchIdAsync(_db, null, cancellationToken);
            var cat = await _bar.CreateCategoryAsync(branchId, request, GetEmployeeId(), cancellationToken);
            return Ok(ApiResponse<ProductCategoryDto>.Ok(cat));
        }
        catch (Exception ex) when (ex is InvalidOperationException or KeyNotFoundException)
        {
            return BadRequest(ApiResponse<ProductCategoryDto>.Fail(CommonErrorCodes.ValidationFailed, ex.Message));
        }
    }

    [HttpPut("categories/{id:guid}")]
    [RequirePermission(PermissionCodes.InventoryManage)]
    public async Task<ActionResult<ApiResponse<ProductCategoryDto>>> UpdateCategory(
        Guid id,
        [FromBody] UpdateProductCategoryRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var cat = await _bar.UpdateCategoryAsync(id, request, GetEmployeeId(), cancellationToken);
            return Ok(ApiResponse<ProductCategoryDto>.Ok(cat));
        }
        catch (Exception ex) when (ex is InvalidOperationException or KeyNotFoundException)
        {
            return BadRequest(ApiResponse<ProductCategoryDto>.Fail(CommonErrorCodes.ValidationFailed, ex.Message));
        }
    }

    [HttpDelete("categories/{id:guid}")]
    [RequirePermission(PermissionCodes.InventoryManage)]
    public async Task<ActionResult<ApiResponse>> DeleteCategory(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            await _bar.DeleteCategoryAsync(id, GetEmployeeId(), cancellationToken);
            return Ok(ApiResponse.Ok());
        }
        catch (Exception ex) when (ex is InvalidOperationException or KeyNotFoundException)
        {
            return BadRequest(ApiResponse.Fail(CommonErrorCodes.ValidationFailed, ex.Message));
        }
    }

    [HttpGet("products")]
    [RequirePermission(PermissionCodes.BarView, PermissionCodes.BarSell, PermissionCodes.InventoryManage)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<ProductDto>>>> Products(
        [FromQuery] Guid? categoryId,
        [FromQuery] bool includeInactive = false,
        CancellationToken cancellationToken = default)
    {
        var list = await _bar.GetProductsAsync(null, categoryId, includeInactive, cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<ProductDto>>.Ok(list));
    }

    [HttpPost("products")]
    [RequirePermission(PermissionCodes.InventoryManage)]
    public async Task<ActionResult<ApiResponse<ProductDto>>> CreateProduct(
        [FromBody] CreateProductRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var branchId = await this.ResolveBranchIdAsync(_db, null, cancellationToken);
            var product = await _bar.CreateProductAsync(branchId, request, GetEmployeeId(), cancellationToken);
            return Ok(ApiResponse<ProductDto>.Ok(product));
        }
        catch (Exception ex) when (ex is InvalidOperationException or KeyNotFoundException)
        {
            return BadRequest(ApiResponse<ProductDto>.Fail(CommonErrorCodes.ValidationFailed, ex.Message));
        }
    }

    [HttpPut("products/{id:guid}")]
    [RequirePermission(PermissionCodes.InventoryManage)]
    public async Task<ActionResult<ApiResponse<ProductDto>>> UpdateProduct(
        Guid id,
        [FromBody] UpdateProductRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var product = await _bar.UpdateProductAsync(id, request, GetEmployeeId(), cancellationToken);
            return Ok(ApiResponse<ProductDto>.Ok(product));
        }
        catch (Exception ex) when (ex is InvalidOperationException or KeyNotFoundException)
        {
            return BadRequest(ApiResponse<ProductDto>.Fail(CommonErrorCodes.ValidationFailed, ex.Message));
        }
    }

    [HttpDelete("products/{id:guid}")]
    [RequirePermission(PermissionCodes.InventoryManage)]
    public async Task<ActionResult<ApiResponse>> DeleteProduct(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            await _bar.DeleteProductAsync(id, GetEmployeeId(), cancellationToken);
            return Ok(ApiResponse.Ok());
        }
        catch (Exception ex) when (ex is InvalidOperationException or KeyNotFoundException)
        {
            return BadRequest(ApiResponse.Fail(CommonErrorCodes.ValidationFailed, ex.Message));
        }
    }

    [HttpPost("products/{id:guid}/image")]
    [RequestSizeLimit(8_000_000)]
    [RequirePermission(PermissionCodes.InventoryManage)]
    public async Task<ActionResult<ApiResponse<ProductDto>>> UploadImage(
        Guid id,
        IFormFile file,
        CancellationToken cancellationToken)
    {
        if (file.Length == 0)
            return BadRequest(ApiResponse<ProductDto>.Fail(CommonErrorCodes.ValidationFailed, "Файл пустой"));

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (ext is not (".jpg" or ".jpeg" or ".png" or ".webp" or ".gif"))
            return BadRequest(ApiResponse<ProductDto>.Fail(CommonErrorCodes.ValidationFailed, "Нужен jpg/png/webp"));

        try
        {
            var dir = Path.Combine(Directory.GetCurrentDirectory(), "data", "bar-images");
            Directory.CreateDirectory(dir);
            var name = $"{id:N}{ext}";
            var path = Path.Combine(dir, name);
            await using (var stream = System.IO.File.Create(path))
                await file.CopyToAsync(stream, cancellationToken);

            var url = $"/media/bar/{name}";
            var product = await _bar.SetProductImageUrlAsync(id, url, GetEmployeeId(), cancellationToken);
            return Ok(ApiResponse<ProductDto>.Ok(product));
        }
        catch (Exception ex) when (ex is InvalidOperationException or KeyNotFoundException)
        {
            return BadRequest(ApiResponse<ProductDto>.Fail(CommonErrorCodes.ValidationFailed, ex.Message));
        }
    }

    [HttpDelete("products/{id:guid}/image")]
    [RequirePermission(PermissionCodes.InventoryManage)]
    public async Task<ActionResult<ApiResponse<ProductDto>>> ClearImage(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var product = await _bar.SetProductImageUrlAsync(id, null, GetEmployeeId(), cancellationToken);
            return Ok(ApiResponse<ProductDto>.Ok(product));
        }
        catch (Exception ex) when (ex is InvalidOperationException or KeyNotFoundException)
        {
            return BadRequest(ApiResponse<ProductDto>.Fail(CommonErrorCodes.ValidationFailed, ex.Message));
        }
    }

    [HttpPost("products/{id:guid}/stock")]
    [RequirePermission(PermissionCodes.InventoryManage)]
    public async Task<ActionResult<ApiResponse<ProductDto>>> AdjustStock(
        Guid id,
        [FromBody] AdjustStockRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var product = await _bar.AdjustStockAsync(id, request, GetEmployeeId(), cancellationToken);
            return Ok(ApiResponse<ProductDto>.Ok(product));
        }
        catch (Exception ex) when (ex is InvalidOperationException or KeyNotFoundException)
        {
            return BadRequest(ApiResponse<ProductDto>.Fail(CommonErrorCodes.ValidationFailed, ex.Message));
        }
    }

    [HttpGet("orders")]
    [RequirePermission(PermissionCodes.BarView, PermissionCodes.BarOrders, PermissionCodes.BarSell)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<BarOrderDto>>>> Orders(CancellationToken cancellationToken)
    {
        var list = await _bar.GetOpenOrdersAsync(null, cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<BarOrderDto>>.Ok(list));
    }

    [HttpPost("orders")]
    [RequirePermission(PermissionCodes.BarSell)]
    public async Task<ActionResult<ApiResponse<BarOrderDto>>> CreateOrder(
        [FromBody] CreateBarOrderRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var order = await _bar.CreateOrderAsync(request, GetEmployeeId(), cancellationToken);
            return Ok(ApiResponse<BarOrderDto>.Ok(order));
        }
        catch (Exception ex) when (ex is InvalidOperationException or KeyNotFoundException)
        {
            return BadRequest(ApiResponse<BarOrderDto>.Fail(CommonErrorCodes.ValidationFailed, ex.Message));
        }
    }

    [HttpPost("orders/{id:guid}/status")]
    [RequirePermission(PermissionCodes.BarOrders)]
    public async Task<ActionResult<ApiResponse<BarOrderDto>>> UpdateStatus(
        Guid id,
        [FromBody] UpdateBarOrderStatusRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var order = await _bar.UpdateOrderStatusAsync(id, request, GetEmployeeId(), cancellationToken);
            return Ok(ApiResponse<BarOrderDto>.Ok(order));
        }
        catch (Exception ex) when (ex is InvalidOperationException or KeyNotFoundException)
        {
            return BadRequest(ApiResponse<BarOrderDto>.Fail(CommonErrorCodes.ValidationFailed, ex.Message));
        }
    }

    [HttpPost("sell")]
    [RequirePermission(PermissionCodes.BarSell)]
    public async Task<ActionResult<ApiResponse<ReceiptDto>>> Sell(
        [FromBody] SellBarItemsRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var receipt = await _bar.SellItemsAsync(request, GetEmployeeId(), cancellationToken);
            return Ok(ApiResponse<ReceiptDto>.Ok(receipt));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<ReceiptDto>.Fail(CommonErrorCodes.ValidationFailed, ex.Message));
        }
    }

    private Guid GetEmployeeId()
    {
        var raw = User.FindFirstValue("employee_id") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.Parse(raw!);
    }
}
