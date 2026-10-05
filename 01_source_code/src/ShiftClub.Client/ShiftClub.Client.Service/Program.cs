using Microsoft.Extensions.Hosting.WindowsServices;
using ShiftClub.Client.Service;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "ShiftClubClient";
});
builder.Services.AddHttpClient("ShiftClub");
builder.Services.AddHostedService<ClientWorker>();

var host = builder.Build();
host.Run();
