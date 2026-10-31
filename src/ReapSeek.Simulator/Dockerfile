FROM mcr.microsoft.com/dotnet/sdk:10.0-noble AS build
WORKDIR /src
COPY Directory.Build.props ./
COPY ReapSeek.Domain/ReapSeek.Domain.csproj ReapSeek.Domain/
COPY ReapSeek.Shared/ReapSeek.Shared.csproj ReapSeek.Shared/
COPY ReapSeek.Simulator/ReapSeek.Simulator.csproj ReapSeek.Simulator/
RUN dotnet restore ReapSeek.Simulator/ReapSeek.Simulator.csproj
COPY ReapSeek.Domain/ ReapSeek.Domain/
COPY ReapSeek.Shared/ ReapSeek.Shared/
COPY ReapSeek.Simulator/ ReapSeek.Simulator/
RUN dotnet publish ReapSeek.Simulator/ReapSeek.Simulator.csproj -c Release --no-restore -o /app

FROM mcr.microsoft.com/dotnet/runtime:10.0-noble
WORKDIR /app
COPY --from=build /app .
USER app
ENTRYPOINT ["dotnet", "ReapSeek.Simulator.dll"]
