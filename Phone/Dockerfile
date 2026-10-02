# Сборка из корня репозитория: docker compose build (нужен Club.Core)
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Club.Core/ Club.Core/
COPY Phone/ Phone/
RUN dotnet publish Phone/Phone.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
# Npgsql при подключении ищет libgssapi — без неё сыпет ошибки в лог
RUN apt-get update && apt-get install -y --no-install-recommends libgssapi-krb5-2 && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "Phone.dll"]
