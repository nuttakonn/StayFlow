# Build context: repo root
# docker build -t stayflow-api .

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy project files first for layer caching
COPY src/backend/StayFlow.Api/StayFlow.Api.csproj src/backend/StayFlow.Api/
COPY src/backend/StayFlow.BuildingBlocks/StayFlow.BuildingBlocks.csproj src/backend/StayFlow.BuildingBlocks/

RUN dotnet restore src/backend/StayFlow.Api/StayFlow.Api.csproj

# Copy remaining source
COPY src/backend/ src/backend/

RUN dotnet publish src/backend/StayFlow.Api/StayFlow.Api.csproj \
    -c Release \
    -o /app/publish \
    --no-restore

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "StayFlow.Api.dll"]
