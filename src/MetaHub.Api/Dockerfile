# Build the MetaHub API as a container image.
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Restore first for better layer caching (copy every project the API references).
COPY MetaHub.sln ./
COPY src/MetaHub.Domain/*.csproj src/MetaHub.Domain/
COPY src/MetaHub.Infrastructure/*.csproj src/MetaHub.Infrastructure/
COPY src/MetaHub.Ingest/*.csproj src/MetaHub.Ingest/
COPY src/MetaHub.Identification/*.csproj src/MetaHub.Identification/
COPY src/MetaHub.Enrichment/*.csproj src/MetaHub.Enrichment/
COPY src/MetaHub.Export/*.csproj src/MetaHub.Export/
COPY src/MetaHub.Api/*.csproj src/MetaHub.Api/
RUN dotnet restore src/MetaHub.Api/MetaHub.Api.csproj

COPY . .
RUN dotnet publish src/MetaHub.Api/MetaHub.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
COPY --from=build /app ./
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "MetaHub.Api.dll"]
