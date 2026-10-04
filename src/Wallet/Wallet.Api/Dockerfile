FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY src/Contracts/Contracts.csproj src/Contracts/
COPY src/Wallet/Wallet.Domain/Wallet.Domain.csproj src/Wallet/Wallet.Domain/
COPY src/Wallet/Wallet.Application/Wallet.Application.csproj src/Wallet/Wallet.Application/
COPY src/Wallet/Wallet.Infrastructure/Wallet.Infrastructure.csproj src/Wallet/Wallet.Infrastructure/
COPY src/Wallet/Wallet.Api/Wallet.Api.csproj src/Wallet/Wallet.Api/
RUN dotnet restore src/Wallet/Wallet.Api/Wallet.Api.csproj
COPY src/Contracts/ src/Contracts/
COPY src/Wallet/Wallet.Domain/ src/Wallet/Wallet.Domain/
COPY src/Wallet/Wallet.Application/ src/Wallet/Wallet.Application/
COPY src/Wallet/Wallet.Infrastructure/ src/Wallet/Wallet.Infrastructure/
COPY src/Wallet/Wallet.Api/ src/Wallet/Wallet.Api/
RUN dotnet publish src/Wallet/Wallet.Api/Wallet.Api.csproj -c Release -o /out --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0
USER root
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
USER app
WORKDIR /app
COPY --from=build /out .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Wallet.Api.dll"]
