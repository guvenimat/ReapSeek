FROM mcr.microsoft.com/dotnet/sdk:10.0-noble-aot AS build
WORKDIR /src
COPY Directory.Build.props ./
COPY ReapSeek.Shared/ReapSeek.Shared.csproj ReapSeek.Shared/
COPY ReapSeek.Worker/ReapSeek.Worker.csproj ReapSeek.Worker/
RUN dotnet restore ReapSeek.Worker/ReapSeek.Worker.csproj -r linux-x64
COPY ReapSeek.Shared/ ReapSeek.Shared/
COPY ReapSeek.Worker/ ReapSeek.Worker/
RUN dotnet publish ReapSeek.Worker/ReapSeek.Worker.csproj -c Release -r linux-x64 --no-restore -o /app

FROM mcr.microsoft.com/dotnet/runtime-deps:10.0-noble
WORKDIR /app
COPY --from=build /app .
USER app
ENTRYPOINT ["./ReapSeek.Worker"]
