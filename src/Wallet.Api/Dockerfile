FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY src/Contracts/Contracts.csproj src/Contracts/
COPY src/Wallet.Domain/Wallet.Domain.csproj src/Wallet.Domain/
COPY src/Wallet.Application/Wallet.Application.csproj src/Wallet.Application/
COPY src/Wallet.Infrastructure/Wallet.Infrastructure.csproj src/Wallet.Infrastructure/
COPY src/Wallet.Api/Wallet.Api.csproj src/Wallet.Api/
RUN dotnet restore src/Wallet.Api/Wallet.Api.csproj
COPY src/Contracts/ src/Contracts/
COPY src/Wallet.Domain/ src/Wallet.Domain/
COPY src/Wallet.Application/ src/Wallet.Application/
COPY src/Wallet.Infrastructure/ src/Wallet.Infrastructure/
COPY src/Wallet.Api/ src/Wallet.Api/
RUN dotnet publish src/Wallet.Api/Wallet.Api.csproj -c Release -o /out --no-restore

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
