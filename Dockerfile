FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore src/CashFlow.Web/CashFlow.Web.csproj
RUN dotnet publish src/CashFlow.Web/CashFlow.Web.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app
RUN apt-get update && apt-get install -y --no-install-recommends tzdata fontconfig && rm -rf /var/lib/apt/lists/*
ENV ASPNETCORE_URLS=http://+:8080 TZ=Europe/Moscow CASHFLOW_TZ=Europe/Moscow
COPY --from=build /app .
EXPOSE 8080
ENTRYPOINT ["dotnet", "CashFlow.Web.dll"]
