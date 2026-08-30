# Finance Telegram Bot

Production-oriented personal and family finance bot. Messages are durably grouped in SQLite,
then transcribed/extracted with OpenAI and committed only after deterministic validation.

## Run locally

```bash
cp .env.example .env
docker build -t finance-bot .
docker run --env-file .env -v finance-data:/data finance-bot
```

On Railway, mount a volume at `/data` and configure the variables from `.env.example`.
Only Telegram users listed in `ALLOWED_TELEGRAM_USER_IDS` are accepted; all other updates are
acknowledged without being persisted.

## Architecture

* `FinanceBot.Domain` contains the ledger, receipt, debt, input and audit model.
* `FinanceBot.Application` contains ports, DTOs and deterministic validation.
* `FinanceBot.Infrastructure` contains EF Core, Telegram and OpenAI adapters and batch processing.
* `FinanceBot.Worker` hosts polling, durable batch scheduling and migration startup.

Account balances are derived from `AccountMovements`; balance adjustments are ledger entries and
never income. AI output is schema-constrained and cannot access the database directly.
