using SISBL_RPC_CDSL_MARGIN_PLEDGE.Classes;
using SISBL_RPC_CDSL_MARGIN_PLEDGE.Interfaces;
using SISBL_RPC_CDSL_MARGIN_PLEDGE.Services;

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseWindowsService(options =>
{
    options.ServiceName = "RPC_CDSL_MARGIN_PLEDGE_V1";
});
builder.Services.AddGrpc();
builder.Services.AddSingleton<IMarginPledgeProcess, MarginPledgeProcess>();
builder.Services.AddSingleton<ILogManager, LogManager>();


var app = builder.Build();
app.MapGrpcService<MarginPledgeService>();
app.MapGet("/", () => "Communication with gRPC endpoints must be made through a gRPC client. To learn how to create a client, visit: https://go.microsoft.com/fwlink/?linkid=2086909");
app.Run();
