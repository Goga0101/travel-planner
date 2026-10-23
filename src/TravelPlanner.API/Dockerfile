# Build context: repository root (see docker-compose.yml)
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY nuget.config global.json TravelPlanner.slnx ./
COPY src/TravelPlanner.Domain/TravelPlanner.Domain.csproj src/TravelPlanner.Domain/
COPY src/TravelPlanner.Application/TravelPlanner.Application.csproj src/TravelPlanner.Application/
COPY src/TravelPlanner.Infrastructure/TravelPlanner.Infrastructure.csproj src/TravelPlanner.Infrastructure/
COPY src/TravelPlanner.API/TravelPlanner.API.csproj src/TravelPlanner.API/
RUN dotnet restore src/TravelPlanner.API/TravelPlanner.API.csproj
COPY src/ src/
RUN dotnet publish src/TravelPlanner.API/TravelPlanner.API.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "TravelPlanner.API.dll"]
