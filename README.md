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
OpenAI calls allow up to five minutes for an AI request by default, so large
receipts are not aborted by the standard HTTP handler's ten-second timeout. Override these limits
with `OPENAI_ATTEMPT_TIMEOUT_SECONDS` and `OPENAI_TOTAL_TIMEOUT_SECONDS` when necessary; the total
must be greater than the per-attempt timeout.

## Telegram commands

* `/account <name> <currency> <cash|bank|other>` creates a personal account. The name may contain spaces, for example `/account Main card MYR bank`.
* `/accounts` lists personal accounts and their current ledger balances.
* `/help` shows the available commands in Telegram.

Messages that are not commands continue through the normal AI transaction extraction flow.
They are collected into one batch until 60 seconds have passed without a new message; every new
message resets that delay. If an account, category, date, or another material detail is missing or
ambiguous, the bot asks a specific follow-up question and keeps the batch. The user's reply reopens
that same batch, so the original messages and attachments remain available. An explicitly stated
transaction date is stored; otherwise the current date and time in Malaysia (UTC+08:00) is used.
If one message describes several operations, each operation is extracted, validated, and recorded
separately in one atomic batch—for example, taxi and restaurant expenses become two transactions,
not one combined expense and not a sequence of unnecessary clarification questions.
If deterministic receipt totals do not match, the bot sends the exact validation differences back
to the AI and retries extraction up to two times with the original messages and receipt images. It
does not ask the user to diagnose arithmetic extracted from a receipt.
Every non-zero receipt item is categorized independently by what the product is used for—not by
the merchant or the transaction-wide category. Generic root categories such as `Покупки` are
rejected for receipt items and automatically sent back to the AI for more specific classification.
The taxonomy covers transport ownership and rentals, food, healthcare, housing, personal goods,
entertainment, education, communications, travel, family care, financial costs, miscellaneous
spending, and common income sources such as dividends, rent, bonuses, and insurance payouts.

Existing transactions can also be corrected or deleted in natural language. Corrections use the
transaction ID shown in the recording confirmation and retain unchanged fields from recent history.
Deletion is deliberately limited to one exact, user-owned transaction ID per request: broad filters
and AI-generated delete queries are never executed. Every correction and deletion is recorded in
the audit log with the previous value.

An open payable debt can be reclassified as an expense by its debt ID. The bot copies the stored
amount, currency, description, and date, then cancels the original debt so it is not counted twice.

The bot also records debts from natural language, including the amount, currency, counterparty, and
direction. `Payable` means that the user owes the counterparty; `Receivable` means that the
counterparty owes the user. If either party or the direction is unclear, the bot asks a follow-up
question instead of guessing.

Natural-language questions are handled as analytics instead of transactions. The AI can build a
read-only query for arbitrary periods, merchants, categories, accounts, receipts, and combinations
of them. The query is executed with the Telegram user's internal ID, strict read-only validation,
a timeout, and a row cap. Its JSON result is then returned to the AI together with the original
conversation so it can produce a readable Russian answer. Different currencies are never summed
together. AI answers are sent through `SendRichMessageAsync` and formatted as concise Telegram Rich
Markdown. Analytics SQL uses the exact stored enum casing and explicit Malaysia `+08:00` half-open
date ranges. If a query reports no data while recent transactions exist, the bot automatically
returns the SQL and empty result to the planner once to repair date, enum, join, or ownership filters.
Receipt analytics joins `Transactions → Receipts → ReceiptItems`, uses the receipt date when present,
and can report individual products, quantities, item discounts, taxes, service charges, and item-level
categories. Recent owned receipts and items are included in planning context for references such as
“последний чек”; transaction totals are not duplicated across their one-to-many detail rows.
Rich Markdown supports headings, emphasis, lists, quotes, separators, and tables where they improve
readability. If Telegram rejects malformed AI-generated Markdown, the bot retries as plain text so
the answer is not lost.

## Architecture

* `FinanceBot.Domain` contains the ledger, receipt, debt, input and audit model.
* `FinanceBot.Application` contains ports, DTOs and deterministic validation.
* `FinanceBot.Infrastructure` contains EF Core, Telegram and OpenAI adapters and batch processing.
* `FinanceBot.Worker` hosts polling, durable batch scheduling and migration startup.

Account balances are derived from `AccountMovements`; balance adjustments are ledger entries and
never income. AI output is schema-constrained and cannot access the database directly.
