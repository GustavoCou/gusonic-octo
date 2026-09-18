# Gusonic custom Octo
# Base: winters27/octo release 2026.09.14
#
# Adds Deezer playlist discovery as metadata only.
# Audio stays Octo-native:
#   Play external track -> YouTube preview
#   Heart external track -> Soulseek/Lidarr

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build

ARG OCTO_REPO=https://github.com/winters27/octo.git
ARG OCTO_REF=2579262a5d5ac9ed0b8a11c208e71e5059d71d95

RUN apt-get update \
    && apt-get install -y --no-install-recommends git python3 ca-certificates \
    && rm -rf /var/lib/apt/lists/*

RUN git clone --no-tags "$OCTO_REPO" /src \
    && cd /src \
    && git checkout "$OCTO_REF"

COPY apply_gusonic_playlists.py /tmp/apply_gusonic_playlists.py
RUN python3 /tmp/apply_gusonic_playlists.py /src

WORKDIR /src

RUN dotnet restore octo.sln
RUN dotnet test octo.Tests/octo.Tests.csproj -c Release --no-restore
RUN dotnet publish octo/octo.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:9.0

WORKDIR /app

RUN apt-get update \
    && apt-get install -y --no-install-recommends ffmpeg fonts-dejavu-core \
    && rm -rf /var/lib/apt/lists/* \
    && mkdir -p /app/downloads

COPY --from=build /app/publish .

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080

LABEL org.opencontainers.image.source="https://github.com/GustavoCou/gusonic-octo"
LABEL org.opencontainers.image.title="Gusonic Octo"
LABEL org.opencontainers.image.description="Octo 2026.09.14 + Deezer external playlist discovery"
LABEL org.opencontainers.image.licenses="GPL-3.0"
LABEL gusonic.feature.deezer-playlists="true"

ENTRYPOINT ["dotnet", "octo.dll"]
