using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShiftClub.Application.Abstractions;
using ShiftClub.Domain.Entities;
using ShiftClub.Infrastructure.Persistence;
using ShiftClub.Server.Auth;
using ShiftClub.Shared.Contracts;
using ShiftClub.Shared.Contracts.Customers;

namespace ShiftClub.Server.Controllers;

[ApiController]
[Authorize]
[Route("api/customers")]
public class CustomersController : ControllerBase
{
	private readonly ICustomerService _customers;

	private readonly ShiftClubDbContext _db;

	public CustomersController(ICustomerService customers, ShiftClubDbContext db)
	{
		_customers = customers;
		_db = db;
	}

	[HttpGet]
	[RequirePermission(new string[] { "customers.view", "customers.manage" })]
	public async Task<ActionResult<ApiResponse<IReadOnlyList<CustomerDto>>>> Search([FromQuery] string? q, CancellationToken cancellationToken)
	{
		return Ok(ApiResponse<IReadOnlyList<CustomerDto>>.Ok(
			await _customers.SearchAsync(q, this.ResolveFilter(null), cancellationToken)));
	}

	[HttpGet("{id:guid}")]
	[RequirePermission(new string[] { "customers.view", "customers.manage" })]
	public async Task<ActionResult<ApiResponse<CustomerDto>>> GetById(Guid id, CancellationToken cancellationToken)
	{
		CustomerDto customerDto = await _customers.GetByIdAsync(id, cancellationToken);
		if ((object)customerDto == null)
		{
			return NotFound(ApiResponse<CustomerDto>.Fail("not_found", "Клиент не найден."));
		}
		return Ok(ApiResponse<CustomerDto>.Ok(customerDto));
	}

	[HttpPost]
	[RequirePermission(new string[] { "customers.manage" })]
	public async Task<ActionResult<ApiResponse<CustomerDto>>> Create([FromBody] CreateCustomerRequest request, CancellationToken cancellationToken)
	{
		_ = 1;
		try
		{
			Guid branchId = await this.ResolveBranchIdAsync(_db, null, cancellationToken);
			return Ok(ApiResponse<CustomerDto>.Ok(await _customers.CreateAsync(request, branchId, GetEmployeeId(), cancellationToken)));
		}
		catch (Exception ex) when (((ex is InvalidOperationException || ex is KeyNotFoundException) ? 1 : 0) != 0)
		{
			return BadRequest(ApiResponse<CustomerDto>.Fail("validation_failed", ex.Message));
		}
	}

	[HttpPut("{id:guid}")]
	[RequirePermission(new string[] { "customers.manage" })]
	public async Task<ActionResult<ApiResponse<CustomerDto>>> Update(Guid id, [FromBody] UpdateCustomerRequest request, CancellationToken cancellationToken)
	{
		try
		{
			return Ok(ApiResponse<CustomerDto>.Ok(await _customers.UpdateAsync(id, request, GetEmployeeId(), cancellationToken)));
		}
		catch (KeyNotFoundException ex)
		{
			return NotFound(ApiResponse<CustomerDto>.Fail("not_found", ex.Message));
		}
		catch (InvalidOperationException ex2)
		{
			return BadRequest(ApiResponse<CustomerDto>.Fail("validation_failed", ex2.Message));
		}
	}

	[HttpGet("{id:guid}/deposit-quote")]
	[RequirePermission(new string[] { "customers.deposit", "customers.manage" })]
	public async Task<ActionResult<ApiResponse<CustomerDepositQuoteDto>>> DepositQuote(Guid id, [FromQuery] decimal amount, CancellationToken cancellationToken)
	{
		try
		{
			return Ok(ApiResponse<CustomerDepositQuoteDto>.Ok(await _customers.QuoteDepositAsync(id, amount, cancellationToken)));
		}
		catch (KeyNotFoundException ex)
		{
			return NotFound(ApiResponse<CustomerDepositQuoteDto>.Fail("not_found", ex.Message));
		}
		catch (InvalidOperationException ex2)
		{
			return BadRequest(ApiResponse<CustomerDepositQuoteDto>.Fail("validation_failed", ex2.Message));
		}
	}

	[HttpPost("{id:guid}/deposit")]
	[RequirePermission(new string[] { "customers.deposit", "customers.manage" })]
	public async Task<ActionResult<ApiResponse<CustomerDepositResultDto>>> Deposit(Guid id, [FromBody] DepositCustomerRequest request, CancellationToken cancellationToken)
	{
		try
		{
			return Ok(ApiResponse<CustomerDepositResultDto>.Ok(await _customers.DepositAsync(id, request, GetEmployeeId(), cancellationToken)));
		}
		catch (KeyNotFoundException ex)
		{
			return NotFound(ApiResponse<CustomerDepositResultDto>.Fail("not_found", ex.Message));
		}
		catch (InvalidOperationException ex2)
		{
			return BadRequest(ApiResponse<CustomerDepositResultDto>.Fail("validation_failed", ex2.Message));
		}
	}

	[HttpPost("{id:guid}/adjust")]
	[RequirePermission(new string[] { "customers.adjust" })]
	public async Task<ActionResult<ApiResponse<CustomerDto>>> Adjust(Guid id, [FromBody] AdjustCustomerBalanceRequest request, CancellationToken cancellationToken)
	{
		try
		{
			return Ok(ApiResponse<CustomerDto>.Ok(await _customers.AdjustAsync(id, request, GetEmployeeId(), cancellationToken)));
		}
		catch (KeyNotFoundException ex)
		{
			return NotFound(ApiResponse<CustomerDto>.Fail("not_found", ex.Message));
		}
		catch (InvalidOperationException ex2)
		{
			return BadRequest(ApiResponse<CustomerDto>.Fail("validation_failed", ex2.Message));
		}
	}

	[HttpPost("{id:guid}/time-bank/adjust")]
	[RequirePermission(new string[] { "customers.adjust", "customers.manage" })]
	public async Task<ActionResult<ApiResponse<CustomerDto>>> AdjustTimeBank(Guid id, [FromBody] AdjustCustomerTimeBankRequest request, CancellationToken cancellationToken)
	{
		try
		{
			return Ok(ApiResponse<CustomerDto>.Ok(await _customers.AdjustTimeBankAsync(id, request, GetEmployeeId(), cancellationToken)));
		}
		catch (KeyNotFoundException ex)
		{
			return NotFound(ApiResponse<CustomerDto>.Fail("not_found", ex.Message));
		}
		catch (InvalidOperationException ex2)
		{
			return BadRequest(ApiResponse<CustomerDto>.Fail("validation_failed", ex2.Message));
		}
	}

	[HttpGet("{id:guid}/transactions")]
	[RequirePermission(new string[] { "customers.view", "customers.manage" })]
	public async Task<ActionResult<ApiResponse<IReadOnlyList<CustomerBalanceTransactionDto>>>> Transactions(Guid id, [FromQuery] int take = 50, CancellationToken cancellationToken = default(CancellationToken))
	{
		return Ok(ApiResponse<IReadOnlyList<CustomerBalanceTransactionDto>>.Ok(await _customers.GetTransactionsAsync(id, take, cancellationToken)));
	}

	[HttpGet("{id:guid}/time-bank/transactions")]
	[RequirePermission(new string[] { "customers.view", "customers.manage" })]
	public async Task<ActionResult<ApiResponse<IReadOnlyList<CustomerTimeBankTransactionDto>>>> TimeBankTransactions(Guid id, [FromQuery] int take = 50, CancellationToken cancellationToken = default(CancellationToken))
	{
		return Ok(ApiResponse<IReadOnlyList<CustomerTimeBankTransactionDto>>.Ok(await _customers.GetTimeBankTransactionsAsync(id, take, cancellationToken)));
	}

	[HttpPost("{id:guid}/credentials")]
	[RequirePermission(new string[] { "customers.manage" })]
	public async Task<ActionResult<ApiResponse<CustomerDto>>> SetCredentials(Guid id, [FromBody] SetCustomerCredentialsRequest request, CancellationToken cancellationToken)
	{
		try
		{
			return Ok(ApiResponse<CustomerDto>.Ok(await _customers.SetCredentialsAsync(id, request, GetEmployeeId(), cancellationToken)));
		}
		catch (KeyNotFoundException ex)
		{
			return NotFound(ApiResponse<CustomerDto>.Fail("not_found", ex.Message));
		}
		catch (InvalidOperationException ex2)
		{
			return BadRequest(ApiResponse<CustomerDto>.Fail("validation_failed", ex2.Message));
		}
	}

	private Guid GetEmployeeId()
	{
		string input = base.User.FindFirstValue("employee_id") ?? base.User.FindFirstValue("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier");
		return Guid.Parse(input);
	}
}
