FROM mcr.microsoft.com/dotnet/runtime:6.0.23-alpine3.18-arm32v7 AS base

ENV ASPNET_VERSION=6.0.23

# Install ASP.NET Core
RUN wget -O aspnetcore.tar.gz https://dotnetcli.azureedge.net/dotnet/aspnetcore/Runtime/$ASPNET_VERSION/aspnetcore-runtime-$ASPNET_VERSION-linux-musl-arm.tar.gz \
    && aspnetcore_sha512='558f085c04f73ef740f9a43c8dffbab6c7fb463cbf0ddeb551bce138808389acc1a0d2f7c858fc815a5b3b8bc56fd561237830633db810ca9ca7265b7303cc10' \
    && echo "$aspnetcore_sha512  aspnetcore.tar.gz" | sha512sum -c - \
    && tar -oxzf aspnetcore.tar.gz -C /usr/share/dotnet ./shared/Microsoft.AspNetCore.App \
    && rm aspnetcore.tar.gz

EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["BackupServerDaemon/BackupServerDaemon.csproj", "BackupServerDaemon/"]
RUN dotnet restore "BackupServerDaemon/BackupServerDaemon.csproj"
COPY . .
WORKDIR "/src/BackupServerDaemon"
RUN dotnet build "BackupServerDaemon.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
RUN dotnet publish "BackupServerDaemon.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
#COPY --from=publish ["/shared/Microsoft.AspNetCore.App", "/usr/share/dotnet/shared/Microsoft.AspNetCore.App"]
ENTRYPOINT ["dotnet", "BackupServerDaemon.dll"]