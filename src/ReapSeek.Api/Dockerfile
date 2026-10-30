FROM mcr.microsoft.com/dotnet/sdk:10.0-noble-aot AS build
WORKDIR /src
COPY Directory.Build.props ./
COPY ReapSeek.Shared/ReapSeek.Shared.csproj ReapSeek.Shared/
COPY ReapSeek.Api/ReapSeek.Api.csproj ReapSeek.Api/
RUN dotnet restore ReapSeek.Api/ReapSeek.Api.csproj -r linux-x64
COPY ReapSeek.Shared/ ReapSeek.Shared/
COPY ReapSeek.Api/ ReapSeek.Api/
RUN dotnet publish ReapSeek.Api/ReapSeek.Api.csproj -c Release -r linux-x64 --no-restore -o /app

FROM mcr.microsoft.com/dotnet/runtime-deps:10.0-noble
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
USER app
ENTRYPOINT ["./ReapSeek.Api"]
