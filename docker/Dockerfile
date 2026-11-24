# Multi-stage Docker build for .NET 10
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY A2AProtocol.slnx* A2AProtocol.sln* global.json Directory.Build.props ./
COPY src/McpServer/McpServer.csproj src/McpServer/
COPY src/McpServer.Contracts/McpServer.Contracts.csproj src/McpServer.Contracts/
COPY tests/McpServer.UnitTests/McpServer.UnitTests.csproj tests/McpServer.UnitTests/
COPY tests/McpServer.IntegrationTests/McpServer.IntegrationTests.csproj tests/McpServer.IntegrationTests/

RUN dotnet restore

COPY src/ src/
COPY tests/ tests/

RUN dotnet publish src/McpServer/McpServer.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

USER $APP_UID

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "McpServer.dll"]
