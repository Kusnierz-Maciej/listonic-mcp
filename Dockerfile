FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src
COPY src/ ./src/
COPY global.json ./
RUN dotnet publish src/ListonicMcp/ListonicMcp.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine
WORKDIR /app
COPY --from=build /app/publish ./
ENTRYPOINT ["dotnet", "ListonicMcp.dll"]
