using System.Net.Http.Headers;
using FinanceBot.Application;
using FinanceBot.Infrastructure.OpenAI;
using FinanceBot.Infrastructure.Persistence;
using FinanceBot.Infrastructure.Services;
using FinanceBot.Infrastructure.Telegram;
using FinanceBot.Worker.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

var builder=Host.CreateApplicationBuilder(args);
// `dotnet run` does not load .env files by itself. Load it for local development,
// then re-add process variables so Docker/Railway configuration has precedence.
builder.Configuration.AddInMemoryCollection(DotEnv.LoadNearest(Directory.GetCurrentDirectory(), AppContext.BaseDirectory));
builder.Configuration.AddEnvironmentVariables();
static string Required(IConfiguration c,string name) => c[name] ?? throw new InvalidOperationException($"{name} is required.");
static string Value(IConfiguration c,string name,string fallback) => c[name] ?? c[$"Finance:{name}"] ?? fallback;
var allowed=Value(builder.Configuration,"ALLOWED_TELEGRAM_USER_IDS",string.Join(',',builder.Configuration.GetSection("Finance:AllowedTelegramUserIds").Get<string[]>()??[])).Split(',',StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries).Select(long.Parse).ToHashSet();
var options=new FinanceOptions { TelegramBotToken=Required(builder.Configuration,"TELEGRAM_BOT_TOKEN"), OpenAiApiKey=Required(builder.Configuration,"OPENAI_API_KEY"), AllowedTelegramUserIds=allowed, OpenAiModel=Value(builder.Configuration,"OPENAI_MODEL","gpt-4.1-mini"), OpenAiTranscriptionModel=Value(builder.Configuration,"OPENAI_TRANSCRIPTION_MODEL","gpt-4o-mini-transcribe"), MessageBatchDelaySeconds=int.Parse(Value(builder.Configuration,"MESSAGE_BATCH_DELAY_SECONDS","300")), DatabasePath=Value(builder.Configuration,"DATABASE_PATH","/data/finance.db"), DefaultCurrency=Value(builder.Configuration,"DEFAULT_CURRENCY","MYR"), ValidationRoundingTolerance=decimal.Parse(Value(builder.Configuration,"VALIDATION_ROUNDING_TOLERANCE","0.05"),System.Globalization.CultureInfo.InvariantCulture) };
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(options.DatabasePath))!);
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
builder.Services.AddHttpClient<IVoiceTranscriptionService,VoiceTranscriptionService>(x=>{x.BaseAddress=new("https://api.openai.com/v1/");x.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",options.OpenAiApiKey);}).AddStandardResilienceHandler();
builder.Services.AddHttpClient<IAiExtractionService,AiExtractionService>(x=>{x.BaseAddress=new("https://api.openai.com/v1/");x.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",options.OpenAiApiKey);}).AddStandardResilienceHandler();
builder.Services.AddSingleton<MessageBatchService>();builder.Services.AddSingleton<BatchProcessor>();builder.Services.AddHostedService<PollingWorker>();
var host=builder.Build();
await using(var scope=host.Services.CreateAsyncScope()){var factory=scope.ServiceProvider.GetRequiredService<IDbContextFactory<FinanceDbContext>>();await using var db=await factory.CreateDbContextAsync();await db.Database.MigrateAsync();}
await host.RunAsync();
