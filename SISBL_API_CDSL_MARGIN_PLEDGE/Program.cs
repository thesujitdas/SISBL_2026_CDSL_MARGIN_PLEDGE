using Microsoft.AspNetCore.Authentication.Cookies;
using Polly;
using Polly.Extensions.Http;
using SISBL_API_CDSL_MARGIN_PLEDGE.Classes;
using SISBL_API_CDSL_MARGIN_PLEDGE.Interfaces;

var builder = WebApplication.CreateBuilder(args);

var sisblConfig = builder.Configuration.GetSection("SISBL");
var isLive = (sisblConfig.GetValue<string>("IsLIVE") == "1");

builder.Services.AddControllers();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options => { });

builder.Services.Configure<HttpRequestRetryTracker>(
    builder.Configuration.GetSection(HttpRequestRetryTracker.OurTracker));

builder.Services.AddHttpClient("MarginRepledge", httpClient =>
{
    if (isLive)
        httpClient.BaseAddress = new Uri(sisblConfig.GetValue<string>("CDSL_MarginRepledge_BaseAddress_LIVE"));
    else httpClient.BaseAddress = new Uri(sisblConfig.GetValue<string>("CDSL_MarginRepledge_BaseAddress_UAT"));
    httpClient.DefaultRequestHeaders.Add("version", sisblConfig.GetValue<string>("CDSL_MarginRepledge_HTTP_Header_Version"));
    httpClient.Timeout = TimeSpan.FromMinutes(1.0);
}).AddPolicyHandler(GetRetryPolicy());

builder.Services.AddSingleton<SISBL_Validator>();
builder.Services.AddSingleton<IMarginRepledgeClientProcess, MarginRepledgeClientProcess>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddCors(policyBuilder =>
    policyBuilder.AddDefaultPolicy(policy =>
        policy.WithOrigins("*").AllowAnyHeader().AllowAnyHeader())
);



var app = builder.Build();
app.UseHttpsRedirection();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.Run();


/* POLICIES */
static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy()
{
    Random jitterer = new Random();
    return HttpPolicyExtensions
        .HandleTransientHttpError()
        .OrResult(msg => msg.StatusCode == System.Net.HttpStatusCode.NotFound)
        .WaitAndRetryAsync(3,
            retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt))
                          + TimeSpan.FromMilliseconds(jitterer.Next(100, 1000))
            );
}