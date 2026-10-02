FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY src/Contracts/Contracts.csproj src/Contracts/
COPY src/Users.Domain/Users.Domain.csproj src/Users.Domain/
COPY src/Users.Application/Users.Application.csproj src/Users.Application/
COPY src/Users.Infrastructure/Users.Infrastructure.csproj src/Users.Infrastructure/
COPY src/Users.Api/Users.Api.csproj src/Users.Api/
RUN dotnet restore src/Users.Api/Users.Api.csproj
COPY src/Contracts/ src/Contracts/
COPY src/Users.Domain/ src/Users.Domain/
COPY src/Users.Application/ src/Users.Application/
COPY src/Users.Infrastructure/ src/Users.Infrastructure/
COPY src/Users.Api/ src/Users.Api/
RUN dotnet publish src/Users.Api/Users.Api.csproj -c Release -o /out --no-restore

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
ENTRYPOINT ["dotnet", "Users.Api.dll"]
