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
They are collected into one batch until 60 seconds have passed without a new message; every new
message resets that delay. If an account, category, date, or another material detail is missing or
ambiguous, the bot asks a specific follow-up question and keeps the batch. The user's reply reopens
that same batch, so the original messages and attachments remain available. An explicitly stated
transaction date is stored; otherwise the first message timestamp is used.

Natural-language questions are handled as analytics instead of transactions. The AI can build a
read-only query for arbitrary periods, merchants, categories, accounts, receipts, and combinations
of them. The query is executed with the Telegram user's internal ID, strict read-only validation,
a timeout, and a row cap. Its JSON result is then returned to the AI together with the original
conversation so it can produce a readable Russian answer. Different currencies are never summed
together.

## Architecture

* `FinanceBot.Domain` contains the ledger, receipt, debt, input and audit model.
* `FinanceBot.Application` contains ports, DTOs and deterministic validation.
* `FinanceBot.Infrastructure` contains EF Core, Telegram and OpenAI adapters and batch processing.
* `FinanceBot.Worker` hosts polling, durable batch scheduling and migration startup.

Account balances are derived from `AccountMovements`; balance adjustments are ledger entries and
never income. AI output is schema-constrained and cannot access the database directly.
