using System.Security.Cryptography;
using System.Text;

namespace ShiftClub.Shared.Contracts.Customers;

public sealed record SetCustomerCredentialsRequest(
    string? Login,
    string? Password,
    string? Pin,
    bool ClearPassword = false,
    bool ClearPin = false);
