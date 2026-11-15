FROM mcr.microsoft.com/dotnet/sdk:10.0-noble-aot AS build
WORKDIR /src
COPY Directory.Build.props ./
COPY ReapSeek.Domain/ReapSeek.Domain.csproj ReapSeek.Domain/
COPY ReapSeek.Shared/ReapSeek.Shared.csproj ReapSeek.Shared/
COPY ReapSeek.Modules/ReapSeek.Modules.csproj ReapSeek.Modules/
COPY Modules/Engagement/ReapSeek.Engagement.Contracts/ReapSeek.Engagement.Contracts.csproj Modules/Engagement/ReapSeek.Engagement.Contracts/
COPY BuildingBlocks/ReapSeek.BuildingBlocks.Web/ReapSeek.BuildingBlocks.Web.csproj BuildingBlocks/ReapSeek.BuildingBlocks.Web/
COPY Modules/Engagement/ReapSeek.Engagement/ReapSeek.Engagement.csproj Modules/Engagement/ReapSeek.Engagement/
COPY ReapSeek.Api/ReapSeek.Api.csproj ReapSeek.Api/
RUN dotnet restore ReapSeek.Api/ReapSeek.Api.csproj -r linux-x64
COPY ReapSeek.Domain/ ReapSeek.Domain/
COPY ReapSeek.Shared/ ReapSeek.Shared/
COPY ReapSeek.Modules/ ReapSeek.Modules/
COPY BuildingBlocks/ BuildingBlocks/
COPY Modules/ Modules/
COPY ReapSeek.Api/ ReapSeek.Api/
RUN dotnet publish ReapSeek.Api/ReapSeek.Api.csproj -c Release -r linux-x64 --no-restore -o /app

FROM mcr.microsoft.com/dotnet/runtime-deps:10.0-noble
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
RUN mkdir -p /data/uploads && chown app /data/uploads
USER app
ENTRYPOINT ["./ReapSeek.Api"]
