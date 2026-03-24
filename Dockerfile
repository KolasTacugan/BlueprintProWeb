# This stage defines the base runtime image
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 8080

# This stage is used to build the service project
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["BlueprintProWeb/BlueprintProWeb.csproj", "BlueprintProWeb/"]
RUN dotnet restore "./BlueprintProWeb/BlueprintProWeb.csproj"
COPY . .
WORKDIR "/src/BlueprintProWeb"
RUN dotnet build "./BlueprintProWeb.csproj" -c $BUILD_CONFIGURATION -o /app/build

# This stage is used to publish the service project to be copied to the final stage
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./BlueprintProWeb.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# This stage is used in production
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "BlueprintProWeb.dll"]