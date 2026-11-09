# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1

COPY global.json ./
COPY src/AcaTime.ItcArtifact.sln ./src/
COPY src/ga ./src/ga
COPY src/runner ./src/runner

RUN dotnet restore src/AcaTime.ItcArtifact.sln
RUN dotnet test src/AcaTime.ItcArtifact.sln -c Release --no-restore
RUN dotnet publish src/runner/AcaTime.ItcRunner.csproj -c Release --no-restore -o /out/runner


FROM mcr.microsoft.com/dotnet/runtime:9.0

RUN apt-get update \
    && apt-get install -y --no-install-recommends python3 curl ca-certificates \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /artifact
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1

COPY --from=build /out/runner ./bin/runner
COPY src/bridge ./src/bridge
COPY src/scorer ./src/scorer
COPY scripts ./scripts
COPY results ./results
COPY experiments ./experiments
COPY docs ./docs
COPY data/README.md data/CHECKSUMS.sha256 data/download.sh ./data/
COPY Makefile README.md LICENSE LICENSES.md CITATION.cff THIRD_PARTY_NOTICES.md ./

# The default action verifies the artifact: fetch the public DTU instances,
# then re-score every curated solution with the independent scorer.
CMD ["bash", "-lc", "./data/download.sh && python3 scripts/verify_results.py"]
