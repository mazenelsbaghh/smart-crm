FROM mwader/static-ffmpeg:7.1 AS ffmpeg

FROM mcr.microsoft.com/dotnet/sdk:9.0-bookworm-slim AS build
USER root
WORKDIR /src

# Copy csproj and restore
COPY backend.csproj ./
RUN dotnet restore

# Copy everything else and build
COPY . ./
RUN dotnet publish -c Release -o /app/out

# Build runtime image
FROM mcr.microsoft.com/dotnet/aspnet:9.0-bookworm-slim AS runtime
USER root
WORKDIR /app
COPY --from=build /app/out .
COPY --from=ffmpeg /ffmpeg /usr/local/bin/ffmpeg
COPY --from=ffmpeg /ffprobe /usr/local/bin/ffprobe
EXPOSE 5000
ENTRYPOINT ["dotnet", "backend.dll"]
