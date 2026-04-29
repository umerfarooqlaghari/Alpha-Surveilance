# Build Stage
FROM mcr.microsoft.com/dotnet/sdk:10.0-preview AS build
WORKDIR /src

# Copy project files and restore
COPY ["audit-services-api/audit-api/audit-api.csproj", "audit-services-api/audit-api/"]
RUN dotnet restore "audit-services-api/audit-api/audit-api.csproj"

# Copy everything else
COPY . .
WORKDIR "/src/audit-services-api/audit-api"
RUN dotnet build "audit-api.csproj" -c Release -o /app/build

# Publish Stage
FROM build AS publish
RUN dotnet publish "audit-api.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Runtime Stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0-preview AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "audit-api.dll"]
