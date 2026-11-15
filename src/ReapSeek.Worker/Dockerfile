FROM mcr.microsoft.com/dotnet/sdk:10.0-noble-aot AS build
WORKDIR /src
COPY Directory.Build.props ./
COPY ReapSeek.Domain/ReapSeek.Domain.csproj ReapSeek.Domain/
COPY ReapSeek.Shared/ReapSeek.Shared.csproj ReapSeek.Shared/
COPY ReapSeek.Modules/ReapSeek.Modules.csproj ReapSeek.Modules/
COPY Modules/Engagement/ReapSeek.Engagement.Contracts/ReapSeek.Engagement.Contracts.csproj Modules/Engagement/ReapSeek.Engagement.Contracts/
COPY Modules/Marketing/ReapSeek.Marketing.Contracts/ReapSeek.Marketing.Contracts.csproj Modules/Marketing/ReapSeek.Marketing.Contracts/
COPY Modules/Finance/ReapSeek.Finance.Contracts/ReapSeek.Finance.Contracts.csproj Modules/Finance/ReapSeek.Finance.Contracts/
COPY BuildingBlocks/ReapSeek.BuildingBlocks/ReapSeek.BuildingBlocks.csproj BuildingBlocks/ReapSeek.BuildingBlocks/
COPY ReapSeek.Worker/ReapSeek.Worker.csproj ReapSeek.Worker/
RUN dotnet restore ReapSeek.Worker/ReapSeek.Worker.csproj -r linux-x64
COPY ReapSeek.Domain/ ReapSeek.Domain/
COPY ReapSeek.Shared/ ReapSeek.Shared/
COPY ReapSeek.Modules/ ReapSeek.Modules/
COPY Modules/Engagement/ReapSeek.Engagement.Contracts/ Modules/Engagement/ReapSeek.Engagement.Contracts/
COPY Modules/Marketing/ReapSeek.Marketing.Contracts/ Modules/Marketing/ReapSeek.Marketing.Contracts/
COPY Modules/Finance/ReapSeek.Finance.Contracts/ Modules/Finance/ReapSeek.Finance.Contracts/
COPY BuildingBlocks/ReapSeek.BuildingBlocks/ BuildingBlocks/ReapSeek.BuildingBlocks/
COPY ReapSeek.Worker/ ReapSeek.Worker/
RUN dotnet publish ReapSeek.Worker/ReapSeek.Worker.csproj -c Release -r linux-x64 --no-restore -o /app

FROM mcr.microsoft.com/dotnet/runtime-deps:10.0-noble
WORKDIR /app
COPY --from=build /app .
USER app
ENTRYPOINT ["./ReapSeek.Worker"]
