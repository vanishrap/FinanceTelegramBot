# Finance Telegram Bot

Production-oriented personal and family finance bot. Messages are durably grouped in SQLite,
then transcribed/extracted with OpenAI and committed only after deterministic validation.

## Run locally

The worker searches the current directory and its parents for `.env`, so direct CLI and IDE runs
both work. Process environment variables (including Railway variables) take precedence.

### With .NET 8 SDK

```bash
cp .env.example .env
dotnet restore FinanceBot.sln
dotnet run --project src/FinanceBot.Worker/FinanceBot.Worker.csproj
```

### With Docker

```bash
cp .env.example .env
docker build -t finance-bot .
docker run --env-file .env -v finance-data:/data finance-bot
```

On Railway, mount a volume at `/data` and configure the variables from `.env.example`.
Only Telegram users listed in `ALLOWED_TELEGRAM_USER_IDS` are accepted; all other updates are
acknowledged without being persisted.

## Telegram commands

* `/account <name> <currency> <cash|bank|other>` creates a personal account. The name may contain spaces, for example `/account Main card MYR bank`.
* `/accounts` lists personal accounts and their current ledger balances.
* `/help` shows the available commands in Telegram.

Messages that are not commands continue through the normal AI transaction extraction flow.

## Architecture

* `FinanceBot.Domain` contains the ledger, receipt, debt, input and audit model.
* `FinanceBot.Application` contains ports, DTOs and deterministic validation.
* `FinanceBot.Infrastructure` contains EF Core, Telegram and OpenAI adapters and batch processing.
* `FinanceBot.Worker` hosts polling, durable batch scheduling and migration startup.

Account balances are derived from `AccountMovements`; balance adjustments are ledger entries and
never income. AI output is schema-constrained and cannot access the database directly.
