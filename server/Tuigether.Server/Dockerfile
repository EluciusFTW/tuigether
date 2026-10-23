# Build context: the repository root, because the build needs Directory.Build.props from there.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source
COPY Directory.Build.props .editorconfig ./
COPY server/Tuigether.Contracts/Tuigether.Contracts.csproj server/Tuigether.Contracts/
COPY server/Tuigether.Server/Tuigether.Server.csproj server/Tuigether.Server/
RUN dotnet restore server/Tuigether.Server/Tuigether.Server.csproj
COPY server/Tuigether.Contracts/ server/Tuigether.Contracts/
COPY server/Tuigether.Server/ server/Tuigether.Server/
RUN dotnet publish server/Tuigether.Server/Tuigether.Server.csproj --configuration Release --no-restore --output /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
EXPOSE 8080
ENTRYPOINT ["dotnet", "Tuigether.Server.dll"]
