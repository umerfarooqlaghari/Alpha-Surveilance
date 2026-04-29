# Build Stage
FROM mcr.microsoft.com/dotnet/sdk:10.0-preview AS build
WORKDIR /src

# Copy project files and restore
COPY ["violation-management-service-api/violation-management-api/violation-management-api.csproj", "violation-management-service-api/violation-management-api/"]
# Copy shared requirements (Protos from other folders)
COPY ["audit-services-api/audit-api/Protos/", "audit-services-api/audit-api/Protos/"]
COPY ["alpha-surveilance-bff/Protos/", "alpha-surveilance-bff/Protos/"]

RUN dotnet restore "violation-management-service-api/violation-management-api/violation-management-api.csproj"

# Copy everything else
COPY . .
WORKDIR "/src/violation-management-service-api/violation-management-api"
RUN dotnet build "violation-management-api.csproj" -c Release -o /app/build

# Publish Stage
FROM build AS publish
RUN dotnet publish "violation-management-api.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Runtime Stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0-preview AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "violation-management-api.dll"]
