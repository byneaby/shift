using ShiftClub.Domain.Entities;

using ShiftClub.Shared.Enums;



namespace ShiftClub.Application.Computers;



/// <summary>

/// Гостевая занятость ПК (Free/Busy/…) отдельно от связности.

/// Offline без сеанса = свободен («Офлайн» в детали); с сеансом = занят.

/// </summary>

public static class ComputerOccupancy

{

    public static (string Occupancy, string? Detail) Resolve(Computer c)

    {

        var status = c.Status;

        var session = c.CurrentSession;

        var offline = c.StationKind != StationKind.Console
                      && status is ComputerStatus.Offline or ComputerStatus.Error;



        if (!c.IsApproved || status == ComputerStatus.PendingApproval)

            return ("Setup", "Ожидает подтверждения");



        if (status == ComputerStatus.Maintenance || c.IsMaintenance)

            return ("Maintenance", "Техработы");



        if (status == ComputerStatus.Updating)

            return ("Updating", "Обновление");



        // Есть активный/пауза сеанс — занят, даже если Shell офлайн.
        if (session is { Status: SessionStatus.Paused })
            return ("Busy", Combine("Пауза", offline));

        if (status == ComputerStatus.Reserved || session is { Status: SessionStatus.Reserved or SessionStatus.Waiting })
            return ("Reserved", Combine("Бронь", offline));

        // Завершённый сеанс, но ПК ещё с CurrentSessionId / InSession — считаем свободным.
        if (session is { Status: SessionStatus.Completed or SessionStatus.Cancelled })
        {
            if (offline)
                return ("Free", "Офлайн");
            return ("Free", null);
        }

        if (c.CurrentSessionId is not null && session is null)
            return ("Busy", Combine(null, offline));

        if (session is { Status: SessionStatus.Active or SessionStatus.PaymentPending }
            || status is ComputerStatus.InSession or ComputerStatus.Starting or ComputerStatus.Ending)
        {
            if (session?.CustomerId is not null)
                return ("Busy", Combine(DetailFromPayment(session.PaymentMethod, hasAccount: true), offline));

            return ("Busy", Combine(DetailFromPayment(session?.PaymentMethod, hasAccount: false), offline));
        }

        // Нет сеанса → свободен. Offline/Error только как деталь связности.
        if (offline)
            return ("Free", "Офлайн");

        return ("Free", null);

    }



    private static string? Combine(string? detail, bool offline)

    {

        if (!offline)

            return detail;

        if (string.IsNullOrWhiteSpace(detail))

            return "Офлайн";

        return $"{detail} · Офлайн";

    }



    private static string DetailFromPayment(PaymentMethod? method, bool hasAccount)

    {

        if (hasAccount)

            return method == PaymentMethod.Balance ? "Аккаунт · баланс" : "Аккаунт";



        return method switch

        {

            PaymentMethod.Cash => "Чек · наличные",

            PaymentMethod.Card => "Чек · карта",

            PaymentMethod.KaspiQr => "Kaspi QR",

            PaymentMethod.Mixed => "Чек · смешанная",

            PaymentMethod.Free => "Бесплатно",

            PaymentMethod.Transfer => "Перевод",

            PaymentMethod.Balance => "Баланс",

            _ => "Гость"

        };

    }

}


