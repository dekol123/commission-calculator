FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY src/Contracts/Contracts.csproj src/Contracts/
COPY src/Accrual/Accrual.Domain/Accrual.Domain.csproj src/Accrual/Accrual.Domain/
COPY src/Accrual/Accrual.Application/Accrual.Application.csproj src/Accrual/Accrual.Application/
COPY src/Accrual/Accrual.Infrastructure/Accrual.Infrastructure.csproj src/Accrual/Accrual.Infrastructure/
COPY src/Accrual/Accrual.Api/Accrual.Api.csproj src/Accrual/Accrual.Api/
RUN dotnet restore src/Accrual/Accrual.Api/Accrual.Api.csproj
COPY src/Contracts/ src/Contracts/
COPY src/Accrual/Accrual.Domain/ src/Accrual/Accrual.Domain/
COPY src/Accrual/Accrual.Application/ src/Accrual/Accrual.Application/
COPY src/Accrual/Accrual.Infrastructure/ src/Accrual/Accrual.Infrastructure/
COPY src/Accrual/Accrual.Api/ src/Accrual/Accrual.Api/
RUN dotnet publish src/Accrual/Accrual.Api/Accrual.Api.csproj -c Release -o /out --no-restore

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
ENTRYPOINT ["dotnet", "Accrual.Api.dll"]
