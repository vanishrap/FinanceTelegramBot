using System.Net.Http.Headers;
using System.Text;
using FinanceBot.Application;
using FinanceBot.Infrastructure.OpenAI;
using FinanceBot.Infrastructure.Persistence;
using FinanceBot.Infrastructure.Services;
using FinanceBot.Infrastructure.Telegram;
using FinanceBot.Worker.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Serilog;

var builder=Host.CreateApplicationBuilder(args);
// `dotnet run` does not load .env files by itself. Load it for local development,
// then re-add process variables so Docker/Railway configuration has precedence.
builder.Configuration.AddInMemoryCollection(DotEnv.LoadNearest(Directory.GetCurrentDirectory(), AppContext.BaseDirectory));
builder.Configuration.AddEnvironmentVariables();
static string Required(IConfiguration c,string name) => c[name] ?? throw new InvalidOperationException($"{name} is required.");
static string Value(IConfiguration c,string name,string fallback) => c[name] ?? c[$"Finance:{name}"] ?? fallback;
var allowed=Value(builder.Configuration,"ALLOWED_TELEGRAM_USER_IDS",string.Join(',',builder.Configuration.GetSection("Finance:AllowedTelegramUserIds").Get<string[]>()??[])).Split(',',StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries).Select(long.Parse).ToHashSet();
var options=new FinanceOptions { TelegramBotToken=Required(builder.Configuration,"TELEGRAM_BOT_TOKEN"), OpenAiApiKey=Required(builder.Configuration,"OPENAI_API_KEY"), AllowedTelegramUserIds=allowed, OpenAiModel=Value(builder.Configuration,"OPENAI_MODEL","gpt-4.1-mini"), OpenAiTranscriptionModel=Value(builder.Configuration,"OPENAI_TRANSCRIPTION_MODEL","gpt-4o-mini-transcribe"), MessageBatchDelaySeconds=int.Parse(Value(builder.Configuration,"MESSAGE_BATCH_DELAY_SECONDS","60")), DatabasePath=Value(builder.Configuration,"DATABASE_PATH","/data/finance.db"), DefaultCurrency=Value(builder.Configuration,"DEFAULT_CURRENCY","MYR"), ValidationRoundingTolerance=decimal.Parse(Value(builder.Configuration,"VALIDATION_ROUNDING_TOLERANCE","0.05"),System.Globalization.CultureInfo.InvariantCulture), OpenAiAttemptTimeoutSeconds=int.Parse(Value(builder.Configuration,"OPENAI_ATTEMPT_TIMEOUT_SECONDS","280")), OpenAiTotalTimeoutSeconds=int.Parse(Value(builder.Configuration,"OPENAI_TOTAL_TIMEOUT_SECONDS","300")) };
if(options.OpenAiAttemptTimeoutSeconds<=0||options.OpenAiTotalTimeoutSeconds<=options.OpenAiAttemptTimeoutSeconds) throw new InvalidOperationException("OPENAI_TOTAL_TIMEOUT_SECONDS must be greater than the positive OPENAI_ATTEMPT_TIMEOUT_SECONDS.");
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(options.DatabasePath))!);
var logDirectory=Path.GetFullPath(Value(builder.Configuration,"LOG_DIRECTORY","/data/logs"));
Directory.CreateDirectory(logDirectory);
builder.Logging.ClearProviders();
builder.Services.AddSerilog((_,configuration)=>configuration
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console(
        restrictedToMinimumLevel:Serilog.Events.LogEventLevel.Error,
        standardErrorFromLevel:Serilog.Events.LogEventLevel.Error)
    .WriteTo.File(
        Path.Combine(logDirectory,"finance-bot-.log"),
        encoding:new UTF8Encoding(encoderShouldEmitUTF8Identifier:true),
        rollingInterval:RollingInterval.Day,
        retainedFileCountLimit:null,
        retainedFileTimeLimit:TimeSpan.FromDays(7)));
builder.Services.AddSingleton(options);
builder.Services.AddPooledDbContextFactory<FinanceDbContext>(x=>x.UseSqlite($"Data Source={options.DatabasePath};Foreign Keys=True;Default Timeout=30"));
builder.Services.AddHttpClient<ITelegramClient,TelegramHttpClient>(x=>x.BaseAddress=new("https://api.telegram.org/")).AddStandardResilienceHandler(resilience=>
{
    // Telegram long polling waits up to 25 seconds. The standard 10-second attempt
    // timeout would cancel healthy getUpdates requests before Telegram can answer.
    resilience.AttemptTimeout.Timeout=TimeSpan.FromSeconds(40);
    // The circuit-breaker sampling window must be at least twice the attempt
    // timeout; the standard 30-second window is invalid with the timeout above.
    resilience.CircuitBreaker.SamplingDuration=TimeSpan.FromSeconds(90);
    resilience.TotalRequestTimeout.Timeout=TimeSpan.FromMinutes(2);
});
builder.Services.AddHttpClient<IVoiceTranscriptionService,VoiceTranscriptionService>(x=>ConfigureOpenAiClient(x,options)).AddStandardResilienceHandler(resilience=>ConfigureOpenAiTimeouts(resilience,options));
builder.Services.AddHttpClient<IAiExtractionService,AiExtractionService>(x=>ConfigureOpenAiClient(x,options)).AddStandardResilienceHandler(resilience=>ConfigureOpenAiTimeouts(resilience,options));
builder.Services.AddHttpClient<IAiAnalyticsService,AiAnalyticsService>(x=>ConfigureOpenAiClient(x,options)).AddStandardResilienceHandler(resilience=>ConfigureOpenAiTimeouts(resilience,options));
builder.Services.AddSingleton<IAnalyticsQueryExecutor,AnalyticsQueryExecutor>();
builder.Services.AddSingleton<MessageBatchService>();builder.Services.AddSingleton<AccountCommandService>();builder.Services.AddSingleton<BatchProcessor>();builder.Services.AddHostedService<PollingWorker>();builder.Services.AddHostedService<BatchProcessingWorker>();
var host=builder.Build();
await using(var scope=host.Services.CreateAsyncScope()){var factory=scope.ServiceProvider.GetRequiredService<IDbContextFactory<FinanceDbContext>>();await using var db=await factory.CreateDbContextAsync();await db.Database.MigrateAsync();}
await host.RunAsync();

static void ConfigureOpenAiClient(HttpClient client,FinanceOptions options)
{
    client.BaseAddress=new("https://api.openai.com/v1/");
    client.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",options.OpenAiApiKey);
    // HttpClient's 100-second default timeout otherwise cancels the request before
    // the explicitly configured resilience timeouts can retry or report it.
    client.Timeout=Timeout.InfiniteTimeSpan;
}

static void ConfigureOpenAiTimeouts(Microsoft.Extensions.Http.Resilience.HttpStandardResilienceOptions resilience,FinanceOptions options)
{
    var attemptTimeout=TimeSpan.FromSeconds(options.OpenAiAttemptTimeoutSeconds);
    resilience.AttemptTimeout.Timeout=attemptTimeout;
    resilience.TotalRequestTimeout.Timeout=TimeSpan.FromSeconds(options.OpenAiTotalTimeoutSeconds);
    // Required by the standard handler: the sampling window must be at least
    // twice the attempt timeout when slow, otherwise healthy AI calls trip it.
    resilience.CircuitBreaker.SamplingDuration=attemptTimeout+attemptTimeout+TimeSpan.FromSeconds(30);
}
