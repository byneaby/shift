using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using ShiftClub.Application.Abstractions;
using ShiftClub.Domain.Entities;
using ShiftClub.Infrastructure.Persistence;
using ShiftClub.Infrastructure.SignalR;
using ShiftClub.Shared.Contracts.Bar;
using ShiftClub.Shared.Contracts.Cash;
using ShiftClub.Shared.Contracts.Settings;
using ShiftClub.Shared.Enums;
using ShiftClub.Shared.SignalR;

namespace ShiftClub.Infrastructure.Services;

public sealed class BarService : IBarService
{
    private readonly ShiftClubDbContext _db;
    private readonly ICashService _cash;
    private readonly ICustomerService _customers;
    private readonly IDocumentNumberService _numbers;
    private readonly IHubContext<StaffHub> _staffHub;
    private readonly ITelegramAlertSink _telegramAlerts;
    private readonly ICustomerTelegramNotifySink _customerNotify;

    public BarService(
        ShiftClubDbContext db,
        ICashService cash,
        ICustomerService customers,
        IDocumentNumberService numbers,
        IHubContext<StaffHub> staffHub,
        ITelegramAlertSink telegramAlerts,
        ICustomerTelegramNotifySink customerNotify)
    {
        _db = db;
        _cash = cash;
        _customers = customers;
        _numbers = numbers;
        _staffHub = staffHub;
        _telegramAlerts = telegramAlerts;
        _customerNotify = customerNotify;
    }

    public async Task<IReadOnlyList<ProductCategoryDto>> GetCategoriesAsync(
        Guid? branchId,
        bool includeInactive = false,
        CancellationToken cancellationToken = default)
    {
        var query = _db.ProductCategories.AsNoTracking().AsQueryable();
        if (!includeInactive)
            query = query.Where(c => c.IsActive);
        if (branchId.HasValue)
            query = query.Where(c => c.BranchId == branchId.Value);

        var list = await query.OrderBy(c => c.SortOrder).ThenBy(c => c.Name).ToListAsync(cancellationToken);
        return list.Select(c => new ProductCategoryDto(c.Id, c.Name, c.Code, c.SortOrder, c.IsActive)).ToList();
    }

    public async Task<ProductCategoryDto> CreateCategoryAsync(
        Guid branchId,
        CreateProductCategoryRequest request,
        Guid employeeId,
        CancellationToken cancellationToken = default)
    {
        var name = (request.Name ?? "").Trim();
        if (name.Length == 0)
            throw new InvalidOperationException("Укажите название категории.");

        var code = string.IsNullOrWhiteSpace(request.Code)
            ? TranslitCode(name)
            : request.Code.Trim().ToUpperInvariant();

        var exists = await _db.ProductCategories.AnyAsync(
            c => c.BranchId == branchId && c.Code == code,
            cancellationToken);
        if (exists)
            throw new InvalidOperationException($"Категория с кодом «{code}» уже есть.");

        var category = new ProductCategory
        {
            BranchId = branchId,
            Name = name,
            Code = code,
            SortOrder = request.SortOrder,
            IsActive = true,
            CreatedBy = employeeId,
            UpdatedBy = employeeId
        };
        _db.ProductCategories.Add(category);
        await _db.SaveChangesAsync(cancellationToken);
        return new ProductCategoryDto(category.Id, category.Name, category.Code, category.SortOrder, category.IsActive);
    }

    public async Task<ProductCategoryDto> UpdateCategoryAsync(
        Guid categoryId,
        UpdateProductCategoryRequest request,
        Guid employeeId,
        CancellationToken cancellationToken = default)
    {
        var category = await _db.ProductCategories
            .FirstOrDefaultAsync(c => c.Id == categoryId, cancellationToken)
            ?? throw new KeyNotFoundException("Категория не найдена.");

        if (request.Name is not null)
        {
            var name = request.Name.Trim();
            if (name.Length == 0)
                throw new InvalidOperationException("Название не может быть пустым.");
            category.Name = name;
        }

        if (request.Code is not null)
        {
            var code = request.Code.Trim().ToUpperInvariant();
            if (code.Length == 0)
                throw new InvalidOperationException("Код не может быть пустым.");
            var clash = await _db.ProductCategories.AnyAsync(
                c => c.BranchId == category.BranchId && c.Code == code && c.Id != category.Id,
                cancellationToken);
            if (clash)
                throw new InvalidOperationException($"Категория с кодом «{code}» уже есть.");
            category.Code = code;
        }

        if (request.SortOrder is not null)
            category.SortOrder = request.SortOrder.Value;
        if (request.IsActive is not null)
            category.IsActive = request.IsActive.Value;

        category.UpdatedAt = DateTimeOffset.UtcNow;
        category.UpdatedBy = employeeId;
        await _db.SaveChangesAsync(cancellationToken);
        return new ProductCategoryDto(category.Id, category.Name, category.Code, category.SortOrder, category.IsActive);
    }

    public async Task DeleteCategoryAsync(Guid categoryId, Guid employeeId, CancellationToken cancellationToken = default)
    {
        var category = await _db.ProductCategories
            .FirstOrDefaultAsync(c => c.Id == categoryId, cancellationToken)
            ?? throw new KeyNotFoundException("Категория не найдена.");

        var productCount = await _db.Products.CountAsync(p => p.CategoryId == categoryId, cancellationToken);
        if (productCount > 0)
        {
            // Soft-delete: keep FK integrity
            category.IsActive = false;
            category.UpdatedAt = DateTimeOffset.UtcNow;
            category.UpdatedBy = employeeId;
            await _db.SaveChangesAsync(cancellationToken);
            return;
        }

        _db.ProductCategories.Remove(category);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ProductDto>> GetProductsAsync(
        Guid? branchId,
        Guid? categoryId,
        bool includeInactive = false,
        CancellationToken cancellationToken = default)
    {
        var query = _db.Products.AsNoTracking().Include(p => p.Category).AsQueryable();
        if (!includeInactive)
            query = query.Where(p => p.IsActive);
        if (branchId.HasValue)
            query = query.Where(p => p.BranchId == branchId.Value);
        if (categoryId.HasValue)
            query = query.Where(p => p.CategoryId == categoryId.Value);

        var list = await query.OrderBy(p => p.Name).ToListAsync(cancellationToken);
        return list.Select(MapProduct).ToList();
    }

    public async Task<ProductDto> CreateProductAsync(
        Guid branchId,
        CreateProductRequest request,
        Guid employeeId,
        CancellationToken cancellationToken = default)
    {
        var category = await _db.ProductCategories
            .FirstOrDefaultAsync(c => c.Id == request.CategoryId && c.BranchId == branchId, cancellationToken)
            ?? throw new KeyNotFoundException("Category not found.");

        if (request.SalePrice < 0 || request.CostPrice < 0 || request.InitialStock < 0)
            throw new InvalidOperationException("Prices and stock cannot be negative.");

        var product = new Product
        {
            BranchId = branchId,
            CategoryId = category.Id,
            Name = request.Name.Trim(),
            Sku = request.Sku.Trim().ToUpperInvariant(),
            Barcode = request.Barcode,
            Unit = string.IsNullOrWhiteSpace(request.Unit) ? "шт" : request.Unit.Trim(),
            CostPrice = request.CostPrice,
            SalePrice = request.SalePrice,
            StockQty = request.InitialStock,
            MinStockQty = request.MinStockQty,
            IsActive = true,
            ImageUrl = string.IsNullOrWhiteSpace(request.ImageUrl) ? null : request.ImageUrl.Trim()
        };

        _db.Products.Add(product);
        await _db.SaveChangesAsync(cancellationToken);

        if (request.InitialStock > 0)
        {
            _db.InventoryMovements.Add(new InventoryMovement
            {
                BranchId = branchId,
                ProductId = product.Id,
                Type = InventoryMovementType.Purchase,
                QuantityDelta = request.InitialStock,
                StockAfter = product.StockQty,
                UnitCost = request.CostPrice,
                EmployeeId = employeeId,
                Comment = "Начальный остаток"
            });
            await _db.SaveChangesAsync(cancellationToken);
        }

        product.Category = category;
        return MapProduct(product);
    }

    public async Task<ProductDto> AdjustStockAsync(
        Guid productId,
        AdjustStockRequest request,
        Guid employeeId,
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            var exists = await _db.InventoryMovements.AsNoTracking()
                .AnyAsync(m => m.IdempotencyKey == request.IdempotencyKey, cancellationToken);
            if (exists)
            {
                var p = await _db.Products.AsNoTracking().Include(x => x.Category)
                    .FirstAsync(x => x.Id == productId, cancellationToken);
                return MapProduct(p);
            }
        }

        var product = await _db.Products.Include(p => p.Category)
            .FirstOrDefaultAsync(p => p.Id == productId, cancellationToken)
            ?? throw new KeyNotFoundException("Product not found.");

        var next = product.StockQty + request.QuantityDelta;
        if (next < 0)
            throw new InvalidOperationException("Недостаточно остатка.");

        product.StockQty = next;
        product.UpdatedAt = DateTimeOffset.UtcNow;
        product.UpdatedBy = employeeId;

        _db.InventoryMovements.Add(new InventoryMovement
        {
            BranchId = product.BranchId,
            ProductId = product.Id,
            Type = request.Type,
            QuantityDelta = request.QuantityDelta,
            StockAfter = next,
            UnitCost = product.CostPrice,
            EmployeeId = employeeId,
            Comment = request.Comment,
            IdempotencyKey = request.IdempotencyKey
        });

        await _db.SaveChangesAsync(cancellationToken);
        return MapProduct(product);
    }

    public async Task<ProductDto> UpdateProductAsync(
        Guid productId,
        UpdateProductRequest request,
        Guid employeeId,
        CancellationToken cancellationToken = default)
    {
        var product = await _db.Products.Include(p => p.Category)
            .FirstOrDefaultAsync(p => p.Id == productId, cancellationToken)
            ?? throw new KeyNotFoundException("Product not found.");

        if (request.Name is not null)
            product.Name = request.Name.Trim();
        if (request.Sku is not null)
            product.Sku = request.Sku.Trim().ToUpperInvariant();
        if (request.Barcode is not null)
            product.Barcode = string.IsNullOrWhiteSpace(request.Barcode) ? null : request.Barcode.Trim();
        if (request.Unit is not null)
            product.Unit = string.IsNullOrWhiteSpace(request.Unit) ? product.Unit : request.Unit.Trim();
        if (request.CostPrice is not null)
        {
            if (request.CostPrice < 0) throw new InvalidOperationException("Себестоимость не может быть отрицательной.");
            product.CostPrice = request.CostPrice.Value;
        }
        if (request.SalePrice is not null)
        {
            if (request.SalePrice < 0) throw new InvalidOperationException("Цена не может быть отрицательной.");
            product.SalePrice = request.SalePrice.Value;
        }
        if (request.MinStockQty is not null)
            product.MinStockQty = request.MinStockQty.Value;
        if (request.IsActive is not null)
            product.IsActive = request.IsActive.Value;
        if (request.ImageUrl is not null)
            product.ImageUrl = string.IsNullOrWhiteSpace(request.ImageUrl) ? null : request.ImageUrl.Trim();
        if (request.CategoryId is Guid newCatId)
        {
            var category = await _db.ProductCategories
                .FirstOrDefaultAsync(c => c.Id == newCatId && c.BranchId == product.BranchId, cancellationToken)
                ?? throw new KeyNotFoundException("Категория не найдена.");
            product.CategoryId = category.Id;
            product.Category = category;
        }

        product.UpdatedAt = DateTimeOffset.UtcNow;
        product.UpdatedBy = employeeId;
        await _db.SaveChangesAsync(cancellationToken);
        await _db.Entry(product).Reference(p => p.Category).LoadAsync(cancellationToken);
        return MapProduct(product);
    }

    public async Task DeleteProductAsync(Guid productId, Guid employeeId, CancellationToken cancellationToken = default)
    {
        var product = await _db.Products
            .FirstOrDefaultAsync(p => p.Id == productId, cancellationToken)
            ?? throw new KeyNotFoundException("Товар не найден.");

        var usedInOrders = await _db.BarOrderItems.AnyAsync(i => i.ProductId == productId, cancellationToken);
        var hasMovements = await _db.InventoryMovements.AnyAsync(m => m.ProductId == productId, cancellationToken);

        if (usedInOrders || hasMovements)
        {
            product.IsActive = false;
            product.UpdatedAt = DateTimeOffset.UtcNow;
            product.UpdatedBy = employeeId;
            await _db.SaveChangesAsync(cancellationToken);
            return;
        }

        _db.Products.Remove(product);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<ProductDto> SetProductImageUrlAsync(
        Guid productId,
        string? imageUrl,
        Guid employeeId,
        CancellationToken cancellationToken = default)
    {
        var product = await _db.Products.Include(p => p.Category)
            .FirstOrDefaultAsync(p => p.Id == productId, cancellationToken)
            ?? throw new KeyNotFoundException("Product not found.");

        product.ImageUrl = string.IsNullOrWhiteSpace(imageUrl) ? null : imageUrl.Trim();
        product.UpdatedAt = DateTimeOffset.UtcNow;
        product.UpdatedBy = employeeId;
        await _db.SaveChangesAsync(cancellationToken);
        return MapProduct(product);
    }

    public async Task<BarOrderDto> CreateOrderAsync(
        CreateBarOrderRequest request,
        Guid? employeeId,
        CancellationToken cancellationToken = default)
    {
        if (request.Items is null || request.Items.Count == 0)
            throw new InvalidOperationException("Order must contain items.");

        if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            var existing = await _db.BarOrders.AsNoTracking()
                .Include(o => o.Items)
                .FirstOrDefaultAsync(o => o.IdempotencyKey == request.IdempotencyKey, cancellationToken);
            if (existing is not null)
                return await MapOrderAsync(existing, cancellationToken);
        }

        var branchId = await _db.Branches.Select(b => b.Id).FirstAsync(cancellationToken);
        string? computerName = null;
        if (request.ComputerId.HasValue)
        {
            var computer = await _db.Computers.AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == request.ComputerId.Value, cancellationToken)
                ?? throw new KeyNotFoundException("Computer not found.");
            branchId = computer.BranchId;
            computerName = computer.DisplayName ?? computer.WindowsName;
        }

        var productIds = request.Items.Select(i => i.ProductId).Distinct().ToList();
        var products = await _db.Products.Where(p => productIds.Contains(p.Id) && p.IsActive)
            .ToDictionaryAsync(p => p.Id, cancellationToken);

        if (products.Count != productIds.Count)
            throw new InvalidOperationException("One or more products not found.");

        var order = new BarOrder
        {
            BranchId = branchId,
            Number = await _numbers.NextAsync(branchId, DocumentSequenceType.BarOrder, cancellationToken),
            Status = BarOrderStatus.New,
            PaymentMode = request.PaymentMode,
            ComputerId = request.ComputerId,
            GamingSessionId = request.GamingSessionId,
            CreatedByEmployeeId = employeeId,
            Comment = request.Comment,
            IdempotencyKey = request.IdempotencyKey
        };

        if (request.GamingSessionId is Guid gsId)
        {
            order.CustomerId = await _db.GamingSessions.AsNoTracking()
                .Where(s => s.Id == gsId)
                .Select(s => s.CustomerId)
                .FirstOrDefaultAsync(cancellationToken);
        }

        foreach (var item in request.Items)
        {
            if (item.Quantity <= 0)
                throw new InvalidOperationException("Quantity must be positive.");
            var product = products[item.ProductId];
            if (product.StockQty < item.Quantity)
                throw new InvalidOperationException($"Недостаточно остатка: {product.Name}");

            var line = Math.Round(product.SalePrice * item.Quantity, 2, MidpointRounding.AwayFromZero);
            order.Items.Add(new BarOrderItem
            {
                ProductId = product.Id,
                ProductName = product.Name,
                Quantity = item.Quantity,
                UnitPrice = product.SalePrice,
                LineTotal = line
            });
            order.Total += line;
        }

        _db.BarOrders.Add(order);
        await _db.SaveChangesAsync(cancellationToken);

        var dto = await MapOrderAsync(order, cancellationToken, computerName);
        await _staffHub.Clients.Group(HubGroups.Staff())
            .SendAsync(HubMethods.OrderCreated, dto, cancellationToken);
        await _staffHub.Clients.Group(HubGroups.Staff()).SendAsync(
            HubMethods.StaffToast,
            new
            {
                kind = "bar",
                title = "Новый заказ бара",
                body = computerName is null
                    ? $"{dto.Number} · {dto.Total:0} ₸"
                    : $"{computerName}: {dto.Number} · {dto.Total:0} ₸",
                sound = true,
                sticky = true,
                orderId = dto.Id,
                computerId = dto.ComputerId,
                at = DateTimeOffset.UtcNow
            },
            cancellationToken);

        var items = string.Join(", ", dto.Items.Select(i => $"{i.ProductName}×{i.Quantity:0}"));
        await _telegramAlerts.PublishAsync(
            new StaffAlertMessage(
                "bar",
                "Новый заказ бара",
                computerName is null
                    ? $"{dto.Number} · {items} · {dto.Total:0} ₸"
                    : $"{computerName}: {dto.Number} · {items} · {dto.Total:0} ₸",
                dto.ComputerId,
                computerName,
                dto.Id),
            cancellationToken);
        return dto;
    }

    public async Task<IReadOnlyList<BarOrderDto>> GetOpenOrdersAsync(
        Guid? branchId,
        CancellationToken cancellationToken = default)
    {
        var query = _db.BarOrders.AsNoTracking()
            .Include(o => o.Items)
            .Where(o => o.Status != BarOrderStatus.Completed
                        && o.Status != BarOrderStatus.Cancelled
                        && o.Status != BarOrderStatus.Rejected);
        if (branchId.HasValue)
            query = query.Where(o => o.BranchId == branchId.Value);

        var list = await query.OrderByDescending(o => o.CreatedAt).Take(100).ToListAsync(cancellationToken);
        var result = new List<BarOrderDto>();
        foreach (var order in list)
            result.Add(await MapOrderAsync(order, cancellationToken));
        return result;
    }

    public async Task<BarOrderDto> UpdateOrderStatusAsync(
        Guid orderId,
        UpdateBarOrderStatusRequest request,
        Guid employeeId,
        CancellationToken cancellationToken = default)
    {
        var order = await _db.BarOrders.Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken)
            ?? throw new KeyNotFoundException("Order not found.");

        if (order.Status is BarOrderStatus.Completed or BarOrderStatus.Cancelled or BarOrderStatus.Rejected)
            throw new InvalidOperationException($"Заказ уже в статусе {order.Status}.");

        order.Status = request.Status;
        order.AssignedEmployeeId ??= employeeId;
        order.UpdatedAt = DateTimeOffset.UtcNow;
        order.UpdatedBy = employeeId;

        switch (request.Status)
        {
            case BarOrderStatus.Accepted:
                order.AcceptedAt = DateTimeOffset.UtcNow;
                break;
            case BarOrderStatus.Ready:
                order.ReadyAt = DateTimeOffset.UtcNow;
                if (order.CustomerId is Guid barCid)
                {
                    var tg = await _db.Customers.AsNoTracking()
                        .Where(c => c.Id == barCid && c.AllowNotifications && c.TelegramUserId != null)
                        .Select(c => new { c.TelegramUserId, Name = c.FirstName })
                        .FirstOrDefaultAsync(cancellationToken);
                    if (tg?.TelegramUserId is long tgId)
                    {
                        var items = string.Join(", ", order.Items.Select(i => i.ProductName));
                        await _customerNotify.PublishAsync(
                            new CustomerTelegramNotice(
                                tgId,
                                $"🍹 <b>Бар готов!</b>\n{System.Net.WebUtility.HtmlEncode(items)}\nЗаберите у стойки."),
                            cancellationToken);
                    }
                }
                break;
            case BarOrderStatus.Completed:
                await FulfillOrderAsync(order, employeeId, request.PaymentMethod, cancellationToken);
                order.CompletedAt = DateTimeOffset.UtcNow;
                break;
            case BarOrderStatus.Cancelled:
            case BarOrderStatus.Rejected:
                break;
        }

        await _db.SaveChangesAsync(cancellationToken);
        var dto = await MapOrderAsync(order, cancellationToken);
        await _staffHub.Clients.Group(HubGroups.Staff())
            .SendAsync(HubMethods.OrderStatusChanged, dto, cancellationToken);
        return dto;
    }

    public async Task<ReceiptDto> SellItemsAsync(
        SellBarItemsRequest request,
        Guid employeeId,
        CancellationToken cancellationToken = default)
    {
        if (request.Items is null || request.Items.Count == 0)
            throw new InvalidOperationException("Sale must contain items.");

        if (request.PaymentMethod is PaymentMethod.Free or PaymentMethod.Postpay)
            throw new InvalidOperationException("Для продажи бара выберите наличные, карту, Kaspi QR, смешанную или баланс.");

        if (request.PaymentMethod == PaymentMethod.Mixed && (request.Payments is null || request.Payments.Count < 2))
            throw new InvalidOperationException("Смешанная оплата: укажите суммы частей (нал + Kaspi).");

        await _cash.EnsureEmployeeHasOpenShiftAsync(employeeId, cancellationToken);

        await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);

        var productIds = request.Items.Select(i => i.ProductId).Distinct().ToList();
        var products = await _db.Products.Where(p => productIds.Contains(p.Id) && p.IsActive)
            .ToDictionaryAsync(p => p.Id, cancellationToken);
        if (products.Count != productIds.Count)
            throw new InvalidOperationException("One or more products not found.");

        var saleItems = new List<CreateSaleItemRequest>();
        decimal total = 0;

        foreach (var item in request.Items)
        {
            if (item.Quantity <= 0)
                throw new InvalidOperationException("Quantity must be positive.");

            var product = products[item.ProductId];
            if (product.StockQty < item.Quantity)
                throw new InvalidOperationException($"Недостаточно остатка: {product.Name}");

            var line = Math.Round(product.SalePrice * item.Quantity, 2, MidpointRounding.AwayFromZero);
            total += line;
            saleItems.Add(new CreateSaleItemRequest(
                ReceiptItemType.Product,
                product.Name,
                item.Quantity,
                product.SalePrice,
                0,
                product.Id));

            await DeductStockAsync(
                product,
                item.Quantity,
                InventoryMovementType.Sale,
                employeeId,
                request.GamingSessionId,
                request.IdempotencyKey is null ? null : $"sale-{request.IdempotencyKey}-{product.Id}",
                cancellationToken);
        }

        Guid? customerId = request.CustomerId;
        if (customerId is null && request.GamingSessionId is Guid sid)
        {
            customerId = await _db.GamingSessions.AsNoTracking()
                .Where(s => s.Id == sid)
                .Select(s => s.CustomerId)
                .FirstOrDefaultAsync(cancellationToken);
        }

        if (request.PaymentMethod == PaymentMethod.Balance && customerId is null)
            throw new InvalidOperationException("Для оплаты бара с баланса нужен клиент сеанса.");

        var receipt = await _cash.CreateSaleAsync(new CreateSaleRequest(
            request.ComputerId,
            request.GamingSessionId,
            request.Comment ?? "Продажа бара",
            request.IdempotencyKey,
            saleItems,
            request.PaymentMethod == PaymentMethod.Balance
                ? [new PaymentPartDto(PaymentMethod.Balance, total)]
                : PaymentParts.Resolve(request.PaymentMethod, total, request.Payments),
            customerId), employeeId, cancellationToken);

        if (request.PaymentMethod == PaymentMethod.Balance && customerId is Guid cid && total > 0)
        {
            await _customers.ChargeFromWalletAsync(
                cid,
                total,
                LedgerTransactionType.ProductPurchase,
                request.GamingSessionId,
                receipt.Id,
                employeeId,
                request.IdempotencyKey is null ? null : $"bar-sell-balance-{request.IdempotencyKey}",
                request.Comment ?? "Бар",
                cancellationToken);
        }

        await tx.CommitAsync(cancellationToken);
        return receipt;
    }

    private async Task FulfillOrderAsync(
        BarOrder order,
        Guid employeeId,
        PaymentMethod? paidWith,
        CancellationToken cancellationToken)
    {
        if (order.StockDeducted)
            return;

        // Оплата с баланса уже списана при создании заказа — только склад.
        if (order.PaymentMode == BarOrderPaymentMode.Balance)
        {
            foreach (var item in order.Items)
            {
                var product = await _db.Products.FirstAsync(p => p.Id == item.ProductId, cancellationToken);
                if (product.StockQty < item.Quantity)
                    throw new InvalidOperationException($"Недостаточно остатка: {product.Name}");

                await DeductStockAsync(
                    product,
                    item.Quantity,
                    InventoryMovementType.OrderFulfillment,
                    employeeId,
                    order.Id,
                    $"order-{order.Id}-{product.Id}",
                    cancellationToken);
            }

            order.StockDeducted = true;
            return;
        }

        // «К сеансу» → списать с кошелька клиента сеанса (бонусы+баланс), без кассового чека.
        if (order.PaymentMode == BarOrderPaymentMode.ChargeToSession)
        {
            Guid? customerId = order.CustomerId;
            if (customerId is null && order.GamingSessionId is Guid sid)
            {
                customerId = await _db.GamingSessions.AsNoTracking()
                    .Where(s => s.Id == sid)
                    .Select(s => s.CustomerId)
                    .FirstOrDefaultAsync(cancellationToken);
            }

            if (customerId is null)
                throw new InvalidOperationException(
                    "Оплата «к сеансу» доступна только при сеансе с аккаунтом. Выберите оплату у кассы.");

            foreach (var item in order.Items)
            {
                var product = await _db.Products.FirstAsync(p => p.Id == item.ProductId, cancellationToken);
                if (product.StockQty < item.Quantity)
                    throw new InvalidOperationException($"Недостаточно остатка: {product.Name}");

                await DeductStockAsync(
                    product,
                    item.Quantity,
                    InventoryMovementType.OrderFulfillment,
                    employeeId,
                    order.Id,
                    $"order-{order.Id}-{product.Id}",
                    cancellationToken);
            }

            await _customers.ChargeFromWalletAsync(
                customerId.Value,
                order.Total,
                LedgerTransactionType.ProductPurchase,
                order.GamingSessionId,
                null,
                employeeId,
                $"bar-order-charge-{order.Id}",
                $"Бар {order.Number}",
                cancellationToken);

            order.StockDeducted = true;
            return;
        }

        await _cash.EnsureEmployeeHasOpenShiftAsync(employeeId, cancellationToken);

        var saleItems = new List<CreateSaleItemRequest>();
        foreach (var item in order.Items)
        {
            var product = await _db.Products.FirstAsync(p => p.Id == item.ProductId, cancellationToken);
            if (product.StockQty < item.Quantity)
                throw new InvalidOperationException($"Недостаточно остатка: {product.Name}");

            await DeductStockAsync(
                product,
                item.Quantity,
                InventoryMovementType.OrderFulfillment,
                employeeId,
                order.Id,
                $"order-{order.Id}-{product.Id}",
                cancellationToken);

            saleItems.Add(new CreateSaleItemRequest(
                ReceiptItemType.Product,
                item.ProductName,
                item.Quantity,
                item.UnitPrice,
                0,
                item.ProductId));
        }

        if (paidWith is not null
            and not PaymentMethod.Cash
            and not PaymentMethod.KaspiQr
            and not PaymentMethod.Card)
            throw new InvalidOperationException("Заказ бара оплачивается наличными или Kaspi QR");

        var method = paidWith ?? order.PaymentMode switch
        {
            BarOrderPaymentMode.CardOnDelivery => PaymentMethod.Card,
            BarOrderPaymentMode.KaspiQrOnDelivery => PaymentMethod.KaspiQr,
            BarOrderPaymentMode.CashOnDelivery => PaymentMethod.Cash,
            _ => PaymentMethod.Cash // PayAtCashier и пр. — у кассы наличными по умолчанию
        };

        var receipt = await _cash.CreateSaleAsync(new CreateSaleRequest(
            order.ComputerId,
            order.GamingSessionId,
            $"Заказ бара {order.Number}",
            $"bar-order-{order.Id}",
            saleItems,
            [new PaymentPartDto(method, order.Total)]), employeeId, cancellationToken);

        order.ReceiptId = receipt.Id;
        order.StockDeducted = true;
    }

    private async Task DeductStockAsync(
        Product product,
        decimal qty,
        InventoryMovementType type,
        Guid employeeId,
        Guid? referenceId,
        string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            var exists = await _db.InventoryMovements
                .AnyAsync(m => m.IdempotencyKey == idempotencyKey, cancellationToken);
            if (exists)
                return;
        }

        product.StockQty -= qty;
        if (product.StockQty < 0)
            throw new InvalidOperationException($"Недостаточно остатка: {product.Name}");

        _db.InventoryMovements.Add(new InventoryMovement
        {
            BranchId = product.BranchId,
            ProductId = product.Id,
            Type = type,
            QuantityDelta = -qty,
            StockAfter = product.StockQty,
            UnitCost = product.CostPrice,
            EmployeeId = employeeId,
            ReferenceId = referenceId,
            IdempotencyKey = idempotencyKey,
            Comment = type.ToString()
        });

        await _db.SaveChangesAsync(cancellationToken);
    }

    private static ProductDto MapProduct(Product p) => new(
        p.Id,
        p.CategoryId,
        p.Category?.Name ?? string.Empty,
        p.Name,
        p.Sku,
        p.Barcode,
        p.Unit,
        p.CostPrice,
        p.SalePrice,
        p.StockQty,
        p.MinStockQty,
        p.IsActive,
        p.StockQty <= p.MinStockQty,
        p.ImageUrl);

    private async Task<BarOrderDto> MapOrderAsync(
        BarOrder order,
        CancellationToken cancellationToken,
        string? computerName = null)
    {
        if (computerName is null && order.ComputerId.HasValue)
        {
            computerName = await _db.Computers.AsNoTracking()
                .Where(c => c.Id == order.ComputerId)
                .Select(c => c.DisplayName ?? c.WindowsName)
                .FirstOrDefaultAsync(cancellationToken);
        }

        return new BarOrderDto(
            order.Id,
            order.Number,
            order.Status,
            order.PaymentMode,
            order.ComputerId,
            computerName,
            order.Total,
            order.Comment,
            order.CreatedAt,
            order.AcceptedAt,
            order.ReadyAt,
            order.CompletedAt,
            order.ReceiptId,
            order.Items.Select(i => new BarOrderItemDto(i.Id, i.ProductId, i.ProductName, i.Quantity, i.UnitPrice, i.LineTotal)).ToList());
    }

    private static string TranslitCode(string name)
    {
        var map = new Dictionary<char, string>
        {
            ['а'] = "A", ['б'] = "B", ['в'] = "V", ['г'] = "G", ['д'] = "D", ['е'] = "E", ['ё'] = "E",
            ['ж'] = "ZH", ['з'] = "Z", ['и'] = "I", ['й'] = "Y", ['к'] = "K", ['л'] = "L", ['м'] = "M",
            ['н'] = "N", ['о'] = "O", ['п'] = "P", ['р'] = "R", ['с'] = "S", ['т'] = "T", ['у'] = "U",
            ['ф'] = "F", ['х'] = "H", ['ц'] = "C", ['ч'] = "CH", ['ш'] = "SH", ['щ'] = "SCH", ['ъ'] = "",
            ['ы'] = "Y", ['ь'] = "", ['э'] = "E", ['ю'] = "YU", ['я'] = "YA"
        };
        var sb = new System.Text.StringBuilder();
        foreach (var ch in name.Trim().ToLowerInvariant())
        {
            if (map.TryGetValue(ch, out var t))
                sb.Append(t);
            else if (char.IsAsciiLetterOrDigit(ch))
                sb.Append(char.ToUpperInvariant(ch));
            else if (ch is ' ' or '-' or '_')
                sb.Append('-');
        }

        var code = sb.ToString().Trim('-');
        if (code.Length == 0)
            code = "CAT";
        if (code.Length > 24)
            code = code[..24];
        return code;
    }
}
