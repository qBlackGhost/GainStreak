# The social server. Build from the repository root, since it needs Core, Social and Contracts:
#   docker build -f src/GymApp.Server/Dockerfile -t gymapp-server .
# Run with the settings as environment variables (see "Running the server" in README.md):
#   docker run -p 8080:8080 -e ConnectionStrings__Social=... -e AllowedHosts=api.example.com gymapp-server
# Apply migrations first, with the same image:
#   docker run --rm -e ConnectionStrings__Social=... gymapp-server migrate

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore on the project files alone, so a code change doesn't redo the package download
COPY Directory.Build.props ./
COPY src/GymApp.Core/GymApp.Core.csproj src/GymApp.Core/
COPY src/GymApp.Social/GymApp.Social.csproj src/GymApp.Social/
COPY src/GymApp.Contracts/GymApp.Contracts.csproj src/GymApp.Contracts/
COPY src/GymApp.Server/GymApp.Server.csproj src/GymApp.Server/
RUN dotnet restore src/GymApp.Server/GymApp.Server.csproj

COPY src/GymApp.Core/ src/GymApp.Core/
COPY src/GymApp.Social/ src/GymApp.Social/
COPY src/GymApp.Contracts/ src/GymApp.Contracts/
COPY src/GymApp.Server/ src/GymApp.Server/
RUN dotnet publish src/GymApp.Server/GymApp.Server.csproj -c Release -o /app --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app ./
# The image's built-in non-root user; it can't write to /app
USER $APP_UID
ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "GymApp.Server.dll"]
