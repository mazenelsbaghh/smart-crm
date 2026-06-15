FROM registry.access.redhat.com/ubi8/dotnet-90:latest AS build
USER root
WORKDIR /src

# Copy csproj and restore
COPY backend.csproj ./
RUN dotnet restore

# Copy everything else and build
COPY . ./
RUN dotnet publish -c Release -o /app/out

# Build runtime image
FROM registry.access.redhat.com/ubi8/dotnet-90-runtime:latest AS runtime
USER root
WORKDIR /app
COPY --from=build /app/out .
EXPOSE 5000
ENTRYPOINT ["dotnet", "backend.dll"]
