# TownHall — a Fusion live-audience Q&A sample, deployed at townhall.actuallab.net.
#
# A lean, self-contained website image: it publishes only TownHall.Host (+ its
# project references, including the TownHall.UI WASM client) instead of the whole
# solution, which keeps the build fast. This mirrors the Fusion.Samples web_blazor
# target and is used by deploy/docker-compose.prod.yml.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS web_build
# python3 + libatomic1 are needed by the WASM native relinking during Release publish
RUN apt-get update \
    && apt-get install -y --no-install-recommends python3 libatomic1 \
    && rm -rf /var/lib/apt/lists/*
RUN dotnet workload install wasm-tools
WORKDIR /src
COPY ["global.json", "."]
COPY ["*.props", "."]
COPY ["src/", "src/"]
RUN dotnet publish -c:Release -o /publish src/TownHall.Host/TownHall.Host.csproj

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS web_townhall
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=true
ENV Logging__Console__FormatterName=
WORKDIR /app
COPY --from=web_build /publish .
ENTRYPOINT ["dotnet", "TownHall.Host.dll"]
