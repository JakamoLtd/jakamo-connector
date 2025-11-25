FROM mcr.microsoft.com/dotnet/runtime:8.0 AS base
USER $APP_UID
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Jakamo.Api.Connector/Jakamo.Api.Connector.csproj", "Jakamo.Api.Connector/"]
RUN dotnet restore "Jakamo.Api.Connector/Jakamo.Api.Connector.csproj"
COPY . .
WORKDIR "/src/Jakamo.Api.Connector"
RUN dotnet build "Jakamo.Api.Connector.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "Jakamo.Api.Connector.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Jakamo.Api.Connector.dll"]
