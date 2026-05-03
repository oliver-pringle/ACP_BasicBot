FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY BasicBot.Api.csproj ./
RUN dotnet restore
COPY . ./
RUN dotnet publish -c Release -o /app /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app ./

# The aspnet base image ships with a non-root `app` user (UID is exposed via
# $APP_UID, currently 1654 in .NET 10). Drop privileges before running.
# NOTE: the SQLite bind-mount target /data must be writable by this UID on the
# host — see the README for the one-time `chown 1654:1654 ./data` step.
USER $APP_UID
EXPOSE 5000

ENTRYPOINT ["dotnet", "BasicBot.Api.dll"]
