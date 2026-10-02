# Сборка из корня репозитория: docker compose build (нужен Club.Core)
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Club.Core/ Club.Core/
COPY Phone/ Phone/
RUN dotnet publish Phone/Phone.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "Phone.dll"]
