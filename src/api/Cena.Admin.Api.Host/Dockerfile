FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/api/Cena.Admin.Api.Host/Cena.Admin.Api.Host.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
EXPOSE 5052
ENTRYPOINT ["dotnet", "Cena.Admin.Api.Host.dll"]
