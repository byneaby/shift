namespace ShiftClub.Shared.Enums;

public enum ComputerStatus
{
    Offline = 0,
    Online = 1,
    Free = 2,
    Locked = 3,
    InSession = 4,
    Reserved = 5,
    Starting = 6,
    Ending = 7,
    Maintenance = 8,
    Updating = 9,
    Error = 10,
    PendingApproval = 11
}

/// <summary>ПК с агентом Shell vs консоль (PS5 и т.п.) — только учёт времени вручную.</summary>
public enum StationKind
{
    Pc = 0,
    Console = 1
}

public enum ComputerCommandType
{
    Lock = 0,
    Unlock = 1,
    Shutdown = 2,
    Restart = 3,
    LogOff = 4,
    StartShell = 5,
    RestartShell = 6,
    RestartService = 7,
    ShowMessage = 8,
    PlayNotification = 9,
    LaunchApplication = 10,
    CloseApplication = 11,
    ReloadConfiguration = 12,
    UpdateClient = 13,
    GetDiagnostics = 14,
    EnterMaintenance = 15,
    EndSession = 16,
    /// <summary>Открыть диспетчер задач оболочки на ПК (+ вернуть список процессов).</summary>
    OpenTaskManager = 17,
    /// <summary>Выйти из аккаунта клиента (вход выполнен на другом ПК).</summary>
    ForceCustomerLogout = 18,
    /// <summary>Экстренное сообщение поверх всех окон и игр (напр. «ВОЕНКОМАТ»), красный фон + звук.</summary>
    EmergencyAlert = 19,
    /// <summary>Выполнить строку командной строки (cmd.exe /c) на ПК; результат в ResultJson.</summary>
    RunCmd = 20
}

public enum ComputerCommandStatus
{
    Created = 0,
    Sent = 1,
    Received = 2,
    Executing = 3,
    Completed = 4,
    Failed = 5,
    Expired = 6,
    Cancelled = 7
}
