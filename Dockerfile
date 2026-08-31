FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore FinanceBot.sln && dotnet publish src/FinanceBot.Worker/FinanceBot.Worker.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/runtime:8.0-alpine AS final
RUN apk add --no-cache icu-libs && mkdir -p /data && chown app:app /data
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false DATABASE_PATH=/data/finance.db
WORKDIR /app
COPY --from=build --chown=app:app /app .
USER app
ENTRYPOINT ["dotnet", "FinanceBot.Worker.dll"]
