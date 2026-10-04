FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY src/Contracts/Contracts.csproj src/Contracts/
COPY src/Users/Users.Domain/Users.Domain.csproj src/Users/Users.Domain/
COPY src/Users/Users.Application/Users.Application.csproj src/Users/Users.Application/
COPY src/Users/Users.Infrastructure/Users.Infrastructure.csproj src/Users/Users.Infrastructure/
COPY src/Users/Users.Api/Users.Api.csproj src/Users/Users.Api/
RUN dotnet restore src/Users/Users.Api/Users.Api.csproj
COPY src/Contracts/ src/Contracts/
COPY src/Users/Users.Domain/ src/Users/Users.Domain/
COPY src/Users/Users.Application/ src/Users/Users.Application/
COPY src/Users/Users.Infrastructure/ src/Users/Users.Infrastructure/
COPY src/Users/Users.Api/ src/Users/Users.Api/
RUN dotnet publish src/Users/Users.Api/Users.Api.csproj -c Release -o /out --no-restore

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
