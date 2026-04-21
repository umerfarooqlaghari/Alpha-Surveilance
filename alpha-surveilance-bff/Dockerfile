# Build Stage
FROM mcr.microsoft.com/dotnet/sdk:10.0-preview-alpine AS build
WORKDIR /src

# Copy project files and restore
COPY ["alpha-surveilance-bff/alpha-surveilance-bff.csproj", "alpha-surveilance-bff/"]
# Copy shared requirements (Protos from other folders)
COPY ["audit-services-api/audit-api/Protos/", "audit-services-api/audit-api/Protos/"]

RUN dotnet restore "alpha-surveilance-bff/alpha-surveilance-bff.csproj"

# Copy everything else
COPY . .
WORKDIR "/src/alpha-surveilance-bff"
RUN dotnet build "alpha-surveilance-bff.csproj" -c Release -o /app/build

# Publish Stage
FROM build AS publish
RUN dotnet publish "alpha-surveilance-bff.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Runtime Stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0-preview-alpine AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "alpha-surveilance-bff.dll"]
